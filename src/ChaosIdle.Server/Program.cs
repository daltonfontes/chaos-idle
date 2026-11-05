using System.Text.RegularExpressions;
using ChaosIdle.Content;
using ChaosIdle.Domain;
using ChaosIdle.Persistence;
using ChaosIdle.Server;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Offline;

var builder = WebApplication.CreateBuilder(args);

// PostgreSQL when a connection string is configured (ConnectionStrings__Postgres), else in memory.
string? postgres = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(postgres))
    builder.Services.AddSingleton<IServerStore, InMemoryServerStore>();
else
    builder.Services.AddSingleton<IServerStore>(_ => PostgresServerStore.Open(postgres));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ContentRegistry>(ContentDatabase.Default);
builder.Services.AddSingleton(new SaveSerializer());
builder.Services.AddSingleton(SaveRules.Default);
builder.Services.AddSingleton(new OfflineCalculator(new LootGenerator(ItemBases.All, Affixes.All, uniques: Uniques.All)));
builder.Services.AddSingleton<GameService>();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.Services.GetRequiredService<IServerStore>(); // connect and migrate at startup, not on the first request
app.MapGameApi();
app.Run();

public partial class Program;

namespace ChaosIdle.Server
{
    public sealed record CreateAccountResponse(string AccountId, string Token);

    public sealed record StartOfflineRequest(string MapId, IReadOnlyList<string>? ModifierIds);

    public sealed record TelemetryBatch(IReadOnlyList<TelemetryIn>? Events);

    public sealed record TelemetryIn(string Name, DateTimeOffset At, Dictionary<string, string>? Properties);

    public static partial class Api
    {
        public const int MaxTelemetryEvents = 100;

        [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$")]
        private static partial Regex SlotPattern();

        [GeneratedRegex("^[a-z][a-z0-9_.]{0,63}$")]
        private static partial Regex EventNamePattern();

        public static void MapGameApi(this WebApplication app)
        {
            app.MapGet("/health", (IServerStore store) => Results.Ok(new { status = "ok", store = store is PostgresServerStore ? "postgres" : "memory" }));

            app.MapPost("/v1/accounts", (IServerStore store, TimeProvider time) =>
            {
                string token = Auth.NewToken();
                var account = new Account("acc_" + Guid.NewGuid().ToString("N"), Auth.Hash(token), time.GetUtcNow());
                store.AddAccount(account);
                return Results.Created($"/v1/accounts/{account.Id}", new CreateAccountResponse(account.Id, token));
            });

            var v1 = app.MapGroup("/v1").AddEndpointFilter(Auth.RequireAccount);

            v1.MapGet("/saves/{slot}", (string slot, HttpContext http, IServerStore store) =>
            {
                if (!SlotPattern().IsMatch(slot)) return Results.Problem("Invalid slot name.", statusCode: 400);
                var save = store.GetSave(http.Account().Id, slot);
                if (save == null) return Results.NotFound();
                http.Response.Headers.ETag = $"\"{save.Revision}\"";
                return Results.Text(save.Text, "application/json");
            });

            v1.MapPut("/saves/{slot}", async (string slot, HttpContext http, GameService game) =>
            {
                if (!SlotPattern().IsMatch(slot)) return Results.Problem("Invalid slot name.", statusCode: 400);
                if (!long.TryParse(http.Request.Headers["X-Base-Revision"], out long baseRevision) || baseRevision < 0)
                    return Results.Problem("X-Base-Revision header (the server revision this save is based on, 0 for none) is required.", statusCode: 400);

                if (http.Request.ContentLength > GameService.MaxSaveBytes) return Results.Problem("Save is too large.", statusCode: 413);
                using var reader = new StreamReader(http.Request.Body);
                var buffer = new char[GameService.MaxSaveBytes + 1];
                int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                if (read > GameService.MaxSaveBytes) return Results.Problem("Save is too large.", statusCode: 413);

                return ToHttp(game.PutSave(http.Account(), slot, baseRevision, new string(buffer, 0, read)));
            });

            v1.MapPost("/saves/{slot}/offline", (string slot, StartOfflineRequest request, HttpContext http, GameService game) =>
            {
                if (!SlotPattern().IsMatch(slot)) return Results.Problem("Invalid slot name.", statusCode: 400);
                var result = game.StartOffline(http.Account(), slot, request.MapId, request.ModifierIds ?? Array.Empty<string>());
                return result.Outcome == Outcome.Ok ? Results.Created($"/v1/offline/{result.Value!.SessionId}", result.Value) : ToHttp(result);
            });

            v1.MapPost("/offline/{sessionId}/claim", (string sessionId, HttpContext http, GameService game) =>
                ToHttp(game.Claim(http.Account(), sessionId, http.Request.Headers["Idempotency-Key"].ToString())));

            v1.MapPost("/telemetry", (TelemetryBatch batch, HttpContext http, IServerStore store, TimeProvider time) =>
            {
                var events = batch.Events ?? Array.Empty<TelemetryIn>();
                if (events.Count > MaxTelemetryEvents) return Results.Problem($"At most {MaxTelemetryEvents} events per batch.", statusCode: 400);
                foreach (var e in events)
                {
                    if (!EventNamePattern().IsMatch(e.Name ?? "")) return Results.Problem($"Invalid event name '{e.Name}'.", statusCode: 400);
                    if (e.Properties is { Count: > 20 } || e.Properties?.Any(p => p.Key.Length > 64 || p.Value.Length > 256) == true)
                        return Results.Problem("Too many or too long properties.", statusCode: 400);
                }

                var now = time.GetUtcNow();
                store.AddTelemetry(events.Select(e => new TelemetryEvent(http.Account().Id, e.Name, e.At > now ? now : e.At,
                    (IReadOnlyDictionary<string, string>?)e.Properties ?? new Dictionary<string, string>())));
                return Results.Accepted();
            });
        }

        private static IResult ToHttp<T>(ServiceResult<T> r) => r.Outcome switch
        {
            Outcome.Ok => Results.Ok(r.Value),
            Outcome.NotFound => Results.Problem(r.Error, statusCode: 404),
            Outcome.Forbidden => Results.Problem(r.Error, statusCode: 403),
            Outcome.Conflict => Results.Json(new { title = r.Error, status = 409, details = r.Details, current = r.Value }, statusCode: 409),
            Outcome.Invalid => Results.Json(new { title = r.Error, status = 422, details = r.Details }, statusCode: 422),
            _ => Results.Json(new { title = r.Error, status = 400, details = r.Details }, statusCode: 400),
        };
    }
}

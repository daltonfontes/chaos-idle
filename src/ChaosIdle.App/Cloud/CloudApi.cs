using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChaosIdle.App.Cloud
{
    public enum CallStatus
    {
        Ok,
        /// <summary>No connection, a timeout, or a server error: try again later.</summary>
        Offline,
        /// <summary>The token is unknown or signed out.</summary>
        SignedOut,
        NotFound,
        Conflict,
        /// <summary>The server refused the request (bad or impossible save, locked map...).</summary>
        Refused,
    }

    public sealed class CallResult<T>
    {
        public CallResult(CallStatus status, T? value, string message = "", JsonElement? body = null)
        {
            Status = status;
            Value = value;
            Message = message;
            Body = body;
        }

        public CallStatus Status { get; }
        public T? Value { get; }
        public string Message { get; }

        /// <summary>The JSON error body, when the server sent one (e.g. a conflict's details).</summary>
        public JsonElement? Body { get; }

        public bool Ok => Status == CallStatus.Ok;
    }

    public sealed class GuestAccount
    {
        public string AccountId { get; set; } = "";
        public string Token { get; set; } = "";
    }

    public sealed class CloudSaveText
    {
        public CloudSaveText(string text, long revision)
        {
            Text = text;
            Revision = revision;
        }

        public string Text { get; }
        public long Revision { get; }
    }

    public sealed class SaveAcceptedDto
    {
        public long Revision { get; set; }
    }

    public sealed class OfflineStartedDto
    {
        public string SessionId { get; set; } = "";
        public DateTimeOffset StartedAtUtc { get; set; }
        public long Revision { get; set; }
    }

    public sealed class OfflineClaimedDto
    {
        public string SessionId { get; set; } = "";
        public long Revision { get; set; }
        public double CreditedHours { get; set; }
        public int RunsCompleted { get; set; }
        public int ItemsFound { get; set; }
        public List<string> Report { get; set; } = new List<string>();
    }

    /// <summary>
    /// The game server's HTTP API (docs/backend.md), for the client. Expected failures come back
    /// as a <see cref="CallStatus"/>, never as exceptions, so a dropped connection can't crash the game.
    /// </summary>
    public sealed class CloudApi
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;

        /// <param name="http">With <see cref="HttpClient.BaseAddress"/> set to the server.</param>
        public CloudApi(HttpClient http) => _http = http;

        public string? Token { get; set; }

        public Task<CallResult<GuestAccount>> CreateGuestAsync(CancellationToken cancel = default) =>
            SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "v1/accounts"), ReadJson<GuestAccount>, cancel, authorized: false);

        public Task<CallResult<CloudSaveText>> GetSaveAsync(string slot, CancellationToken cancel = default) =>
            SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"v1/saves/{slot}"), async res =>
            {
                string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                string tag = res.Headers.ETag?.Tag?.Trim('"') ?? "0";
                return new CloudSaveText(text, long.TryParse(tag, out long r) ? r : 0);
            }, cancel);

        public Task<CallResult<SaveAcceptedDto>> PutSaveAsync(string slot, string text, long baseRevision, CancellationToken cancel = default) =>
            SendAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, $"v1/saves/{slot}") { Content = new StringContent(text, Encoding.UTF8, "application/json") };
                req.Headers.Add("X-Base-Revision", baseRevision.ToString());
                return req;
            }, ReadJson<SaveAcceptedDto>, cancel);

        public Task<CallResult<OfflineStartedDto>> StartOfflineAsync(string slot, string mapId, IReadOnlyList<string> modifierIds, CancellationToken cancel = default) =>
            SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"v1/saves/{slot}/offline")
            {
                Content = JsonBody(new { mapId, modifierIds }),
            }, ReadJson<OfflineStartedDto>, cancel);

        /// <param name="idempotencyKey">The same key for every retry of one claim, so a lost answer can be asked for again.</param>
        public Task<CallResult<OfflineClaimedDto>> ClaimAsync(string sessionId, string idempotencyKey, CancellationToken cancel = default) =>
            SendAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"v1/offline/{sessionId}/claim");
                req.Headers.Add("Idempotency-Key", idempotencyKey);
                return req;
            }, ReadJson<OfflineClaimedDto>, cancel);

        private static StringContent JsonBody(object value) =>
            new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json");

        private static async Task<T> ReadJson<T>(HttpResponseMessage res) =>
            JsonSerializer.Deserialize<T>(await res.Content.ReadAsStringAsync().ConfigureAwait(false), Json)!;

        private async Task<CallResult<T>> SendAsync<T>(Func<HttpRequestMessage> build, Func<HttpResponseMessage, Task<T>> read,
            CancellationToken cancel, bool authorized = true)
        {
            try
            {
                using var req = build();
                if (authorized)
                {
                    if (string.IsNullOrEmpty(Token)) return new CallResult<T>(CallStatus.SignedOut, default, "Not connected to the cloud.");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                }

                using var res = await _http.SendAsync(req, cancel).ConfigureAwait(false);
                if (res.IsSuccessStatusCode) return new CallResult<T>(CallStatus.Ok, await read(res).ConfigureAwait(false));

                var (message, body) = await ReadProblem(res).ConfigureAwait(false);
                var status = res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => CallStatus.SignedOut,
                    HttpStatusCode.NotFound => CallStatus.NotFound,
                    HttpStatusCode.Conflict => CallStatus.Conflict,
                    var code when (int)code >= 500 || code == HttpStatusCode.RequestTimeout || (int)code == 429 => CallStatus.Offline,
                    _ => CallStatus.Refused,
                };
                return new CallResult<T>(status, default, message, body);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is OperationCanceledException || e is JsonException)
            {
                return new CallResult<T>(CallStatus.Offline, default, "Can't reach the cloud right now.");
            }
        }

        private static async Task<(string Message, JsonElement? Body)> ReadProblem(HttpResponseMessage res)
        {
            string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement.Clone();
                string message = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()!
                    : root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()!
                    : res.ReasonPhrase ?? "";
                return (message, root);
            }
            catch (JsonException)
            {
                return (res.ReasonPhrase ?? "", null);
            }
        }
    }
}

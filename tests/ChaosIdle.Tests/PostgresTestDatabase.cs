using Npgsql;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>
/// A fresh, empty PostgreSQL database for one test, dropped on dispose. The server is given by
/// CHAOSIDLE_TEST_POSTGRES (a connection string for a role that may create databases). Without
/// it, PostgreSQL tests are skipped, unless CHAOSIDLE_REQUIRE_POSTGRES=1 (set in CI), where a
/// missing database is a failure so the tests can never be skipped silently.
/// </summary>
public sealed class PostgresTestDatabase : IDisposable
{
    private readonly string _admin;
    private readonly string _name;

    public PostgresTestDatabase()
    {
        string? admin = Environment.GetEnvironmentVariable("CHAOSIDLE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(admin))
        {
            if (Environment.GetEnvironmentVariable("CHAOSIDLE_REQUIRE_POSTGRES") == "1")
                throw new InvalidOperationException("CHAOSIDLE_REQUIRE_POSTGRES=1 but CHAOSIDLE_TEST_POSTGRES is not set.");
            throw new SkipException("Set CHAOSIDLE_TEST_POSTGRES to run the PostgreSQL tests.");
        }

        _admin = admin;
        _name = "chaosidle_t_" + Guid.NewGuid().ToString("N")[..16];
        Admin($"create database {_name}");
        ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        Admin($"drop database if exists {_name} with (force)");
    }

    private void Admin(string sql)
    {
        using var connection = new NpgsqlConnection(_admin);
        connection.Open();
        using var cmd = new NpgsqlCommand(sql, connection);
        cmd.ExecuteNonQuery();
    }
}

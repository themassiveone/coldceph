using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Integrity.Interfaces;
using Microsoft.Data.Sqlite;

namespace ColdCeph.Control.Features.Integrity.Repositories;

public sealed class SqliteIntegrityRepository : IIntegrityRepository
{
    private readonly string _connectionString;

    public SqliteIntegrityRepository(ControlConfig config)
    {
        Directory.CreateDirectory(config.DataDirectory);
        _connectionString = $"Data Source={Path.Join(config.DataDirectory, "integrity.db")}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS verified_clean (
                Id INTEGER PRIMARY KEY,
                At TEXT NOT NULL,
                Summary TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public DateTimeOffset? GetLastVerifiedCleanAt()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT At FROM verified_clean WHERE Id = 1";
        var value = command.ExecuteScalar() as string;
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    public string? GetLastVerifiedCleanSummary()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Summary FROM verified_clean WHERE Id = 1";
        return command.ExecuteScalar() as string;
    }

    public void SaveVerifiedClean(DateTimeOffset at, string summary)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO verified_clean (Id, At, Summary) VALUES (1, $at, $summary)
            ON CONFLICT(Id) DO UPDATE SET At = $at, Summary = $summary
            """;
        command.Parameters.AddWithValue("$at", at.ToString("O"));
        command.Parameters.AddWithValue("$summary", summary);
        command.ExecuteNonQuery();
    }
}

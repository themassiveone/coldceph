using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Operations.Interfaces;
using ColdCeph.Core.Features.Operations.DTOs;
using Microsoft.Data.Sqlite;

namespace ColdCeph.Control.Features.Operations.Repositories;

public sealed class SqliteOperationsRepository : IOperationsRepository
{
    private readonly string _connectionString;

    public SqliteOperationsRepository(ControlConfig config)
    {
        Directory.CreateDirectory(config.DataDirectory);
        _connectionString = $"Data Source={Path.Join(config.DataDirectory, "operations.db")}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS operations (
                OperationId TEXT PRIMARY KEY,
                Kind TEXT NOT NULL,
                Initiator TEXT NOT NULL,
                Summary TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT
            );
            CREATE TABLE IF NOT EXISTS events (
                EventId TEXT PRIMARY KEY,
                OperationId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Detail TEXT NOT NULL,
                At TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Append(OperationRecordDto operation)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO operations (OperationId, Kind, Initiator, Summary, StartedAt, CompletedAt)
            VALUES ($id, $kind, $init, $summary, $start, $end)
            """;
        command.Parameters.AddWithValue("$id", operation.OperationId);
        command.Parameters.AddWithValue("$kind", operation.Kind);
        command.Parameters.AddWithValue("$init", operation.Initiator);
        command.Parameters.AddWithValue("$summary", operation.Summary);
        command.Parameters.AddWithValue("$start", operation.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$end", (object?)operation.CompletedAt?.ToString("O") ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void AppendEvent(AuditEventDto auditEvent)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events (EventId, OperationId, Kind, Detail, At)
            VALUES ($id, $op, $kind, $detail, $at)
            """;
        command.Parameters.AddWithValue("$id", auditEvent.EventId);
        command.Parameters.AddWithValue("$op", auditEvent.OperationId);
        command.Parameters.AddWithValue("$kind", auditEvent.Kind);
        command.Parameters.AddWithValue("$detail", auditEvent.Detail);
        command.Parameters.AddWithValue("$at", auditEvent.At.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<OperationRecordDto> ListOperations()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT OperationId, Kind, Initiator, Summary, StartedAt, CompletedAt FROM operations ORDER BY StartedAt DESC";
        using var reader = command.ExecuteReader();
        var items = new List<OperationRecordDto>();
        while (reader.Read())
        {
            items.Add(new OperationRecordDto
            {
                OperationId = reader.GetString(0),
                Kind = reader.GetString(1),
                Initiator = reader.GetString(2),
                Summary = reader.GetString(3),
                StartedAt = DateTimeOffset.Parse(reader.GetString(4)),
                CompletedAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))
            });
        }

        return items;
    }

    public IReadOnlyList<AuditEventDto> ListEvents()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EventId, OperationId, Kind, Detail, At FROM events ORDER BY At DESC";
        using var reader = command.ExecuteReader();
        var items = new List<AuditEventDto>();
        while (reader.Read())
        {
            items.Add(new AuditEventDto
            {
                EventId = reader.GetString(0),
                OperationId = reader.GetString(1),
                Kind = reader.GetString(2),
                Detail = reader.GetString(3),
                At = DateTimeOffset.Parse(reader.GetString(4))
            });
        }

        return items;
    }
}

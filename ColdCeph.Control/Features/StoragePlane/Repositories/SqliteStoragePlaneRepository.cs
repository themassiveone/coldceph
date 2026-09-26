using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.StoragePlane.Interfaces;
using ColdCeph.Control.Features.StoragePlane.Models;
using ColdCeph.Core.Features.StoragePlane.DTOs;
using Microsoft.Data.Sqlite;

namespace ColdCeph.Control.Features.StoragePlane.Repositories;

public sealed class SqliteStoragePlaneRepository : IStoragePlaneRepository
{
    private readonly string _connectionString;

    public SqliteStoragePlaneRepository(ControlConfig config)
    {
        Directory.CreateDirectory(config.DataDirectory);
        _connectionString = $"Data Source={Path.Join(config.DataDirectory, "storage-plane.db")}";
        EnsureSchema();
    }

    public StoragePlaneRecord Load()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT State, LeaseId, LeaseHolder, ActiveOperationId, LeaseDeadline, JournalStep, LastReadyAt, LastTransitionAt, RealityReconciled
            FROM storage_plane WHERE Id = 1
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return new StoragePlaneRecord();

        var record = new StoragePlaneRecord
        {
            State = Enum.Parse<StoragePlaneState>(reader.GetString(0)),
            LeaseId = reader.IsDBNull(1) ? null : reader.GetString(1),
            LeaseHolder = reader.IsDBNull(2) ? null : reader.GetString(2),
            ActiveOperationId = reader.IsDBNull(3) ? null : reader.GetString(3),
            LeaseDeadline = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
            JournalStep = reader.GetString(5),
            LastReadyAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            LastTransitionAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            RealityReconciled = reader.GetInt32(8) == 1
        };
        reader.Close();

        using var noout = connection.CreateCommand();
        noout.CommandText = "SELECT Scope, OperationId, ControllerInstance, SetAt, PreviousState FROM noout_records";
        using var nooutReader = noout.ExecuteReader();
        while (nooutReader.Read())
        {
            record.OwnedNoout.Add(new NooutRecordDto
            {
                Scope = nooutReader.GetString(0),
                OperationId = nooutReader.GetString(1),
                ControllerInstance = nooutReader.GetString(2),
                SetAt = DateTimeOffset.Parse(nooutReader.GetString(3)),
                PreviousState = nooutReader.GetInt32(4) == 1
            });
        }

        return record;
    }

    public void Save(StoragePlaneRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO storage_plane (Id, State, LeaseId, LeaseHolder, ActiveOperationId, LeaseDeadline, JournalStep, LastReadyAt, LastTransitionAt, RealityReconciled)
                VALUES (1, $state, $leaseId, $holder, $op, $deadline, $step, $ready, $transition, $trusted)
                ON CONFLICT(Id) DO UPDATE SET
                    State = $state, LeaseId = $leaseId, LeaseHolder = $holder, ActiveOperationId = $op,
                    LeaseDeadline = $deadline, JournalStep = $step, LastReadyAt = $ready, LastTransitionAt = $transition,
                    RealityReconciled = $trusted
                """;
            command.Parameters.AddWithValue("$state", record.State.ToString());
            command.Parameters.AddWithValue("$leaseId", (object?)record.LeaseId ?? DBNull.Value);
            command.Parameters.AddWithValue("$holder", (object?)record.LeaseHolder ?? DBNull.Value);
            command.Parameters.AddWithValue("$op", (object?)record.ActiveOperationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$deadline", (object?)record.LeaseDeadline?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$step", record.JournalStep);
            command.Parameters.AddWithValue("$ready", (object?)record.LastReadyAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$transition", (object?)record.LastTransitionAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$trusted", record.RealityReconciled ? 1 : 0);
            command.ExecuteNonQuery();
        }

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM noout_records";
            clear.ExecuteNonQuery();
        }

        foreach (var owned in record.OwnedNoout)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO noout_records (Scope, OperationId, ControllerInstance, SetAt, PreviousState)
                VALUES ($scope, $op, $controller, $at, $prev)
                """;
            insert.Parameters.AddWithValue("$scope", owned.Scope);
            insert.Parameters.AddWithValue("$op", owned.OperationId);
            insert.Parameters.AddWithValue("$controller", owned.ControllerInstance);
            insert.Parameters.AddWithValue("$at", owned.SetAt.ToString("O"));
            insert.Parameters.AddWithValue("$prev", owned.PreviousState ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS storage_plane (
                Id INTEGER PRIMARY KEY,
                State TEXT NOT NULL,
                LeaseId TEXT,
                LeaseHolder TEXT,
                ActiveOperationId TEXT,
                LeaseDeadline TEXT,
                JournalStep TEXT NOT NULL,
                LastReadyAt TEXT,
                LastTransitionAt TEXT,
                RealityReconciled INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS noout_records (
                Scope TEXT PRIMARY KEY,
                OperationId TEXT NOT NULL,
                ControllerInstance TEXT NOT NULL,
                SetAt TEXT NOT NULL,
                PreviousState INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}

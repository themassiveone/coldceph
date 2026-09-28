using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Osds.Interfaces;
using ColdCeph.Core.Features.Osds.DTOs;
using Microsoft.Data.Sqlite;

namespace ColdCeph.Control.Features.Osds.Repositories;

public sealed class SqliteOsdsObservationRepository : IOsdsObservationRepository
{
    private readonly string _connectionString;

    public SqliteOsdsObservationRepository(ControlConfig config)
    {
        Directory.CreateDirectory(config.DataDirectory);
        _connectionString = $"Data Source={Path.Join(config.DataDirectory, "osds.db")}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS osds (
                HostId TEXT NOT NULL,
                OsdId INTEGER NOT NULL,
                DeviceId TEXT,
                Up INTEGER NOT NULL,
                Inn INTEGER NOT NULL,
                ProcessRunning INTEGER NOT NULL,
                PRIMARY KEY (HostId, OsdId)
            );
            CREATE TABLE IF NOT EXISTS osd_errors (
                HostId TEXT PRIMARY KEY,
                Error TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<OsdDto> LoadOsds()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT HostId, OsdId, DeviceId, Up, Inn, ProcessRunning FROM osds";
        using var reader = command.ExecuteReader();
        var osds = new List<OsdDto>();
        while (reader.Read())
        {
            osds.Add(new OsdDto
            {
                HostId = reader.GetString(0),
                OsdId = reader.GetInt32(1),
                DeviceId = reader.IsDBNull(2) ? null : reader.GetString(2),
                Up = reader.GetInt32(3) == 1,
                In = reader.GetInt32(4) == 1,
                ProcessRunning = reader.GetInt32(5) == 1
            });
        }

        return osds;
    }

    public IReadOnlyDictionary<string, string> LoadErrors()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT HostId, Error FROM osd_errors";
        using var reader = command.ExecuteReader();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
            errors[reader.GetString(0)] = reader.GetString(1);
        return errors;
    }

    public void Save(IReadOnlyList<OsdDto> osds, IReadOnlyDictionary<string, string> errors)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM osds; DELETE FROM osd_errors;";
            clear.ExecuteNonQuery();
        }

        foreach (var osd in osds)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO osds (HostId, OsdId, DeviceId, Up, Inn, ProcessRunning)
                VALUES ($host, $id, $device, $up, $inn, $running)
                """;
            insert.Parameters.AddWithValue("$host", osd.HostId);
            insert.Parameters.AddWithValue("$id", osd.OsdId);
            insert.Parameters.AddWithValue("$device", (object?)osd.DeviceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$up", osd.Up ? 1 : 0);
            insert.Parameters.AddWithValue("$inn", osd.In ? 1 : 0);
            insert.Parameters.AddWithValue("$running", osd.ProcessRunning ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        foreach (var error in errors)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO osd_errors (HostId, Error) VALUES ($host, $error)";
            insert.Parameters.AddWithValue("$host", error.Key);
            insert.Parameters.AddWithValue("$error", error.Value);
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }
}

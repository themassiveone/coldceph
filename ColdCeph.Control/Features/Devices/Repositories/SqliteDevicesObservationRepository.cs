using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Devices.Interfaces;
using ColdCeph.Core.Features.Devices.DTOs;
using Microsoft.Data.Sqlite;

namespace ColdCeph.Control.Features.Devices.Repositories;

public sealed class SqliteDevicesObservationRepository : IDevicesObservationRepository
{
    private readonly string _connectionString;

    public SqliteDevicesObservationRepository(ControlConfig config)
    {
        Directory.CreateDirectory(config.DataDirectory);
        _connectionString = $"Data Source={Path.Join(config.DataDirectory, "devices.db")}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                DeviceId TEXT PRIMARY KEY,
                HostId TEXT NOT NULL,
                MappedOsdId INTEGER,
                Wwn TEXT,
                Serial TEXT,
                Path TEXT,
                PowerState TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS device_errors (
                HostId TEXT PRIMARY KEY,
                Error TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DeviceDto> LoadDevices()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, HostId, MappedOsdId, Wwn, Serial, Path, PowerState FROM devices";
        using var reader = command.ExecuteReader();
        var devices = new List<DeviceDto>();
        while (reader.Read())
        {
            devices.Add(new DeviceDto
            {
                DeviceId = reader.GetString(0),
                HostId = reader.GetString(1),
                MappedOsdId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Wwn = reader.IsDBNull(3) ? null : reader.GetString(3),
                Serial = reader.IsDBNull(4) ? null : reader.GetString(4),
                Path = reader.IsDBNull(5) ? null : reader.GetString(5),
                PowerState = Enum.Parse<DevicePowerState>(reader.GetString(6))
            });
        }

        return devices;
    }

    public IReadOnlyDictionary<string, string> LoadErrors()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT HostId, Error FROM device_errors";
        using var reader = command.ExecuteReader();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
            errors[reader.GetString(0)] = reader.GetString(1);
        return errors;
    }

    public void Save(IReadOnlyList<DeviceDto> devices, IReadOnlyDictionary<string, string> errors)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM devices; DELETE FROM device_errors;";
            clear.ExecuteNonQuery();
        }

        foreach (var device in devices)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO devices (DeviceId, HostId, MappedOsdId, Wwn, Serial, Path, PowerState)
                VALUES ($id, $host, $osd, $wwn, $serial, $path, $power)
                """;
            insert.Parameters.AddWithValue("$id", device.DeviceId);
            insert.Parameters.AddWithValue("$host", device.HostId);
            insert.Parameters.AddWithValue("$osd", (object?)device.MappedOsdId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$wwn", (object?)device.Wwn ?? DBNull.Value);
            insert.Parameters.AddWithValue("$serial", (object?)device.Serial ?? DBNull.Value);
            insert.Parameters.AddWithValue("$path", (object?)device.Path ?? DBNull.Value);
            insert.Parameters.AddWithValue("$power", device.PowerState.ToString());
            insert.ExecuteNonQuery();
        }

        foreach (var error in errors)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO device_errors (HostId, Error) VALUES ($host, $error)";
            insert.Parameters.AddWithValue("$host", error.Key);
            insert.Parameters.AddWithValue("$error", error.Value);
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }
}

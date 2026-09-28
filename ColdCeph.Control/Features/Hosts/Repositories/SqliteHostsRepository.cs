using ColdCeph.Control.Composition;
using ColdCeph.Control.Features.Hosts.Interfaces;
using ColdCeph.Control.Features.Hosts.Models;
using ColdCeph.Core.Features.Hosts.DTOs;
using Microsoft.Data.Sqlite;

namespace ColdCeph.Control.Features.Hosts.Repositories;

public sealed class SqliteHostsRepository : IHostsRepository
{
    private readonly string _connectionString;

    public SqliteHostsRepository(ControlConfig config)
    {
        Directory.CreateDirectory(config.DataDirectory);
        _connectionString = $"Data Source={Path.Join(config.DataDirectory, "hosts.db")}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS enrolled (
                HostId TEXT PRIMARY KEY,
                Hostname TEXT NOT NULL,
                Endpoint TEXT NOT NULL,
                LastHeartbeat TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pending (
                HostId TEXT PRIMARY KEY,
                Hostname TEXT NOT NULL,
                Endpoint TEXT NOT NULL,
                RequestedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS blocked (
                HostId TEXT PRIMARY KEY,
                Hostname TEXT NOT NULL,
                Endpoint TEXT NOT NULL,
                RequestedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public HostsRecord Load()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return new HostsRecord
        {
            Hosts = LoadEnrolled(connection),
            Pending = LoadJoins(connection, "pending"),
            Blocked = LoadJoins(connection, "blocked")
        };
    }

    public void Save(HostsRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();
        ReplaceEnrolled(connection, tx, record.Hosts.Values);
        ReplaceJoins(connection, tx, "pending", record.Pending.Values);
        ReplaceJoins(connection, tx, "blocked", record.Blocked.Values);
        tx.Commit();
    }

    private static Dictionary<string, HostDto> LoadEnrolled(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT HostId, Hostname, Endpoint, LastHeartbeat FROM enrolled";
        using var reader = command.ExecuteReader();
        var hosts = new Dictionary<string, HostDto>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var hostId = reader.GetString(0);
            hosts[hostId] = new HostDto
            {
                HostId = hostId,
                Hostname = reader.GetString(1),
                Endpoint = new Uri(reader.GetString(2)),
                LastHeartbeat = DateTimeOffset.Parse(reader.GetString(3)),
                Alive = true
            };
        }

        return hosts;
    }

    private static Dictionary<string, HostJoinRequestDto> LoadJoins(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HostId, Hostname, Endpoint, RequestedAt FROM {table}";
        using var reader = command.ExecuteReader();
        var joins = new Dictionary<string, HostJoinRequestDto>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var hostId = reader.GetString(0);
            joins[hostId] = new HostJoinRequestDto
            {
                HostId = hostId,
                Hostname = reader.GetString(1),
                Endpoint = new Uri(reader.GetString(2)),
                RequestedAt = DateTimeOffset.Parse(reader.GetString(3))
            };
        }

        return joins;
    }

    private static void ReplaceEnrolled(
        SqliteConnection connection,
        SqliteTransaction tx,
        IEnumerable<HostDto> hosts)
    {
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM enrolled";
            clear.ExecuteNonQuery();
        }

        foreach (var host in hosts)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO enrolled (HostId, Hostname, Endpoint, LastHeartbeat)
                VALUES ($id, $name, $endpoint, $beat)
                """;
            insert.Parameters.AddWithValue("$id", host.HostId);
            insert.Parameters.AddWithValue("$name", host.Hostname);
            insert.Parameters.AddWithValue("$endpoint", host.Endpoint.ToString());
            insert.Parameters.AddWithValue("$beat", host.LastHeartbeat.ToString("O"));
            insert.ExecuteNonQuery();
        }
    }

    private static void ReplaceJoins(
        SqliteConnection connection,
        SqliteTransaction tx,
        string table,
        IEnumerable<HostJoinRequestDto> joins)
    {
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = $"DELETE FROM {table}";
            clear.ExecuteNonQuery();
        }

        foreach (var join in joins)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = $"""
                INSERT INTO {table} (HostId, Hostname, Endpoint, RequestedAt)
                VALUES ($id, $name, $endpoint, $at)
                """;
            insert.Parameters.AddWithValue("$id", join.HostId);
            insert.Parameters.AddWithValue("$name", join.Hostname);
            insert.Parameters.AddWithValue("$endpoint", join.Endpoint.ToString());
            insert.Parameters.AddWithValue("$at", join.RequestedAt.ToString("O"));
            insert.ExecuteNonQuery();
        }
    }
}

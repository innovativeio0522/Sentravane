using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Sentravane.Shared.Models;

namespace Sentravane.Sensor.Service.Services;

public interface ILocalStoreService
{
    Task EnqueueHeartbeatAsync(HeartbeatPayload heartbeat);
    Task<HeartbeatPayload?> DequeueHeartbeatAsync();
    Task RecordIncidentAsync(ForensicSnapshotPayload snapshot);
}

public class LocalStoreService : ILocalStoreService
{
    private readonly ILogger<LocalStoreService> _logger;
    private readonly string _connectionString;

    public LocalStoreService(ILogger<LocalStoreService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dbDir = Path.Combine(appData, "Sentravane", "Data");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "sensor_cache.db");
        _connectionString = $"Data Source={dbPath};";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS TelemetryQueue (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Payload BLOB NOT NULL,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS IncidentsLocal (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Details TEXT NOT NULL,
                LoggedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );";
        cmd.ExecuteNonQuery();
    }

    public async Task EnqueueHeartbeatAsync(HeartbeatPayload heartbeat)
    {
        try
        {
            var json = JsonSerializer.Serialize(heartbeat);
            var plaintextBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.LocalMachine);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO TelemetryQueue (Payload) VALUES (@payload)";
            cmd.Parameters.AddWithValue("@payload", encrypted);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist offline telemetry");
        }
    }

    public async Task<HeartbeatPayload?> DequeueHeartbeatAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Payload FROM TelemetryQueue ORDER BY Id ASC LIMIT 1";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);
                var encrypted = (byte[])reader["Payload"];
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
                var json = Encoding.UTF8.GetString(decrypted);

                await using var deleteCmd = connection.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM TelemetryQueue WHERE Id = @id";
                deleteCmd.Parameters.AddWithValue("@id", id);
                await deleteCmd.ExecuteNonQueryAsync();

                return JsonSerializer.Deserialize<HeartbeatPayload>(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue offline telemetry");
        }
        return null;
    }

    public async Task RecordIncidentAsync(ForensicSnapshotPayload snapshot)
    {
        try
        {
            var json = JsonSerializer.Serialize(snapshot);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO IncidentsLocal (Details) VALUES (@details)";
            cmd.Parameters.AddWithValue("@details", json);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record local incident");
        }
    }
}

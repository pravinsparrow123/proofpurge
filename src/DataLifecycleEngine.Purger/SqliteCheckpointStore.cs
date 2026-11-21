using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DataLifecycleEngine.Purger;

public class SqliteCheckpointStore : ICheckpointStore
{
    private readonly string _connectionString;

    public SqliteCheckpointStore(string sqliteDbPath = "./checkpoints/purge_state.db")
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(sqliteDbPath)) ?? ".";
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS purge_checkpoints (
                policy_name TEXT NOT NULL,
                table_name TEXT NOT NULL,
                rows_deleted INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL,
                last_updated_utc TEXT NOT NULL,
                PRIMARY KEY (policy_name, table_name)
            );";

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveProgressAsync(string policyName, string tableName, long rowsDeleted, string status, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO purge_checkpoints (policy_name, table_name, rows_deleted, status, last_updated_utc)
            VALUES (@Policy, @Table, @Rows, @Status, @Updated)
            ON CONFLICT(policy_name, table_name) DO UPDATE SET
                rows_deleted = @Rows,
                status = @Status,
                last_updated_utc = @Updated;";

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Policy", policyName);
        cmd.Parameters.AddWithValue("@Table", tableName);
        cmd.Parameters.AddWithValue("@Rows", rowsDeleted);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@Updated", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Dictionary<string, TablePurgeState>> GetProgressAsync(string policyName, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TablePurgeState>(StringComparer.OrdinalIgnoreCase);
        const string sql = @"
            SELECT table_name, rows_deleted, status, last_updated_utc
            FROM purge_checkpoints
            WHERE policy_name = @Policy;";

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Policy", policyName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string tableName = reader.GetString(0);
            long rows = reader.GetInt64(1);
            string status = reader.GetString(2);
            string updatedStr = reader.GetString(3);
            DateTime.TryParse(updatedStr, out var updatedUtc);

            result[tableName] = new TablePurgeState
            {
                TableName = tableName,
                RowsDeleted = rows,
                Status = status,
                LastUpdatedUtc = updatedUtc
            };
        }

        return result;
    }

    public async Task ClearPolicyProgressAsync(string policyName, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM purge_checkpoints WHERE policy_name = @Policy;";

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Policy", policyName);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

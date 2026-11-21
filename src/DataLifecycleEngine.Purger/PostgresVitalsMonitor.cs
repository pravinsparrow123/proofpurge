using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace DataLifecycleEngine.Purger;

public class PostgresVitalsMonitor : IDatabaseVitalsMonitor
{
    public async Task<DatabaseVitals> GetVitalsAsync(string connectionString, CancellationToken ct = default)
    {
        const string query = @"
            SELECT 
                COALESCE(pg_database_size(current_database()) / (1024.0 * 1024.0), 0) AS DbSizeMb,
                COALESCE((SELECT COUNT(1) FROM pg_stat_activity WHERE state = 'active'), 0) AS ActiveConnections,
                COALESCE((SELECT MAX(EXTRACT(EPOCH FROM (now() - replay_lsn::text::interval))) FROM pg_stat_replication WHERE replay_lsn IS NOT NULL), 0) AS MaxLagSeconds;";

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(query, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                double dbSizeMb = Convert.ToDouble(reader.GetValue(0));
                int activeConns = Convert.ToInt32(reader.GetValue(1));
                double lagSecs = Convert.ToDouble(reader.GetValue(2));

                return new DatabaseVitals
                {
                    TotalLogSizeMb = dbSizeMb,
                    UsedLogSizeMb = dbSizeMb,
                    UsedLogSpacePercent = Math.Min(100.0, lagSecs * 10.0), // Maps replication lag to 0-100% scale
                    ActiveTransactionsCount = activeConns,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }
        }
        catch
        {
            return new DatabaseVitals
            {
                TotalLogSizeMb = 0,
                UsedLogSizeMb = 0,
                UsedLogSpacePercent = 0,
                ActiveTransactionsCount = 0,
                CheckedAtUtc = DateTime.UtcNow
            };
        }

        return new DatabaseVitals();
    }
}

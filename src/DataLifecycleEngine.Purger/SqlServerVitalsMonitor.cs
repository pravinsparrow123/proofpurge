using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DataLifecycleEngine.Purger;

public class SqlServerVitalsMonitor : IDatabaseVitalsMonitor
{
    public async Task<DatabaseVitals> GetVitalsAsync(string connectionString, CancellationToken ct = default)
    {
        const string query = @"
            SELECT 
                total_log_size_in_bytes,
                used_log_space_in_bytes,
                used_log_space_in_percent,
                (SELECT COUNT(1) FROM sys.dm_tran_active_transactions) AS active_tran_count
            FROM sys.dm_db_log_space_usage;";

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand(query, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                long totalBytes = reader.GetInt64(0);
                long usedBytes = reader.GetInt64(1);
                double percent = Convert.ToDouble(reader.GetValue(2));
                int activeTrans = reader.GetInt32(3);

                return new DatabaseVitals
                {
                    TotalLogSizeMb = totalBytes / (1024.0 * 1024.0),
                    UsedLogSizeMb = usedBytes / (1024.0 * 1024.0),
                    UsedLogSpacePercent = percent,
                    ActiveTransactionsCount = activeTrans,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }
        }
        catch
        {
            // Fallback if dm_db_log_space_usage has restricted permissions
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

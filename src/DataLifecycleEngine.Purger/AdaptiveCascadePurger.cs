using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Core;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Core.Graph;
using Microsoft.Data.SqlClient;

namespace DataLifecycleEngine.Purger;

public class AdaptiveCascadePurger
{
    private readonly IDatabaseVitalsMonitor _vitalsMonitor;
    private readonly ICheckpointStore _checkpointStore;

    public event Action<string, int, DatabaseVitals>? OnBatchCompleted;
    public event Action<string, DatabaseVitals>? OnThrottled;
    public event Action<string, long>? OnTableCompleted;

    public AdaptiveCascadePurger(
        IDatabaseVitalsMonitor? vitalsMonitor = null,
        ICheckpointStore? checkpointStore = null)
    {
        _vitalsMonitor = vitalsMonitor ?? new SqlServerVitalsMonitor();
        _checkpointStore = checkpointStore ?? new SqliteCheckpointStore();
    }

    public async Task<PurgeRunResult> PurgeCascadeAsync(
        string connectionString,
        RetentionPolicy policy,
        ExecutionPlan plan,
        PurgeOptions options,
        CancellationToken ct = default)
    {
        var runStopwatch = Stopwatch.StartNew();
        var result = new PurgeRunResult
        {
            PolicyName = policy.Name,
            IsDryRun = options.DryRun
        };

        await _checkpointStore.InitializeAsync(ct);
        var checkpoints = options.Resume
            ? await _checkpointStore.GetProgressAsync(policy.Name, ct)
            : new Dictionary<string, TablePurgeState>(StringComparer.OrdinalIgnoreCase);

        DateTime cutoffDate = DateHelper.CalculateCutoffDate(policy.RetentionCutoff);
        string rootWhere = $"[{policy.DateColumn}] < @Cutoff";
        if (!string.IsNullOrWhiteSpace(policy.WhereClause))
        {
            rootWhere += $" AND ({policy.WhereClause})";
        }

        try
        {
            // Plan.DeletionOrder is in safe deletion order: Leaves -> Parents -> Root
            foreach (var tableName in plan.DeletionOrder)
            {
                ct.ThrowIfCancellationRequested();

                if (options.Resume &&
                    checkpoints.TryGetValue(tableName, out var state) &&
                    string.Equals(state.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    result.TableResults.Add(new TablePurgeResult
                    {
                        TableName = tableName,
                        RowsPurged = state.RowsDeleted,
                        Status = "SKIPPED (Already Completed)",
                        Duration = TimeSpan.Zero
                    });
                    result.TotalRowsPurged += state.RowsDeleted;
                    continue;
                }

                var tableStopwatch = Stopwatch.StartNew();
                long tableDeletedTotal = 0;

                if (options.DryRun)
                {
                    string countQuery = BuildDryRunCountQuery(tableName, policy, rootWhere);
                    await using var conn = new SqlConnection(connectionString);
                    await conn.OpenAsync(ct);

                    await using var countCmd = new SqlCommand(countQuery, conn);
                    countCmd.Parameters.Add(new SqlParameter("@Cutoff", SqlDbType.DateTime2) { Value = cutoffDate });

                    var candidateCountObj = await countCmd.ExecuteScalarAsync(ct);
                    tableDeletedTotal = Convert.ToInt64(candidateCountObj ?? 0);

                    tableStopwatch.Stop();
                    result.TableResults.Add(new TablePurgeResult
                    {
                        TableName = tableName,
                        RowsPurged = tableDeletedTotal,
                        Status = "DRY RUN (Simulated)",
                        Duration = tableStopwatch.Elapsed
                    });
                    result.TotalRowsPurged += tableDeletedTotal;
                    OnTableCompleted?.Invoke(tableName, tableDeletedTotal);
                    continue;
                }

                // Live micro-batch delete execution
                string deleteSql = BuildDeleteQuery(tableName, policy, rootWhere, options.BatchSize);
                long previouslyDeleted = (options.Resume && checkpoints.TryGetValue(tableName, out var prev)) ? prev.RowsDeleted : 0;
                tableDeletedTotal = previouslyDeleted;

                await using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(ct);

                    while (!ct.IsCancellationRequested)
                    {
                        // Check vitals and adaptively throttle
                        var vitals = await _vitalsMonitor.GetVitalsAsync(connectionString, ct);
                        while (vitals.IsThrottlingRecommended(options.MaxTransactionLogPercent) && !ct.IsCancellationRequested)
                        {
                            OnThrottled?.Invoke(tableName, vitals);
                            await Task.Delay(Math.Max(3000, options.ThrottlePauseMs * 2), ct);
                            vitals = await _vitalsMonitor.GetVitalsAsync(connectionString, ct);
                        }

                        await using var cmd = new SqlCommand(deleteSql, conn);
                        cmd.CommandTimeout = 120;
                        cmd.Parameters.Add(new SqlParameter("@Cutoff", SqlDbType.DateTime2) { Value = cutoffDate });
                        cmd.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = options.BatchSize });

                        int deletedInBatch = await cmd.ExecuteNonQueryAsync(ct);
                        if (deletedInBatch == 0)
                        {
                            // No more rows matching criteria in this table
                            break;
                        }

                        tableDeletedTotal += deletedInBatch;
                        await _checkpointStore.SaveProgressAsync(policy.Name, tableName, tableDeletedTotal, "IN_PROGRESS", ct);
                        OnBatchCompleted?.Invoke(tableName, deletedInBatch, vitals);

                        if (options.ThrottlePauseMs > 0)
                        {
                            await Task.Delay(options.ThrottlePauseMs, ct);
                        }
                    }
                }

                tableStopwatch.Stop();
                await _checkpointStore.SaveProgressAsync(policy.Name, tableName, tableDeletedTotal, "COMPLETED", ct);

                result.TableResults.Add(new TablePurgeResult
                {
                    TableName = tableName,
                    RowsPurged = tableDeletedTotal,
                    Status = "COMPLETED",
                    Duration = tableStopwatch.Elapsed
                });

                result.TotalRowsPurged += tableDeletedTotal;
                OnTableCompleted?.Invoke(tableName, tableDeletedTotal);
            }

            runStopwatch.Stop();
            result.TotalDuration = runStopwatch.Elapsed;
            result.CompletedSuccessfully = true;
        }
        catch (OperationCanceledException)
        {
            runStopwatch.Stop();
            result.TotalDuration = runStopwatch.Elapsed;
            result.CompletedSuccessfully = false;
            result.ErrorMessage = "Purge execution was cancelled by user. Progress checkpointed successfully.";
        }
        catch (Exception ex)
        {
            runStopwatch.Stop();
            result.TotalDuration = runStopwatch.Elapsed;
            result.CompletedSuccessfully = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public static string BuildDeleteQuery(string targetTable, RetentionPolicy policy, string rootWhere, int batchSize)
    {
        if (string.Equals(targetTable, policy.RootTable, StringComparison.OrdinalIgnoreCase))
        {
            return $"DELETE TOP (@BatchSize) FROM [dbo].[{targetTable}] WHERE {rootWhere};";
        }

        var level1Child = policy.CascadeChildren.FirstOrDefault(c => string.Equals(c.Table, targetTable, StringComparison.OrdinalIgnoreCase));
        if (level1Child != null)
        {
            return $"DELETE TOP (@BatchSize) FROM [dbo].[{targetTable}] WHERE [{level1Child.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{policy.RootTable}] WITH (NOLOCK) WHERE {rootWhere});";
        }

        foreach (var l1 in policy.CascadeChildren)
        {
            var l2 = l1.Children.FirstOrDefault(c => string.Equals(c.Table, targetTable, StringComparison.OrdinalIgnoreCase));
            if (l2 != null)
            {
                return $"DELETE TOP (@BatchSize) FROM [dbo].[{targetTable}] WHERE [{l2.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{l1.Table}] WITH (NOLOCK) WHERE [{l1.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{policy.RootTable}] WITH (NOLOCK) WHERE {rootWhere}));";
            }
        }

        return $"DELETE TOP (@BatchSize) FROM [dbo].[{targetTable}];";
    }

    public static string BuildDryRunCountQuery(string targetTable, RetentionPolicy policy, string rootWhere)
    {
        if (string.Equals(targetTable, policy.RootTable, StringComparison.OrdinalIgnoreCase))
        {
            return $"SELECT COUNT_BIG(1) FROM [dbo].[{targetTable}] WITH (NOLOCK) WHERE {rootWhere};";
        }

        var level1Child = policy.CascadeChildren.FirstOrDefault(c => string.Equals(c.Table, targetTable, StringComparison.OrdinalIgnoreCase));
        if (level1Child != null)
        {
            return $"SELECT COUNT_BIG(1) FROM [dbo].[{targetTable}] WITH (NOLOCK) WHERE [{level1Child.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{policy.RootTable}] WITH (NOLOCK) WHERE {rootWhere});";
        }

        foreach (var l1 in policy.CascadeChildren)
        {
            var l2 = l1.Children.FirstOrDefault(c => string.Equals(c.Table, targetTable, StringComparison.OrdinalIgnoreCase));
            if (l2 != null)
            {
                return $"SELECT COUNT_BIG(1) FROM [dbo].[{targetTable}] WITH (NOLOCK) WHERE [{l2.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{l1.Table}] WITH (NOLOCK) WHERE [{l1.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{policy.RootTable}] WITH (NOLOCK) WHERE {rootWhere}));";
            }
        }

        return $"SELECT COUNT_BIG(1) FROM [dbo].[{targetTable}] WITH (NOLOCK);";
    }
}

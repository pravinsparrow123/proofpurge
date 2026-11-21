using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver.Storage;
using DataLifecycleEngine.Core;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Core.Graph;
using Microsoft.Data.SqlClient;

namespace DataLifecycleEngine.Archiver;

public class CascadeArchiver
{
    private readonly ParquetArchiver _parquetArchiver;

    public CascadeArchiver(ParquetArchiver parquetArchiver)
    {
        _parquetArchiver = parquetArchiver;
    }

    public async Task<List<ArchiveMetadata>> ArchivePolicyCascadeAsync(
        string connectionString,
        RetentionPolicy policy,
        ExecutionPlan plan,
        CancellationToken ct = default)
    {
        var archives = new List<ArchiveMetadata>();
        DateTime cutoffDate = DateHelper.CalculateCutoffDate(policy.RetentionCutoff);

        // Build root filter SQL
        string rootWhere = $"[{policy.DateColumn}] < @Cutoff";
        if (!string.IsNullOrWhiteSpace(policy.WhereClause))
        {
            rootWhere += $" AND ({policy.WhereClause})";
        }

        // We can process tables in the execution order (or reverse, Parquet archives are independent)
        foreach (var tableName in plan.DeletionOrder)
        {
            string selectSql;
            var sqlParams = new List<SqlParameter>
            {
                new("@Cutoff", SqlDbType.DateTime2) { Value = cutoffDate }
            };

            if (string.Equals(tableName, policy.RootTable, StringComparison.OrdinalIgnoreCase))
            {
                selectSql = $"SELECT * FROM [dbo].[{tableName}] WITH (NOLOCK) WHERE {rootWhere}";
            }
            else
            {
                // Generate join predicate from the relationship tree
                selectSql = BuildCascadeSelectQuery(tableName, policy, rootWhere);
            }

            var meta = await _parquetArchiver.ArchiveTableDataAsync(
                connectionString,
                policy.Name,
                tableName,
                selectSql,
                sqlParams,
                ct);

            archives.Add(meta);
        }

        return archives;
    }

    private static string BuildCascadeSelectQuery(string targetTable, RetentionPolicy policy, string rootWhere)
    {
        // Simple 2-level hierarchy query resolution for standard schemas:
        // Level 1 children (Orders, AuditLogs): AccountId IN (SELECT Id FROM Accounts WHERE ...)
        // Level 2 children (OrderItems, Shipments): OrderId IN (SELECT Id FROM Orders WHERE AccountId IN (SELECT Id FROM Accounts WHERE ...))
        var level1Child = policy.CascadeChildren.FirstOrDefault(c => string.Equals(c.Table, targetTable, StringComparison.OrdinalIgnoreCase));
        if (level1Child != null)
        {
            return $"SELECT * FROM [dbo].[{targetTable}] WITH (NOLOCK) WHERE [{level1Child.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{policy.RootTable}] WITH (NOLOCK) WHERE {rootWhere})";
        }

        foreach (var l1 in policy.CascadeChildren)
        {
            var l2 = l1.Children.FirstOrDefault(c => string.Equals(c.Table, targetTable, StringComparison.OrdinalIgnoreCase));
            if (l2 != null)
            {
                return $"SELECT * FROM [dbo].[{targetTable}] WITH (NOLOCK) WHERE [{l2.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{l1.Table}] WITH (NOLOCK) WHERE [{l1.ForeignKey}] IN (SELECT [Id] FROM [dbo].[{policy.RootTable}] WITH (NOLOCK) WHERE {rootWhere}))";
            }
        }

        // Fallback
        return $"SELECT * FROM [dbo].[{targetTable}] WITH (NOLOCK);";
    }
}

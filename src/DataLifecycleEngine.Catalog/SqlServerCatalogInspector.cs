using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Core.Graph;
using Microsoft.Data.SqlClient;

namespace DataLifecycleEngine.Catalog;

public class SqlServerCatalogInspector : ICatalogInspector
{
    public async Task<List<TableMetadata>> GetTablesAsync(string connectionString, CancellationToken ct = default)
    {
        var tables = new List<TableMetadata>();
        const string query = @"
            SELECT 
                s.name AS SchemaName,
                t.name AS TableName,
                ISNULL(SUM(p.rows), 0) AS EstimatedRowCount,
                ISNULL(SUM(a.total_pages) * 8 * 1024, 0) AS TotalSizeBytes
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
            LEFT JOIN sys.allocation_units a ON p.partition_id = a.container_id
            WHERE t.is_ms_shipped = 0
            GROUP BY s.name, t.name;";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableMetadata
            {
                Schema = reader.GetString(0),
                TableName = reader.GetString(1),
                EstimatedRowCount = reader.GetInt64(2),
                TotalSizeBytes = reader.GetInt64(3)
            });
        }

        return tables;
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string connectionString, CancellationToken ct = default)
    {
        var fks = new List<ForeignKeyInfo>();
        const string query = @"
            SELECT 
                fk.name AS ConstraintName,
                SCHEMA_NAME(child_tab.schema_id) AS ChildSchema,
                child_tab.name AS ChildTable,
                child_col.name AS ChildColumn,
                SCHEMA_NAME(parent_tab.schema_id) AS ParentSchema,
                parent_tab.name AS ParentTable,
                parent_col.name AS ParentColumn,
                child_col.is_nullable AS IsNullable,
                CASE 
                    WHEN EXISTS (
                        SELECT 1 
                        FROM sys.index_columns ic
                        JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        WHERE ic.object_id = child_tab.object_id 
                          AND ic.column_id = child_col.column_id
                          AND ic.key_ordinal = 1
                    ) THEN 1 
                    ELSE 0 
                END AS HasIndexOnChildColumn
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables child_tab ON fkc.parent_object_id = child_tab.object_id
            JOIN sys.columns child_col ON fkc.parent_object_id = child_col.object_id AND fkc.parent_column_id = child_col.column_id
            JOIN sys.tables parent_tab ON fkc.referenced_object_id = parent_tab.object_id
            JOIN sys.columns parent_col ON fkc.referenced_object_id = parent_col.object_id AND fkc.referenced_column_id = parent_col.column_id
            WHERE child_tab.is_ms_shipped = 0 AND parent_tab.is_ms_shipped = 0;";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            fks.Add(new ForeignKeyInfo
            {
                ConstraintName = reader.GetString(0),
                ChildSchema = reader.GetString(1),
                ChildTable = reader.GetString(2),
                ChildColumn = reader.GetString(3),
                ParentSchema = reader.GetString(4),
                ParentTable = reader.GetString(5),
                ParentColumn = reader.GetString(6),
                IsNullable = reader.GetBoolean(7),
                HasIndexOnChildColumn = reader.GetInt32(8) == 1
            });
        }

        return fks;
    }

    public async Task<long> GetRowCountAsync(string connectionString, string schema, string table, CancellationToken ct = default)
    {
        string safeSchema = SanitizeIdentifier(schema);
        string safeTable = SanitizeIdentifier(table);
        string query = $"SELECT COUNT_BIG(1) FROM [{safeSchema}].[{safeTable}] WITH (NOLOCK);";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(query, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<long> GetCandidateCountAsync(
        string connectionString, 
        string schema, 
        string table, 
        string dateColumn, 
        string cutoffInterval, 
        string? whereClause, 
        CancellationToken ct = default)
    {
        string safeSchema = SanitizeIdentifier(schema);
        string safeTable = SanitizeIdentifier(table);
        string safeDateCol = SanitizeIdentifier(dateColumn);
        DateTime cutoffDate = Core.DateHelper.CalculateCutoffDate(cutoffInterval);

        string sql = $"SELECT COUNT_BIG(1) FROM [{safeSchema}].[{safeTable}] WITH (NOLOCK) WHERE [{safeDateCol}] < @Cutoff";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" AND ({whereClause})";
        }

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Cutoff", SqlDbType.DateTime2) { Value = cutoffDate });

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public static DateTime CalculateCutoffDate(string cutoff)
    {
        if (string.IsNullOrWhiteSpace(cutoff))
            return DateTime.UtcNow.AddYears(-3);

        var match = Regex.Match(cutoff.Trim().ToLowerInvariant(), @"^(\d+)([dmy])$");
        if (!match.Success)
            throw new ArgumentException($"Invalid retention cutoff format: '{cutoff}'. Expected format like '3y', '90d', '12m'.");

        int value = int.Parse(match.Groups[1].Value);
        string unit = match.Groups[2].Value;

        return unit switch
        {
            "d" => DateTime.UtcNow.AddDays(-value),
            "m" => DateTime.UtcNow.AddMonths(-value),
            "y" => DateTime.UtcNow.AddYears(-value),
            _ => throw new ArgumentException($"Unsupported unit '{unit}'")
        };
    }

    private static string SanitizeIdentifier(string identifier)
    {
        return identifier.Replace("]", "]]");
    }
}


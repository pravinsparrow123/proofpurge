using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Core;
using DataLifecycleEngine.Core.Graph;
using Npgsql;

namespace DataLifecycleEngine.Catalog;

public class PostgresCatalogInspector : ICatalogInspector
{
    public async Task<List<TableMetadata>> GetTablesAsync(string connectionString, CancellationToken ct = default)
    {
        var tables = new List<TableMetadata>();
        const string query = @"
            SELECT 
                t.table_schema AS SchemaName,
                t.table_name AS TableName,
                COALESCE(c.reltuples::bigint, 0) AS EstimatedRowCount,
                COALESCE(pg_total_relation_size(quote_ident(t.table_schema) || '.' || quote_ident(t.table_name)), 0) AS TotalSizeBytes
            FROM information_schema.tables t
            LEFT JOIN pg_class c ON c.relname = t.table_name
            WHERE t.table_schema NOT IN ('pg_catalog', 'information_schema')
              AND t.table_type = 'BASE TABLE'
            ORDER BY t.table_schema, t.table_name;";

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            tables.Add(new TableMetadata
            {
                Schema = reader.GetString(0),
                TableName = reader.GetString(1),
                EstimatedRowCount = Math.Max(0, reader.GetInt64(2)),
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
                tc.constraint_name AS ConstraintName,
                tc.table_schema AS ChildSchema,
                tc.table_name AS ChildTable,
                kcu.column_name AS ChildColumn,
                ccu.table_schema AS ParentSchema,
                ccu.table_name AS ParentTable,
                ccu.column_name AS ParentColumn,
                CASE WHEN col.is_nullable = 'YES' THEN true ELSE false END AS IsNullable,
                CASE WHEN idx.indexrelid IS NOT NULL THEN 1 ELSE 0 END AS HasIndexOnChildColumn
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name
                AND ccu.table_schema = tc.table_schema
            JOIN information_schema.columns col
                ON col.table_schema = tc.table_schema
                AND col.table_name = tc.table_name
                AND col.column_name = kcu.column_name
            LEFT JOIN pg_stat_user_indexes idx
                ON idx.schemaname = tc.table_schema
                AND idx.relname = tc.table_name
                AND idx.indexdef LIKE '%' || kcu.column_name || '%'
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema NOT IN ('pg_catalog', 'information_schema');";

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(query, conn);
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
        string query = $"SELECT COUNT(1) FROM \"{schema}\".\"{table}\";";
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(query, conn);
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
        DateTime cutoffDate = DateHelper.CalculateCutoffDate(cutoffInterval);
        string sql = $"SELECT COUNT(1) FROM \"{schema}\".\"{table}\" WHERE \"{dateColumn}\" < @Cutoff";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" AND ({whereClause})";
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("@Cutoff", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = cutoffDate });

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}

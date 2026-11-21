using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver.Storage;
using Microsoft.Data.SqlClient;
using Parquet;
using Parquet.Schema;
using Parquet.Serialization;

namespace DataLifecycleEngine.Archiver;

public class ParquetArchiver
{
    private readonly IStorageSink _storageSink;

    public ParquetArchiver(IStorageSink storageSink)
    {
        _storageSink = storageSink;
    }

    public static ParquetSchema BuildSchema(IReadOnlyList<(string Name, Type ClrType)> columns)
    {
        var fields = new List<Field>();
        foreach (var (name, clrType) in columns)
        {
            Type underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
            Field field = underlying switch
            {
                Type t when t == typeof(int) => new DataField<int?>(name),
                Type t when t == typeof(long) => new DataField<long?>(name),
                Type t when t == typeof(short) => new DataField<short?>(name),
                Type t when t == typeof(byte) => new DataField<byte?>(name),
                Type t when t == typeof(bool) => new DataField<bool?>(name),
                Type t when t == typeof(decimal) => new DataField<decimal?>(name),
                Type t when t == typeof(double) => new DataField<double?>(name),
                Type t when t == typeof(float) => new DataField<float?>(name),
                Type t when t == typeof(DateTime) => new DataField<DateTime?>(name),
                Type t when t == typeof(DateTimeOffset) => new DataField<DateTimeOffset?>(name),
                Type t when t == typeof(Guid) => new DataField<string>(name),
                Type t when t == typeof(byte[]) => new DataField<byte[]>(name),
                _ => new DataField<string>(name)
            };
            fields.Add(field);
        }

        return new ParquetSchema(fields);
    }

    public async Task<ArchiveMetadata> ArchiveTableDataAsync(
        string connectionString,
        string policyName,
        string tableName,
        string selectSql,
        IEnumerable<SqlParameter>? sqlParams = null,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(selectSql, conn);
        if (sqlParams != null)
        {
            foreach (var p in sqlParams)
            {
                cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.SqlDbType) { Value = p.Value });
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        // Discover column schema
        var schemaTable = await reader.GetColumnSchemaAsync(ct);
        var colDefs = new List<(string Name, Type ClrType)>();
        foreach (var col in schemaTable)
        {
            colDefs.Add((col.ColumnName, col.DataType ?? typeof(string)));
        }

        var parquetSchema = BuildSchema(colDefs);

        var rows = new List<IDictionary<string, object?>>();
        long estimatedUncompressedBytes = 0;

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(colDefs.Count);
            for (int c = 0; c < colDefs.Count; c++)
            {
                string colName = colDefs[c].Name;
                if (reader.IsDBNull(c))
                {
                    row[colName] = null;
                }
                else
                {
                    object raw = reader.GetValue(c);
                    object? converted = ConvertValue(raw, colDefs[c].ClrType);
                    row[colName] = converted;
                    estimatedUncompressedBytes += EstimateValueSize(raw);
                }
            }
            rows.Add(row);
        }

        if (rows.Count == 0)
        {
            return new ArchiveMetadata
            {
                TableName = tableName,
                RowCount = 0,
                OriginalSizeBytes = 0,
                CompressedSizeBytes = 0,
                Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                StorageLocation = "N/A"
            };
        }

        var memStream = new MemoryStream();
        var options = new ParquetOptions
        {
            CompressionMethod = CompressionMethod.Snappy
        };

        await ParquetSerializer.SerializeUntypedAsync(rows, parquetSchema, memStream, options, cancellationToken: ct);

        var (location, checksum, compressedBytes) = await _storageSink.SaveArchiveAsync(
            policyName, 
            tableName, 
            memStream, 
            ct);

        return new ArchiveMetadata
        {
            TableName = tableName,
            RowCount = rows.Count,
            OriginalSizeBytes = estimatedUncompressedBytes,
            CompressedSizeBytes = compressedBytes,
            Sha256Checksum = checksum,
            StorageLocation = location,
            ArchivedAtUtc = DateTime.UtcNow
        };
    }

    private static object? ConvertValue(object? val, Type targetType)
    {
        if (val == null || val == DBNull.Value) return null;
        if (targetType == typeof(Guid) || val is Guid) return val.ToString();
        return val;
    }

    private static long EstimateValueSize(object? val)
    {
        if (val == null || val == DBNull.Value) return 1;
        if (val is string s) return s.Length * 2L;
        if (val is byte[] b) return b.Length;
        if (val is int || val is float) return 4;
        if (val is long || val is double || val is DateTime) return 8;
        if (val is decimal) return 16;
        if (val is bool || val is byte) return 1;
        return 8;
    }
}

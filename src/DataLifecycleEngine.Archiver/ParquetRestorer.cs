using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver.Storage;
using Microsoft.Data.SqlClient;
using Parquet.Serialization;

namespace DataLifecycleEngine.Archiver;

public class ParquetRestorer
{
    private readonly IStorageSink _storageSink;

    public ParquetRestorer(IStorageSink storageSink)
    {
        _storageSink = storageSink;
    }

    public async Task<long> RestoreTableAsync(
        string connectionString,
        string archiveFilePath,
        string targetTable,
        string targetSchema = "dbo",
        CancellationToken ct = default)
    {
        await using var stream = await _storageSink.OpenArchiveAsync(archiveFilePath, ct);
        var result = await ParquetSerializer.DeserializeUntypedAsync(stream, cancellationToken: ct);

        if (result.Data.Count == 0)
        {
            return 0;
        }

        var dt = new DataTable(targetTable);

        // Build DataTable columns from first record
        var firstRow = result.Data[0];
        foreach (var kvp in firstRow)
        {
            Type colType = kvp.Value != null ? kvp.Value.GetType() : typeof(string);
            dt.Columns.Add(kvp.Key, Nullable.GetUnderlyingType(colType) ?? colType);
        }

        // Fill DataTable
        foreach (var dict in result.Data)
        {
            var row = dt.NewRow();
            foreach (var kvp in dict)
            {
                row[kvp.Key] = kvp.Value ?? DBNull.Value;
            }
            dt.Rows.Add(row);
        }

        // Execute SqlBulkCopy
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        using var bulkCopy = new SqlBulkCopy(
            conn,
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls,
            null)
        {
            DestinationTableName = $"[{targetSchema}].[{targetTable}]",
            BatchSize = 2000,
            BulkCopyTimeout = 300
        };

        foreach (DataColumn col in dt.Columns)
        {
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dt, ct);
        return dt.Rows.Count;
    }
}


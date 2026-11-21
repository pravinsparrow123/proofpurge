using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Core.Graph;

namespace DataLifecycleEngine.Catalog;

public interface ICatalogInspector
{
    Task<List<TableMetadata>> GetTablesAsync(string connectionString, CancellationToken ct = default);
    Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string connectionString, CancellationToken ct = default);
    Task<long> GetRowCountAsync(string connectionString, string schema, string table, CancellationToken ct = default);
    Task<long> GetCandidateCountAsync(string connectionString, string schema, string table, string dateColumn, string cutoffInterval, string? whereClause, CancellationToken ct = default);
}


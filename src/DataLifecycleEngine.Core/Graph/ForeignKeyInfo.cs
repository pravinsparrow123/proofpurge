using System;
using System.Collections.Generic;

namespace DataLifecycleEngine.Core.Graph;

public class ForeignKeyInfo
{
    public string ConstraintName { get; set; } = string.Empty;
    public string ChildSchema { get; set; } = "dbo";
    public string ChildTable { get; set; } = string.Empty;
    public string ChildColumn { get; set; } = string.Empty;
    public string ParentSchema { get; set; } = "dbo";
    public string ParentTable { get; set; } = string.Empty;
    public string ParentColumn { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool HasIndexOnChildColumn { get; set; }

    public string FullChildName => string.IsNullOrEmpty(ChildSchema) ? ChildTable : $"{ChildSchema}.{ChildTable}";
    public string FullParentName => string.IsNullOrEmpty(ParentSchema) ? ParentTable : $"{ParentSchema}.{ParentTable}";
}

public class TableMetadata
{
    public string Schema { get; set; } = "dbo";
    public string TableName { get; set; } = string.Empty;
    public string PrimaryKeyColumn { get; set; } = "Id";
    public long EstimatedRowCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<ForeignKeyInfo> OutgoingForeignKeys { get; set; } = new(); // FKs from this table pointing to parents
    public List<ForeignKeyInfo> IncomingForeignKeys { get; set; } = new(); // FKs from children pointing to this table

    public string FullName => string.IsNullOrEmpty(Schema) ? TableName : $"{Schema}.{TableName}";
}


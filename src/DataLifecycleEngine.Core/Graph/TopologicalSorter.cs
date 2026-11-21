using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLifecycleEngine.Core.Graph;

public class ExecutionPlan
{
    public List<string> DeletionOrder { get; set; } = new();
    public List<ForeignKeyInfo> Relationships { get; set; } = new();
    public List<string> MissingIndexWarnings { get; set; } = new();
    public List<string> CyclicDependencies { get; set; } = new();
    public bool HasCycles => CyclicDependencies.Count > 0;
}

public class TopologicalSorter
{
    /// <summary>
    /// Computes safe deletion order: Leaves (deepest children) first, Parents (root) last.
    /// Edge direction: Child -> Parent (means "Child must be deleted before Parent").
    /// </summary>
    public static ExecutionPlan ComputeDeletionOrder(
        IEnumerable<string> tableNames, 
        IEnumerable<ForeignKeyInfo> foreignKeys)
    {
        var plan = new ExecutionPlan();
        var tables = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        var relevantFks = foreignKeys
            .Where(fk => tables.Contains(fk.ChildTable) && tables.Contains(fk.ParentTable))
            .ToList();

        plan.Relationships = relevantFks;

        // Check for missing indexes on FK child columns
        foreach (var fk in relevantFks.Where(fk => !fk.HasIndexOnChildColumn))
        {
            plan.MissingIndexWarnings.Add(
                $"Missing index on foreign key column: {fk.ChildTable}.{fk.ChildColumn} referencing {fk.ParentTable}.{fk.ParentColumn}. Cascading deletes may cause full table scans!");
        }

        // Handle self-referencing tables separately (e.g. Employee.ManagerId -> Employee.Id)
        var nonSelfFks = new List<ForeignKeyInfo>();
        foreach (var fk in relevantFks)
        {
            if (string.Equals(fk.ChildTable, fk.ParentTable, StringComparison.OrdinalIgnoreCase))
            {
                plan.MissingIndexWarnings.Add(
                    $"Self-referencing table detected: {fk.ChildTable}. Deletes will require hierarchical level-by-level batching or nullable FK zeroing.");
            }
            else
            {
                nonSelfFks.Add(fk);
            }
        }

        // Build adjacency list & in-degree map
        // Edge: Child -> Parent (Child must be deleted BEFORE Parent)
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            adj[table] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            inDegree[table] = 0;
        }

        foreach (var fk in nonSelfFks)
        {
            // If child -> parent edge is not yet added
            if (adj[fk.ChildTable].Add(fk.ParentTable))
            {
                inDegree[fk.ParentTable]++;
            }
        }

        // Kahn's Algorithm
        var queue = new Queue<string>();
        foreach (var table in tables)
        {
            if (inDegree[table] == 0)
            {
                queue.Enqueue(table); // Leaf tables with no dependents waiting to be deleted
            }
        }

        var deletionOrder = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            deletionOrder.Add(current);

            foreach (var neighbor in adj[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (deletionOrder.Count < tables.Count)
        {
            // Cycle detected! Use Tarjan's or cycle finder
            var remaining = tables.Where(t => !deletionOrder.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
            plan.CyclicDependencies.Add($"Mutual circular dependency detected among tables: {string.Join(" <-> ", remaining)}");
        }

        plan.DeletionOrder = deletionOrder;
        return plan;
    }
}


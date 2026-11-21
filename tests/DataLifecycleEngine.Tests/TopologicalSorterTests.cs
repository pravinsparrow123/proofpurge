using System.Collections.Generic;
using DataLifecycleEngine.Core.Graph;
using Xunit;

namespace DataLifecycleEngine.Tests;

public class TopologicalSorterTests
{
    [Fact]
    public void ComputeDeletionOrder_MultiTierHierarchy_ProducesSafeLeafToRootOrder()
    {
        // Arrange
        // Hierarchy:
        // Accounts (Root)
        //   -> Orders (Child of Accounts)
        //        -> OrderItems (Child of Orders)
        //        -> Shipments (Child of Orders)
        //   -> AuditLogs (Child of Accounts)
        var tables = new List<string> { "Accounts", "Orders", "OrderItems", "Shipments", "AuditLogs" };

        var fks = new List<ForeignKeyInfo>
        {
            new() { ChildTable = "Orders", ChildColumn = "AccountId", ParentTable = "Accounts", ParentColumn = "Id", HasIndexOnChildColumn = true },
            new() { ChildTable = "OrderItems", ChildColumn = "OrderId", ParentTable = "Orders", ParentColumn = "Id", HasIndexOnChildColumn = true },
            new() { ChildTable = "Shipments", ChildColumn = "OrderId", ParentTable = "Orders", ParentColumn = "Id", HasIndexOnChildColumn = true },
            new() { ChildTable = "AuditLogs", ChildColumn = "AccountId", ParentTable = "Accounts", ParentColumn = "Id", HasIndexOnChildColumn = true }
        };

        // Act
        var plan = TopologicalSorter.ComputeDeletionOrder(tables, fks);

        // Assert
        Assert.False(plan.HasCycles);
        Assert.Equal(5, plan.DeletionOrder.Count);

        int orderItemsIdx = plan.DeletionOrder.IndexOf("OrderItems");
        int shipmentsIdx = plan.DeletionOrder.IndexOf("Shipments");
        int ordersIdx = plan.DeletionOrder.IndexOf("Orders");
        int auditLogsIdx = plan.DeletionOrder.IndexOf("AuditLogs");
        int accountsIdx = plan.DeletionOrder.IndexOf("Accounts");

        // OrderItems and Shipments must precede Orders
        Assert.True(orderItemsIdx < ordersIdx, "OrderItems must be deleted before Orders");
        Assert.True(shipmentsIdx < ordersIdx, "Shipments must be deleted before Orders");

        // Orders and AuditLogs must precede Accounts
        Assert.True(ordersIdx < accountsIdx, "Orders must be deleted before Accounts");
        Assert.True(auditLogsIdx < accountsIdx, "AuditLogs must be deleted before Accounts");

        // Root table Accounts must be last
        Assert.Equal("Accounts", plan.DeletionOrder[^1]);
    }

    [Fact]
    public void ComputeDeletionOrder_CircularDependency_DetectsCycle()
    {
        // Arrange: A references B and B references A
        var tables = new List<string> { "TableA", "TableB" };
        var fks = new List<ForeignKeyInfo>
        {
            new() { ChildTable = "TableA", ChildColumn = "B_Id", ParentTable = "TableB", ParentColumn = "Id" },
            new() { ChildTable = "TableB", ChildColumn = "A_Id", ParentTable = "TableA", ParentColumn = "Id" }
        };

        // Act
        var plan = TopologicalSorter.ComputeDeletionOrder(tables, fks);

        // Assert
        Assert.True(plan.HasCycles);
        Assert.NotEmpty(plan.CyclicDependencies);
    }

    [Fact]
    public void ComputeDeletionOrder_MissingIndex_WarnsCorrectly()
    {
        // Arrange
        var tables = new List<string> { "Accounts", "Orders" };
        var fks = new List<ForeignKeyInfo>
        {
            new() { ChildTable = "Orders", ChildColumn = "AccountId", ParentTable = "Accounts", ParentColumn = "Id", HasIndexOnChildColumn = false }
        };

        // Act
        var plan = TopologicalSorter.ComputeDeletionOrder(tables, fks);

        // Assert
        Assert.NotEmpty(plan.MissingIndexWarnings);
        Assert.Contains("Missing index", plan.MissingIndexWarnings[0]);
    }
}


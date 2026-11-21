using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Purger;
using Xunit;

namespace DataLifecycleEngine.Tests;

public class PurgerTests
{
    private RetentionPolicy CreateSamplePolicy()
    {
        return new RetentionPolicy
        {
            Name = "test_policy",
            RootTable = "Accounts",
            DateColumn = "LastActivityDate",
            RetentionCutoff = "3y",
            WhereClause = "Status = 'CLOSED'",
            CascadeChildren = new List<CascadeChild>
            {
                new()
                {
                    Table = "Orders",
                    ForeignKey = "AccountId",
                    Children = new List<CascadeChild>
                    {
                        new() { Table = "OrderItems", ForeignKey = "OrderId" },
                        new() { Table = "Shipments", ForeignKey = "OrderId" }
                    }
                },
                new()
                {
                    Table = "AuditLogs",
                    ForeignKey = "AccountId"
                }
            }
        };
    }

    [Fact]
    public void BuildDeleteQuery_RootTable_GeneratesDirectWhere()
    {
        var policy = CreateSamplePolicy();
        string rootWhere = "[LastActivityDate] < @Cutoff AND (Status = 'CLOSED')";

        string sql = AdaptiveCascadePurger.BuildDeleteQuery("Accounts", policy, rootWhere, 2000);

        Assert.Equal("DELETE TOP (@BatchSize) FROM [dbo].[Accounts] WHERE [LastActivityDate] < @Cutoff AND (Status = 'CLOSED');", sql);
    }

    [Fact]
    public void BuildDeleteQuery_Level1Child_GeneratesSubqueryOnRoot()
    {
        var policy = CreateSamplePolicy();
        string rootWhere = "[LastActivityDate] < @Cutoff AND (Status = 'CLOSED')";

        string sql = AdaptiveCascadePurger.BuildDeleteQuery("Orders", policy, rootWhere, 2000);

        Assert.Equal("DELETE TOP (@BatchSize) FROM [dbo].[Orders] WHERE [AccountId] IN (SELECT [Id] FROM [dbo].[Accounts] WITH (NOLOCK) WHERE [LastActivityDate] < @Cutoff AND (Status = 'CLOSED'));", sql);
    }

    [Fact]
    public void BuildDeleteQuery_Level2Child_GeneratesDoubleSubquery()
    {
        var policy = CreateSamplePolicy();
        string rootWhere = "[LastActivityDate] < @Cutoff AND (Status = 'CLOSED')";

        string sql = AdaptiveCascadePurger.BuildDeleteQuery("OrderItems", policy, rootWhere, 2000);

        Assert.Equal("DELETE TOP (@BatchSize) FROM [dbo].[OrderItems] WHERE [OrderId] IN (SELECT [Id] FROM [dbo].[Orders] WITH (NOLOCK) WHERE [AccountId] IN (SELECT [Id] FROM [dbo].[Accounts] WITH (NOLOCK) WHERE [LastActivityDate] < @Cutoff AND (Status = 'CLOSED')));", sql);
    }

    [Fact]
    public void BuildDryRunCountQuery_Level1Child_GeneratesSelectCount()
    {
        var policy = CreateSamplePolicy();
        string rootWhere = "[LastActivityDate] < @Cutoff AND (Status = 'CLOSED')";

        string sql = AdaptiveCascadePurger.BuildDryRunCountQuery("Orders", policy, rootWhere);

        Assert.Equal("SELECT COUNT_BIG(1) FROM [dbo].[Orders] WITH (NOLOCK) WHERE [AccountId] IN (SELECT [Id] FROM [dbo].[Accounts] WITH (NOLOCK) WHERE [LastActivityDate] < @Cutoff AND (Status = 'CLOSED'));", sql);
    }

    [Theory]
    [InlineData(64.9, 65.0, false)]
    [InlineData(65.0, 65.0, true)]
    [InlineData(82.5, 65.0, true)]
    public void DatabaseVitals_IsThrottlingRecommended_EvaluatesCorrectly(double currentLogPercent, double threshold, bool expectedThrottled)
    {
        var vitals = new DatabaseVitals { UsedLogSpacePercent = currentLogPercent };
        Assert.Equal(expectedThrottled, vitals.IsThrottlingRecommended(threshold));
    }

    [Fact]
    public async Task SqliteCheckpointStore_SavesAndRetrievesProgressCorrectly()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"checkpoint_test_{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteCheckpointStore(tempDb);
            await store.InitializeAsync();

            await store.SaveProgressAsync("policyA", "OrderItems", 1500, "COMPLETED");
            await store.SaveProgressAsync("policyA", "Orders", 750, "IN_PROGRESS");

            var progress = await store.GetProgressAsync("policyA");

            Assert.Equal(2, progress.Count);
            Assert.Equal("COMPLETED", progress["OrderItems"].Status);
            Assert.Equal(1500, progress["OrderItems"].RowsDeleted);
            Assert.Equal("IN_PROGRESS", progress["Orders"].Status);
            Assert.Equal(750, progress["Orders"].RowsDeleted);

            await store.ClearPolicyProgressAsync("policyA");
            var cleared = await store.GetProgressAsync("policyA");
            Assert.Empty(cleared);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(tempDb))
                {
                    File.Delete(tempDb);
                }
            }
            catch
            {
                // Ignore transient file lock on temp cleanup
            }
        }
    }
}

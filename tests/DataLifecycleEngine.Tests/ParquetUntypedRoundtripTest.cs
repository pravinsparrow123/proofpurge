using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet.Schema;
using Parquet.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace DataLifecycleEngine.Tests;

public class ParquetUntypedRoundtripTest
{
    private readonly ITestOutputHelper _output;

    public ParquetUntypedRoundtripTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestSerializeUntypedRoundtrip()
    {
        var schema = new ParquetSchema(
            new DataField<int?>("Id"),
            new DataField<string>("AccountName"),
            new DataField<DateTime?>("LastActivityDate")
        );

        var row1 = new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["AccountName"] = "Customer_1",
            ["LastActivityDate"] = DateTime.UtcNow
        };

        var row2 = new Dictionary<string, object?>
        {
            ["Id"] = 2,
            ["AccountName"] = "Customer_2",
            ["LastActivityDate"] = DateTime.UtcNow.AddDays(-10)
        };

        var rows = new List<IDictionary<string, object?>> { row1, row2 };

        using var ms = new MemoryStream();
        await ParquetSerializer.SerializeUntypedAsync(rows, schema, ms);

        ms.Position = 0;
        _output.WriteLine($"Serialized Parquet size: {ms.Length} bytes");

        var result = await ParquetSerializer.DeserializeUntypedAsync(ms);
        _output.WriteLine($"Deserialized rows: {result.Data.Count}");

        Assert.Equal(2, result.Data.Count);
        Assert.Equal("Customer_1", result.Data[0]["AccountName"]);
        Assert.Equal(1, result.Data[0]["Id"]);
    }
}

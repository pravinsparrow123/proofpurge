using System;
using System.Reflection;
using Parquet;
using Xunit;
using Xunit.Abstractions;

namespace DataLifecycleEngine.Tests;

public class ParquetReflectionTest
{
    private readonly ITestOutputHelper _output;

    public ParquetReflectionTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectWriterAndReader()
    {
        _output.WriteLine("=== ParquetRowGroupWriter ===");
        foreach (var m in typeof(ParquetRowGroupWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            _output.WriteLine($"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        _output.WriteLine("=== ParquetRowGroupReader ===");
        foreach (var m in typeof(ParquetRowGroupReader).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            _output.WriteLine($"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }

        _output.WriteLine("=== ParquetSerializer ===");
        foreach (var m in typeof(Parquet.Serialization.ParquetSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            _output.WriteLine($"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }
    }
}

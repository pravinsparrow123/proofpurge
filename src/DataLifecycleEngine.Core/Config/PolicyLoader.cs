using System;
using System.IO;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DataLifecycleEngine.Core.Config;

public static class PolicyLoader
{
    private static readonly Regex EnvVarRegex = new(@"\$\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    public static RetentionConfiguration LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        string rawYaml = File.ReadAllText(filePath);
        return LoadFromString(rawYaml);
    }

    public static RetentionConfiguration LoadFromString(string yamlContent)
    {
        // Expand environment variables like ${DB_CONNECTION_STRING}
        string expandedYaml = EnvVarRegex.Replace(yamlContent, match =>
        {
            string varName = match.Groups[1].Value;
            return Environment.GetEnvironmentVariable(varName) ?? match.Value;
        });

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<RetentionConfiguration>(expandedYaml);
        Validate(config);
        return config;
    }

    private static void Validate(RetentionConfiguration config)
    {
        if (config == null)
            throw new ArgumentException("Configuration cannot be empty.");

        if (string.IsNullOrWhiteSpace(config.TargetDatabase.Driver))
            throw new ArgumentException("target_database.driver must be specified (sqlserver or postgres).");

        if (config.Policies == null || config.Policies.Count == 0)
            throw new ArgumentException("At least one policy must be defined in the configuration.");

        foreach (var policy in config.Policies)
        {
            if (string.IsNullOrWhiteSpace(policy.Name))
                throw new ArgumentException("Policy must have a valid name.");

            if (string.IsNullOrWhiteSpace(policy.RootTable))
                throw new ArgumentException($"Policy '{policy.Name}' must define a root_table.");

            if (string.IsNullOrWhiteSpace(policy.DateColumn))
                throw new ArgumentException($"Policy '{policy.Name}' must define a date_column.");
        }
    }
}


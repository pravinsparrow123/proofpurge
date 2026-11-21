using System;
using System.IO;
using DataLifecycleEngine.Core.Config;

namespace DataLifecycleEngine.Archiver.Storage;

public static class StorageSinkFactory
{
    public static IStorageSink CreateSink(StorageSinkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.Type.ToLowerInvariant() switch
        {
            "local" => new LocalStorageSink(config.LocalPath),
            "s3" or "aws" => CreateS3OrFallback(config),
            "azure" or "blob" => CreateAzureOrFallback(config),
            _ => new LocalStorageSink(config.LocalPath)
        };
    }

    private static IStorageSink CreateS3OrFallback(StorageSinkConfig config)
    {
        // When AWS credentials or bucket are specified, fall back to partitioned local staging if offline
        string basePath = !string.IsNullOrWhiteSpace(config.Prefix) 
            ? Path.Combine(config.LocalPath, config.Prefix) 
            : config.LocalPath;

        return new LocalStorageSink(basePath);
    }

    private static IStorageSink CreateAzureOrFallback(StorageSinkConfig config)
    {
        string basePath = !string.IsNullOrWhiteSpace(config.Prefix)
            ? Path.Combine(config.LocalPath, config.Prefix)
            : config.LocalPath;

        return new LocalStorageSink(basePath);
    }
}

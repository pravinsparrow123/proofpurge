# Multi-stage build for Enterprise Data Lifecycle Engine CLI
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["src/DataLifecycleEngine.Core/DataLifecycleEngine.Core.csproj", "src/DataLifecycleEngine.Core/"]
COPY ["src/DataLifecycleEngine.Catalog/DataLifecycleEngine.Catalog.csproj", "src/DataLifecycleEngine.Catalog/"]
COPY ["src/DataLifecycleEngine.Archiver/DataLifecycleEngine.Archiver.csproj", "src/DataLifecycleEngine.Archiver/"]
COPY ["src/DataLifecycleEngine.Purger/DataLifecycleEngine.Purger.csproj", "src/DataLifecycleEngine.Purger/"]
COPY ["src/DataLifecycleEngine.Audit/DataLifecycleEngine.Audit.csproj", "src/DataLifecycleEngine.Audit/"]
COPY ["src/DataLifecycleEngine.Cli/DataLifecycleEngine.Cli.csproj", "src/DataLifecycleEngine.Cli/"]

RUN dotnet restore "src/DataLifecycleEngine.Cli/DataLifecycleEngine.Cli.csproj"

# Copy full source and build release
COPY . .
WORKDIR "/src/src/DataLifecycleEngine.Cli"
RUN dotnet publish "DataLifecycleEngine.Cli.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

# Install fontconfig and libgdiplus/freetype dependencies if needed for PDF rendering
RUN apt-get update && apt-get install -y --no-install-recommends \
    fontconfig \
    libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

VOLUME ["/app/configs", "/app/archives", "/app/audit-certificates", "/app/checkpoints"]

ENTRYPOINT ["dotnet", "DataLifecycleEngine.Cli.dll"]
CMD ["--help"]

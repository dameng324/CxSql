using CxSql.Infrastructure.Storage;
using CxSql.Models;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class JsonFileConnectionStoreTests
{
    [Test]
    public async Task SaveAndLoadRoundTripsConnections()
    {
        var directory = CreateTempDirectory();
        var filePath = Path.Combine(directory, "connections.json");
        var store = new JsonFileConnectionStore(filePath);
        var connection = new DatabaseConnection
        {
            Name = "local",
            DatabaseType = DatabaseType.Sqlite,
            ConnectionString = "Data Source=local.db",
            CreatedAtUnixMs = 1,
            UpdatedAtUnixMs = 2,
        };

        await store.SaveAsync([connection], CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        if (loaded.Count != 1)
        {
            throw new InvalidOperationException("Expected one connection.");
        }

        if (loaded[0].Name != "local")
        {
            throw new InvalidOperationException("Expected connection name to round-trip.");
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cxsql-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }
}

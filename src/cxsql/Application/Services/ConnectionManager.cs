using CxSql.Infrastructure.Storage;
using CxSql.Models;
using Microsoft.Extensions.Logging;

namespace CxSql.Application.Services;

public sealed class ConnectionManager(
    IConnectionStore connectionStore,
    DatabaseProviderRegistry providerRegistry,
    ILogger<ConnectionManager> logger
)
{
    public async Task<IReadOnlyList<DatabaseConnection>> ListAsync(
        CancellationToken cancellationToken
    )
    {
        var connections = await connectionStore.LoadAsync(cancellationToken);
        return connections
            .OrderBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DatabaseConnection> CreateAsync(
        string name,
        DatabaseType databaseType,
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        ValidateConnectionInput(name, connectionString);

        var connections = (await connectionStore.LoadAsync(cancellationToken)).ToList();
        if (
            connections.Any(connection =>
                string.Equals(connection.Name, name, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new InvalidOperationException($"Connection '{name}' already exists.");
        }

        var now = UnixTime.NowMilliseconds();
        var connection = new DatabaseConnection
        {
            Name = name.Trim(),
            DatabaseType = databaseType,
            ConnectionString = connectionString.Trim(),
            CreatedAtUnixMs = now,
            UpdatedAtUnixMs = now,
        };

        connections.Add(connection);
        await connectionStore.SaveAsync(connections, cancellationToken);
        return connection;
    }

    public async Task<DatabaseConnection> UpdateAsync(
        DatabaseConnection updatedConnection,
        CancellationToken cancellationToken
    )
    {
        ValidateConnectionInput(updatedConnection.Name, updatedConnection.ConnectionString);

        var connections = (await connectionStore.LoadAsync(cancellationToken)).ToList();
        var index = connections.FindIndex(connection => connection.Id == updatedConnection.Id);
        if (index < 0)
        {
            throw new InvalidOperationException("Connection was not found.");
        }

        if (
            connections.Any(connection =>
                connection.Id != updatedConnection.Id
                && string.Equals(
                    connection.Name,
                    updatedConnection.Name,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            throw new InvalidOperationException(
                $"Connection '{updatedConnection.Name}' already exists."
            );
        }

        updatedConnection.UpdatedAtUnixMs = UnixTime.NowMilliseconds();
        connections[index] = updatedConnection;
        await connectionStore.SaveAsync(connections, cancellationToken);
        return updatedConnection;
    }

    public async Task DeleteAsync(string connectionId, CancellationToken cancellationToken)
    {
        var connections = (await connectionStore.LoadAsync(cancellationToken)).ToList();
        var removed = connections.RemoveAll(connection => connection.Id == connectionId);
        if (removed == 0)
        {
            throw new InvalidOperationException("Connection was not found.");
        }

        await connectionStore.SaveAsync(connections, cancellationToken);
    }

    public async Task<ConnectionTestResult> TestAsync(
        DatabaseConnection databaseConnection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var provider = providerRegistry.GetProvider(databaseConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                databaseConnection.ConnectionString
            );
            await connection.OpenAsync(cancellationToken);
            return ConnectionTestResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Connection test failed for {ConnectionName}",
                databaseConnection.Name
            );
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    private static void ValidateConnectionInput(string name, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Connection name is required.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string is required.");
        }
    }
}

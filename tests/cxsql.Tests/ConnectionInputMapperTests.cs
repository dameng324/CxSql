using CxSql.Models;
using CxSql.UI.Dialogs;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class ConnectionInputMapperTests
{
    [Test]
    public void SqliteDisplayValueUsesDataSourcePath()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "sample.db",
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var displayValue = ConnectionInputMapper.ToDisplayValue(
            DatabaseType.Sqlite,
            connectionString
        );

        if (displayValue != "sample.db")
        {
            throw new InvalidOperationException(
                $"Expected SQLite display value to be sample.db, got {displayValue}."
            );
        }
    }

    [Test]
    public void SqliteInputBuildsConnectionString()
    {
        var connectionString = ConnectionInputMapper.ToConnectionString(
            DatabaseType.Sqlite,
            "sample.db"
        );
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (!builder.DataSource.EndsWith("sample.db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected SQLite data source to end with sample.db, got {builder.DataSource}."
            );
        }

        if (builder.Mode != SqliteOpenMode.ReadWriteCreate)
        {
            throw new InvalidOperationException("Expected SQLite mode to be ReadWriteCreate.");
        }
    }

    [Test]
    public void NetworkDatabaseInputKeepsConnectionString()
    {
        const string connectionString = "Host=localhost;Database=app;Username=app;Password=p;";

        var mapped = ConnectionInputMapper.ToConnectionString(
            DatabaseType.PostgreSql,
            connectionString
        );

        if (mapped != connectionString)
        {
            throw new InvalidOperationException(
                "Expected PostgreSQL connection string to remain unchanged."
            );
        }
    }

    [Test]
    public void PostgreSqlServerFieldsBuildConnectionString()
    {
        var connectionString = ConnectionInputMapper.ToServerConnectionString(
            DatabaseType.PostgreSql,
            new ServerConnectionFields("127.0.0.1", 5432, "appdb", "appuser", "secret")
        );
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (
            builder.Host != "127.0.0.1"
            || builder.Port != 5432
            || builder.Database != "appdb"
            || builder.Username != "appuser"
            || builder.Password != "secret"
        )
        {
            throw new InvalidOperationException(
                $"Unexpected PostgreSQL connection string: {connectionString}."
            );
        }
    }

    [Test]
    public void SqlServerServerFieldsBuildConnectionString()
    {
        var connectionString = ConnectionInputMapper.ToServerConnectionString(
            DatabaseType.SqlServer,
            new ServerConnectionFields("10.0.0.5", 1444, "appdb", "sa", "secret")
        );
        var builder = new SqlConnectionStringBuilder(connectionString);

        if (
            builder.DataSource != "10.0.0.5,1444"
            || builder.InitialCatalog != "appdb"
            || builder.UserID != "sa"
            || builder.Password != "secret"
            || !builder.Encrypt
            || !builder.TrustServerCertificate
        )
        {
            throw new InvalidOperationException(
                $"Unexpected SQL Server connection string: {connectionString}."
            );
        }
    }

    [Test]
    public void SafePostgreSqlConnectionStringMasksPassword()
    {
        const string connectionString = "Host=localhost;Database=app;Username=app;Password=secret;";

        var safeValue = ConnectionInputMapper.ToSafeConnectionString(
            DatabaseType.PostgreSql,
            connectionString
        );

        if (
            safeValue.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || !safeValue.Contains("******", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Expected PostgreSQL password to be masked, got {safeValue}."
            );
        }
    }

    [Test]
    public void SafeSqlServerConnectionStringMasksPassword()
    {
        const string connectionString =
            "Server=localhost;Database=app;User ID=sa;Password=secret;TrustServerCertificate=True;";

        var safeValue = ConnectionInputMapper.ToSafeConnectionString(
            DatabaseType.SqlServer,
            connectionString
        );

        if (
            safeValue.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || !safeValue.Contains("******", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Expected SQL Server password to be masked, got {safeValue}."
            );
        }
    }
}

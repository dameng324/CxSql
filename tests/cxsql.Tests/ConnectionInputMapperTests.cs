using CxSql.Models;
using CxSql.UI.Dialogs;
using Microsoft.Data.Sqlite;
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
}

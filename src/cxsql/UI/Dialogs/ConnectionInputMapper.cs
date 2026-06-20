using CxSql.Models;
using Microsoft.Data.Sqlite;

namespace CxSql.UI.Dialogs;

public static class ConnectionInputMapper
{
    public static string GetDisplayType(DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.Sqlite => "SQLite",
            DatabaseType.PostgreSql => "PostgreSQL",
            DatabaseType.SqlServer => "SQL Server",
            _ => databaseType.ToString(),
        };
    }

    public static string GetInputLabel(DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.Sqlite => "SQLite file path: ",
            DatabaseType.PostgreSql => "PostgreSQL connection string: ",
            DatabaseType.SqlServer => "SQL Server connection string: ",
            _ => "Connection string: ",
        };
    }

    public static string GetHelpText(DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.Sqlite =>
                "For SQLite, enter the database file path. cxsql builds the connection string.",
            DatabaseType.PostgreSql => "For PostgreSQL, enter the full connection string.",
            DatabaseType.SqlServer => "For SQL Server, enter the full connection string.",
            _ => "Enter the connection string.",
        };
    }

    public static string ToDisplayValue(DatabaseType databaseType, string connectionString)
    {
        if (databaseType != DatabaseType.Sqlite)
        {
            return connectionString;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource)
                ? connectionString
                : builder.DataSource;
        }
        catch (ArgumentException)
        {
            return connectionString;
        }
    }

    public static string ToConnectionString(DatabaseType databaseType, string rawValue)
    {
        if (databaseType != DatabaseType.Sqlite)
        {
            return rawValue;
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(rawValue),
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }
}

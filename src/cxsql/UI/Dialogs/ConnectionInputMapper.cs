using CxSql.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace CxSql.UI.Dialogs;

public sealed record ServerConnectionFields(
    string Host,
    int Port,
    string? Database,
    string Username,
    string Password
);

public static class ConnectionInputMapper
{
    public static int GetDefaultPort(DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.PostgreSql => 5432,
            DatabaseType.SqlServer => 1433,
            _ => 0,
        };
    }

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

    public static string ToServerConnectionString(
        DatabaseType databaseType,
        ServerConnectionFields fields
    )
    {
        return databaseType switch
        {
            DatabaseType.PostgreSql => BuildPostgreSqlConnectionString(fields),
            DatabaseType.SqlServer => BuildSqlServerConnectionString(fields),
            _ => throw new InvalidOperationException(
                $"Server fields are not supported for {databaseType}."
            ),
        };
    }

    private static string BuildPostgreSqlConnectionString(ServerConnectionFields fields)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = fields.Host,
            Port = fields.Port,
            Username = fields.Username,
            Password = fields.Password,
            Timeout = 15,
        };
        if (!string.IsNullOrWhiteSpace(fields.Database))
        {
            builder.Database = fields.Database.Trim();
        }

        return builder.ToString();
    }

    private static string BuildSqlServerConnectionString(ServerConnectionFields fields)
    {
        var dataSource =
            fields.Port == GetDefaultPort(DatabaseType.SqlServer)
                ? fields.Host
                : $"{fields.Host},{fields.Port}";
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            UserID = fields.Username,
            Password = fields.Password,
            ConnectTimeout = 15,
            Encrypt = true,
            TrustServerCertificate = true,
        };
        if (!string.IsNullOrWhiteSpace(fields.Database))
        {
            builder.InitialCatalog = fields.Database.Trim();
        }

        return builder.ToString();
    }
}

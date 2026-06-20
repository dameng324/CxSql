using System.Data.Common;
using CxSql.Models;

namespace CxSql.Database.Providers;

public interface IDatabaseProvider
{
    string ProviderName { get; }

    DbConnection CreateConnection(string connectionString);

    Task<IReadOnlyList<DatabaseObject>> GetDatabaseObjectsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    );

    Task<QueryResult> ExecuteSqlAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken
    );
}

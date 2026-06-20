using CxSql.Models;

namespace CxSql.Database.Providers;

public interface IPreviewSqlBuilder
{
    string BuildPreviewSql(DatabaseObject databaseObject, int rowLimit);
}

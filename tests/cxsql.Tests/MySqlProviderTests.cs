using CxSql.Database.Providers;
using CxSql.Models;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class MySqlProviderTests
{
    [Test]
    public void PreviewSqlQuotesDatabaseAndTable()
    {
        var provider = new MySqlProvider();
        var sql = provider.BuildPreviewSql(
            new DatabaseObject
            {
                Schema = "my`db",
                Name = "select`rows",
                ObjectType = DatabaseObjectType.Table,
            },
            100
        );

        if (sql != "SELECT * FROM `my``db`.`select``rows` LIMIT 100;")
        {
            throw new InvalidOperationException($"Unexpected MySQL preview SQL: {sql}");
        }
    }
}

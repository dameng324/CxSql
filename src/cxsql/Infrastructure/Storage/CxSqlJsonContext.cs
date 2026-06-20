using System.Text.Json.Serialization;
using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DatabaseConnection>))]
[JsonSerializable(typeof(List<QueryHistory>))]
[JsonSerializable(typeof(List<FavoriteQuery>))]
internal sealed partial class CxSqlJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CxSql.Infrastructure.Storage;

internal sealed class JsonFileListStore<T>(string filePath, JsonTypeInfo<List<T>> jsonTypeInfo)
{
    public async Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken) ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<T> items, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = items.ToList();
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, jsonTypeInfo, cancellationToken);
    }
}

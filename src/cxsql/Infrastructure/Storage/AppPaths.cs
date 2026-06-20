namespace CxSql.Infrastructure.Storage;

public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        ConnectionsFile = Path.Combine(rootDirectory, "connections.json");
        QueryHistoryFile = Path.Combine(rootDirectory, "query-history.json");
        FavoritesFile = Path.Combine(rootDirectory, "favorites.json");
        LogsDirectory = Path.Combine(rootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string ConnectionsFile { get; }

    public string QueryHistoryFile { get; }

    public string FavoritesFile { get; }

    public string LogsDirectory { get; }

    public static AppPaths CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cxsql"
        );

        return new AppPaths(root);
    }
}

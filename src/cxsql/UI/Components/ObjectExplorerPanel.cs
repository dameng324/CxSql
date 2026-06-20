using CxSql.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Components;

public sealed class ObjectExplorerPanel
{
    private static readonly object ConnectionsRootTag = new();
    private readonly TreeControl tree;
    private IReadOnlyList<DatabaseConnection> connections = [];
    private DatabaseConnection? activeConnection;
    private IReadOnlyList<DatabaseObject> objects = [];

    public ObjectExplorerPanel()
    {
        tree = Controls
            .Tree()
            .WithGuide(TreeGuide.Line)
            .WithHighlightColors(Color.White, new Color(40, 60, 100))
            .WithBackgroundColor(Color.Transparent)
            .WithScrollbarVisibility(ScrollbarVisibility.Auto)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
        tree.HoverEnabled = true;
        tree.SelectOnRightClick = true;

        tree.SelectedNodeChanged += (_, args) =>
        {
            switch (args.Node?.Tag)
            {
                case DatabaseConnection connection:
                    ConnectionSelected?.Invoke(connection);
                    break;
                case DatabaseObject databaseObject:
                    ObjectSelected?.Invoke(databaseObject);
                    break;
            }
        };

        tree.NodeActivated += (_, args) =>
        {
            switch (args.Node?.Tag)
            {
                case DatabaseConnection connection:
                    ConnectionActivated?.Invoke(connection);
                    break;
                case DatabaseObject databaseObject:
                    ObjectActivated?.Invoke(databaseObject);
                    break;
            }
        };

        tree.MouseRightClick += (_, args) =>
        {
            var rightClickedNode = tree.LastRightClickedNode;
            if (rightClickedNode is null)
            {
                ConnectionsRightClicked?.Invoke(args);
                return;
            }

            switch (rightClickedNode.Tag)
            {
                case DatabaseConnection connection:
                    ConnectionRightClicked?.Invoke(connection, args);
                    break;
                case DatabaseObject databaseObject:
                    ObjectRightClicked?.Invoke(databaseObject, args);
                    break;
                case not null when ReferenceEquals(rightClickedNode.Tag, ConnectionsRootTag):
                    ConnectionsRightClicked?.Invoke(args);
                    break;
            }
        };
    }

    public TreeControl Control => tree;

    public event Action<DatabaseConnection>? ConnectionSelected;

    public event Action<DatabaseConnection>? ConnectionActivated;

    public event Action<DatabaseConnection, MouseEventArgs>? ConnectionRightClicked;

    public event Action<MouseEventArgs>? ConnectionsRightClicked;

    public event Action<DatabaseObject>? ObjectSelected;

    public event Action<DatabaseObject>? ObjectActivated;

    public event Action<DatabaseObject, MouseEventArgs>? ObjectRightClicked;

    public void SetConnections(
        IReadOnlyList<DatabaseConnection> nextConnections,
        DatabaseConnection? nextActiveConnection,
        IReadOnlyList<DatabaseObject> nextObjects
    )
    {
        connections = nextConnections;
        activeConnection = nextActiveConnection;
        objects = nextObjects;
        Rebuild();
    }

    private void Rebuild()
    {
        tree.Clear();

        foreach (var connection in connections)
        {
            var activeMarker =
                activeConnection?.Id == connection.Id ? "[green]✳️[/] " : "[grey50]○[/] ";
            var node = tree.AddRootNode(
                $"{activeMarker}[white]{MarkupParser.Escape(connection.Name)}[/] [grey50]({connection.DatabaseType})[/]"
            );
            node.Tag = connection;
            node.IsExpanded = activeConnection?.Id == connection.Id;

            if (activeConnection?.Id == connection.Id)
            {
                AddObjectGroups(node);
            }
        }

        if (connections.Count == 0)
        {
            var emptyNode = tree.AddRootNode("[grey50]No saved connections[/]");
            emptyNode.Tag = ConnectionsRootTag;
        }
    }

    private void AddObjectGroups(TreeNode connectionNode)
    {
        foreach (var group in objects.GroupBy(item => item.ObjectType).OrderBy(item => item.Key))
        {
            var groupNode = connectionNode.AddChild($"[yellow]{group.Key}[/]");
            groupNode.IsExpanded = true;

            foreach (var databaseObject in group.OrderBy(item => item.DisplayName))
            {
                var child = groupNode.AddChild(MarkupParser.Escape(databaseObject.DisplayName));
                child.Tag = databaseObject;
            }
        }

        if (objects.Count == 0)
        {
            connectionNode.AddChild("[grey50]No database objects loaded[/]");
        }
    }
}

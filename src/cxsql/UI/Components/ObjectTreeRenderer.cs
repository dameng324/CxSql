using CxSql.Models;

namespace CxSql.UI.Components;

public sealed class ObjectTreeRenderer
{
    public void Render(IReadOnlyList<DatabaseObject> objects, TextWriter writer)
    {
        if (objects.Count == 0)
        {
            writer.WriteLine("Object Tree: no objects loaded.");
            return;
        }

        writer.WriteLine("Object Tree:");
        foreach (
            var group in objects
                .GroupBy(databaseObject => databaseObject.ObjectType)
                .OrderBy(group => group.Key)
        )
        {
            writer.WriteLine($"  {group.Key}");
            foreach (var databaseObject in group.OrderBy(item => item.DisplayName))
            {
                writer.WriteLine($"    - {databaseObject.DisplayName}");
            }
        }
    }
}

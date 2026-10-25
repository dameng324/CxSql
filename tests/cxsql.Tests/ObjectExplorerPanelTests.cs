using CxSql.Models;
using CxSql.UI.Components;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class ObjectExplorerPanelTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void KeepsCaseSensitiveSchemasAndTheirMembersSeparate(bool includeSchemaObjects)
    {
        var connection = new DatabaseConnection
        {
            Name = "PostgreSQL",
            DatabaseType = DatabaseType.PostgreSql,
        };
        var schemaNames = new[] { "Sales", "sales" };
        var tables = schemaNames
            .Select(name => new DatabaseObject
            {
                Name = "people",
                Schema = name,
                ObjectType = DatabaseObjectType.Table,
            })
            .ToArray();
        var schemas = schemaNames
            .Select(name => new DatabaseObject
            {
                Name = name,
                ObjectType = DatabaseObjectType.Schema,
            })
            .ToArray();
        var objects = includeSchemaObjects ? schemas.Concat(tables).ToArray() : tables;
        var explorer = new ObjectExplorerPanel();

        explorer.SetConnections([connection], connection, objects);

        var schemaNodes = explorer.Control.RootNodes.Single().Children.ToArray();
        if (schemaNodes.Length != 2)
        {
            throw new InvalidOperationException(
                $"Expected separate Sales and sales schema nodes, but found {schemaNodes.Length}."
            );
        }

        foreach (var table in tables)
        {
            var schemaNode = schemaNodes.Single(node =>
                node.Tag is DatabaseObject schema
                && schema.ObjectType == DatabaseObjectType.Schema
                && schema.Name == table.Schema
            );
            if (
                includeSchemaObjects
                && !ReferenceEquals(
                    schemaNode.Tag,
                    schemas.Single(schema => schema.Name == table.Schema)
                )
            )
            {
                throw new InvalidOperationException("Schema preview must retain the schema tag.");
            }

            var members = schemaNode.Children.SelectMany(group => group.Children).ToArray();
            if (members.Length != 1 || !ReferenceEquals(members[0].Tag, table))
            {
                throw new InvalidOperationException(
                    $"Schema {table.Schema} must contain only its own people table."
                );
            }
        }
    }
}

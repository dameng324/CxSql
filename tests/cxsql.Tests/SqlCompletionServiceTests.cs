using CxSql.Application.Services;
using CxSql.Models;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class SqlCompletionServiceTests
{
    [Test]
    public void SuggestionsIncludeKeywordsObjectsAndColumns()
    {
        var service = new SqlCompletionService();
        var suggestions = service.GetSuggestions(
            "SEL",
            [new DatabaseObject { Name = "people", ObjectType = DatabaseObjectType.Table }],
            [
                new DatabaseColumn
                {
                    Name = "person_name",
                    TableName = "people",
                    DataType = "TEXT",
                },
            ],
            []
        );

        if (!suggestions.Any(suggestion => suggestion.ReplacementText == "SELECT"))
        {
            throw new InvalidOperationException("Expected SELECT keyword completion.");
        }

        var completed = SqlCompletionService.ApplyCompletion(
            "SEL",
            suggestions.Single(suggestion => suggestion.ReplacementText == "SELECT")
        );
        if (completed != "SELECT ")
        {
            throw new InvalidOperationException(
                "Expected completion to replace the current prefix."
            );
        }

        suggestions = service.GetSuggestions(
            "person_",
            [new DatabaseObject { Name = "people", ObjectType = DatabaseObjectType.Table }],
            [
                new DatabaseColumn
                {
                    Name = "person_name",
                    TableName = "people",
                    DataType = "TEXT",
                },
            ],
            []
        );

        if (!suggestions.Any(suggestion => suggestion.ReplacementText == "person_name"))
        {
            throw new InvalidOperationException("Expected column completion.");
        }

        suggestions = service.GetSuggestions(
            "peo",
            [new DatabaseObject { Name = "people", ObjectType = DatabaseObjectType.Table }],
            [],
            []
        );

        if (!suggestions.Any(suggestion => suggestion.ReplacementText == "people"))
        {
            throw new InvalidOperationException("Expected table completion.");
        }

        var prefix = SqlCompletionService.GetCurrentPrefixAtCursor(
            "SELECT person_name\nFROM people",
            currentLine: 1,
            currentColumn: 19
        );
        if (prefix != "person_name")
        {
            throw new InvalidOperationException("Expected cursor-aware completion prefix.");
        }
    }
}

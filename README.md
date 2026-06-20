# cxsql

cxsql is a cross-platform terminal SQL client. The product direction is a
Navicat-like database management workflow adapted to a terminal TUI.

## Current MVP

- Manage local JSON database connections.
- Open SQLite, PostgreSQL, and SQL Server connections.
- Execute SQL through ADO.NET.
- Display query results in a terminal grid.
- Show SQL errors without crashing the app.
- Export the latest result set to CSV.
- Store query history locally.

## UI

cxsql now starts in a SharpConsoleUI window system:

- Minimal top bar with the visible exit command.
- Left connection and object tree with mouse context menus.
- Center SQL editor tabs using `MultilineEditControl`.
- Query toolbar with visible Execute, Stop, Save SQL, and History actions.
- Bottom result area with tabs for result grid and messages.
- Draggable splitters between the object tree, editor, and result area.
- Mouse activation for toolbar buttons, connection rows, object tree nodes, and
  result grid selection.

## UX Rules

- Mouse-first, keyboard-shortcuts-second.
- No implicit shortcuts.
- Common shortcuts must be visible next to their toolbar buttons.
- Current allowed shortcuts:
  - `F5`: Execute current SQL
  - `Ctrl+S`: Save SQL
  - `Ctrl+Q`: Exit
  - `Esc`: Close dialog

The terminal fallback is only kept for non-interactive environments and legacy
diagnostics; the normal app path uses SharpConsoleUI controls directly.

## Install

```powershell
dotnet tool install --global cxsql
cxsql
```

## Build

```powershell
dotnet tool restore
dotnet build cxsql.slnx
dotnet test cxsql.slnx
```

## Run

```powershell
dotnet run --project src/cxsql/cxsql.csproj
```

## Format

```powershell
dotnet tool run csharpier -- check .
dotnet tool run csharpier -- format .
```

## Publish

The `Publish` GitHub Actions workflow builds Native AOT tool packages using the
multi-RID pattern:

- platform-specific packages: `dotnet pack -p:ToolType=aot --use-current-runtime`
- agnostic package: `dotnet pack -p:ToolType=aot`
- collected artifacts: `all-aot-packages`

Publishing to nuget.org uses OIDC through `NuGet/login@v1`. Configure NuGet
trusted publishing for the repository and set the `NUGET_USER` repository
variable to the nuget.org account name.

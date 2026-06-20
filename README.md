# cxsql

cxsql is a cross-platform terminal SQL client. The product direction is a
Navicat-like database management workflow adapted to a terminal TUI.

## Current MVP

- Manage local JSON database connections.
- Open SQLite connections.
- Execute SQL through ADO.NET.
- Display query results in a terminal grid.
- Show SQL errors without crashing the app.
- Export the latest result set to CSV.
- Store query history locally.

PostgreSQL and SQL Server providers are present behind the same interface and
will be expanded after the SQLite path is complete.

## UI

cxsql now starts in a SharpConsoleUI window system:

- Top toolbar with visible command buttons.
- Left connection and object tree.
- Center SQL editor tabs using `MultilineEditControl`.
- Bottom result area with tabs for grid, messages, and saved connections.
- Draggable splitters between the object tree, editor, and result area.
- Mouse activation for toolbar buttons, connection rows, object tree nodes, and
  result grid selection.

## UX Rules

- Mouse-first, keyboard-shortcuts-second.
- No implicit shortcuts.
- Common shortcuts must be visible next to their toolbar buttons.
- Current allowed shortcuts:
  - `F5`: Execute current SQL
  - `Ctrl+N`: New query
  - `Ctrl+S`: Save SQL
  - `Esc`: Close dialog

The terminal fallback is only kept for non-interactive environments and legacy
diagnostics; the normal app path uses SharpConsoleUI controls directly.

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

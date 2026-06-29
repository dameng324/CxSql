# cxsql

cxsql is a mouse-first SQL terminal client for people who want a practical
database workspace without leaving the terminal. It combines saved connections,
an object explorer, SQL editor tabs, result grids, table metadata, query history,
CSV export, and copy workflows in a cross-platform TUI.

## Highlights

- Connect to SQLite, PostgreSQL, and SQL Server.
- Save connection profiles locally as JSON.
- Browse database objects in a left-side explorer.
- Double-click tables and views to preview data.
- Open table details for structure, constraints, indexes, triggers, and DDL.
- Run SQL in tabbed editors with syntax highlighting and completion.
- Work with results in a sortable, filterable grid.
- Generate SQL from result-grid column sort and filter actions.
- Export visible result data to CSV or copy it as tab-delimited text.
- Review and reload previous queries from local history.

## Install

```powershell
dotnet tool install --global cxsql
cxsql
```

## Use

Start `cxsql`, create or open a connection, then use the object tree and editor
together:

- Double-click a saved connection to open it.
- Double-click a table or view to run a preview query.
- Right-click a table or view and choose `Open Details` to inspect metadata.
- Right-click a result-grid header to sort or build a SQL filter.
- Use the `ResultGrid` actions to export CSV, copy all rows, or clear filters.
- Use `F5` to execute the active SQL editor.

## Logs

cxsql keeps runtime diagnostics out of the interactive TUI. Warning and error
logs are written under `~/.cxsql/logs` using a per-startup file name such as
`cxsql-20260629-143015.log`.

At startup, cxsql automatically deletes its own log files older than 30 days.

## Build

```powershell
dotnet tool restore
dotnet build cxsql.slnx
dotnet test cxsql.slnx
```

## Run From Source

```powershell
dotnet run --project src/cxsql/cxsql.csproj
```

## Format

```powershell
dotnet tool run csharpier -- check .
dotnet tool run csharpier -- format .
```

## Publish

The `Publish` GitHub Actions workflow runs from version tags and manual
dispatches. It builds Native AOT dotnet-tool packages with the multi-RID
package pattern:

- platform-specific packages: `dotnet pack -p:ToolType=aot --use-current-runtime`
- agnostic package: `dotnet pack -p:ToolType=aot`
- collected artifact: `all-aot-packages`

Publishing to nuget.org uses OIDC through `NuGet/login@v1`. Configure NuGet
trusted publishing for the repository and the `BigDream` nuget.org account.

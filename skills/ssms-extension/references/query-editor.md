# Query editor

Working with query windows: the connection, the script text, new windows, execution and results.
Template code: `Ssms/QueryWindow.cs` (always included) and `QueryEditor/` (sample command).

## Contents

- [Three layers](#three-layers)
- [The active connection](#the-active-connection)
- [Running your own queries](#running-your-own-queries)
- [Reading and changing the script](#reading-and-changing-the-script)
- [Opening a query window](#opening-a-query-window)
- [Executing](#executing)
- [Context menus](#context-menus)
- [Results grid and Messages](#results-grid-and-messages)
- [Reacting to typing](#reacting-to-typing)
- [Parsing T-SQL](#parsing-t-sql)

## Three layers

A query window is a Visual Studio document whose editor is the standard VS text editor, hosted in
an SSMS control that adds the connection and the results panes. Each layer has its own API:

| Need | Use | Supported? |
| --- | --- | --- |
| Text, selection, caret, simple edits | `EnvDTE` automation (`DTE.ActiveDocument`) | Public, stable |
| Text buffers, adornments, completion, key handling | VS editor APIs (`IWpfTextView`, MEF) | Public, stable |
| Connection, new connected windows | `ServiceCache.ScriptFactory` (`IScriptFactory`) | SSMS internal, long-lived |
| Results grid, Messages tab, execution state | `SQLEditors.dll` types by reflection | SSMS internal, fragile |

Use the highest row that can do the job.

## The active connection

```csharp
ThreadHelper.ThrowIfNotOnUIThread();
UIConnectionInfo? info = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo?.UIConnectionInfo;
```

`ServiceCache` is in `SqlPackageBase.dll` (namespace `Microsoft.SqlServer.Management.UI.VSIntegration`),
`UIConnectionInfo` in `Microsoft.SqlServer.RegSvrEnum.dll`. The result is null, or has an empty
`ServerName`, when the active document is not a connected query window; treat both as "no
connection". Wrap the access in try/catch.

| `UIConnectionInfo` member | Meaning |
| --- | --- |
| `ServerName` | Server as the user typed it |
| `UserName` | Login; empty for integrated authentication |
| `AuthenticationType` | Numeric authentication mode |
| `AdvancedOptions` | Name/value collection with the remaining connection settings |
| `Password`, `InMemoryPassword` | Present for SQL logins. Do not read, log or store them |

The database is not a property. Use `UIConnectionInfoUtil.GetDatabaseName(info)` (in
`ConnectionDlg.dll`). It is the database the window was connected with, or empty for the login's
default; it does not follow a later `USE` statement in the window.

## Running your own queries

Open a separate connection with the same settings. Never try to borrow the query window's own
session: the user's transactions, temp tables and `SET` options live there.

```csharp
ThreadHelper.ThrowIfNotOnUIThread();
if (UIConnectionInfoUtil.GetCoreConnectionInfo(info) is SqlConnectionInfo core)
{
    var copy = (SqlConnectionInfo)core.Copy();
    var database = UIConnectionInfoUtil.GetDatabaseName(info);
    if (!string.IsNullOrEmpty(database)) { copy.DatabaseName = database; }
    copy.ApplicationName = "My Extension";
    DbConnection connection = (DbConnection)copy.CreateConnectionObject();   // not yet open
}
```

The template wraps this as `QueryWindow.CreateConnection()`. Going through `SqlConnectionInfo`
(SMO, `Microsoft.SqlServer.ConnectionInfo.dll`) makes Windows, SQL Server and Microsoft Entra
authentication, encryption and certificate settings behave as they do in SSMS, with no
credentials passing through extension code. `GetCoreConnectionInfo` returns something else for
Analysis Services and other server types; the `is SqlConnectionInfo` test filters those out.

Then:

- Capture the connection on the UI thread, **open and query on a background thread**
  (`Background.Run` and `await TaskScheduler.Default`). Opening can take seconds, or prompt.
- Set `ApplicationName`, so DBAs can tell the extension's sessions from the user's.
- Set a short `CommandTimeout`, and keep metadata queries cheap. A tool that runs unprompted
  queries against production servers has to be a good citizen.
- Program against `System.Data.Common` (`DbConnection`, `DbCommand`) where possible. The concrete
  type is `Microsoft.Data.SqlClient.SqlConnection` from SSMS's own copy; reference that assembly
  compile-only, never from NuGet with runtime assets.

## Reading and changing the script

The automation model is enough for most commands:

```csharp
ThreadHelper.ThrowIfNotOnUIThread();
var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(SDTE));
var document = dte.ActiveDocument;                               // null when no document is open
var selection = document?.Selection as EnvDTE.TextSelection;

string selected = selection?.Text ?? string.Empty;               // empty when nothing is selected

if (document?.Object("TextDocument") is EnvDTE.TextDocument text)
{
    string all = text.StartPoint.CreateEditPoint().GetText(text.EndPoint);
}

selection?.Insert("-- inserted", (int)EnvDTE.vsInsertFlags.vsInsertFlagsContainNewText);
```

`Insert` replaces the selection, or inserts at the caret when nothing is selected. Each automation
edit is one undo step. To make several edits undo as one, wrap them:
`dte.UndoContext.Open("My Extension edit"); try { ... } finally { dte.UndoContext.Close(); }`.

`ActiveDocument` can be any document (a `.sql` file, an execution plan, a table designer).
`QueryWindow.IsActive` in the template checks that the active document's view is SSMS's query
editor control before a command acts on it.

For anything beyond simple edits, get the VS editor view of the active document and work with
its text buffer (`view.TextBuffer.CurrentSnapshot`, `view.Selection`, `view.Caret`,
`buffer.Replace`):

```csharp
var selectionService = (IVsMonitorSelection)Package.GetGlobalService(typeof(SVsShellMonitorSelection));
selectionService.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frameObject);
((IVsWindowFrame)frameObject).GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView);

if (docView is IVsCodeWindow codeWindow && codeWindow.GetLastActiveView(out IVsTextView textView) == VSConstants.S_OK)
{
    var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
    var adapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();
    IWpfTextView view = adapters.GetWpfTextView(textView);
}
```

Asking for the document frame, rather than the focused window, returns the query window even
while focus is in its results grid or in a tool window.

## Opening a query window

```csharp
ThreadHelper.ThrowIfNotOnUIThread();
var factory = ServiceCache.ScriptFactory;                             // IScriptFactory
factory.CreateNewBlankScript(ScriptType.Sql);                         // SSMS picks the connection
factory.CreateNewBlankScript(ScriptType.Sql, uiConnectionInfo, null); // connected as given
```

`IScriptFactory` and `ScriptType` are in `SqlWorkbench.Interfaces.dll`, namespace
`Microsoft.SqlServer.Management.UI.VSIntegration.Editors`. The new window becomes the active
document, so the text is put in through `dte.ActiveDocument` right after the call
(`QueryWindow.Open(text, connection)` in the template does both).

- Without a connection argument SSMS decides as for its own New Query button: the server selected
  in Object Explorer, or the active window's connection.
- Passing the `UIConnectionInfo` of the active window opens a second window on the same server.
- To pin the database regardless of how the window connects, start the script with `USE [db];`.

## Executing

```csharp
dte.ExecuteCommand("Query.Execute");
```

runs the active query window as F5 does: the selection if there is one, otherwise the whole
script. It returns immediately; execution continues asynchronously and there is no public
completion event. To run a specific statement, select it first (`TextSelection.MoveToLineAndOffset`
with the extend flag), execute, and restore the selection.

Executing on the user's behalf can change or destroy data. Do it only as the direct, obvious
result of a command the user invoked, never as a side effect, and ask for confirmation when the
script was generated by the extension.

To observe executions (query history, timing), subscribe to the command's events through
`dte.Events.CommandEvents` for the `Query.Execute` command's GUID and id (look them up once with
`dte.Commands.Item("Query.Execute")`). Keep a reference to the `CommandEvents` object in a field,
or the COM event sink is garbage collected and the events stop.

## Context menus

Parent groups for the query window's right-click menus (declare the symbols as shown in
[commands-and-menus.md](commands-and-menus.md)):

| Menu | GUID `{33F13AC3-80BB-4ECB-85BC-225435603A5E}`, id |
| --- | --- |
| Query text editor | `0x0050` |
| Messages tab | `0x0060` |
| Results grid | `0x0070` |
| Execution plan | `0x0080` |

## Results grid and Messages

There is no public API for the results. Extensions that read grid cells or the Messages text
reflect into `SQLEditors.dll` (in `Common7\IDE\Extensions\Application`), starting from the
document view object (`SqlScriptEditorControl`) and walking to its results control and grid
containers. Member names differ between SSMS versions, including between 21 and 22.

Before building on this:

1. Tell the user the feature depends on private members and may break with any SSMS update.
2. Inspect the installed version (`scripts\inspect-ssms-type.ps1 -Assembly SQLEditors -NonPublic -Type ...`)
   instead of copying member names from another extension.
3. Resolve every member defensively, cache the `MemberInfo` objects, and when one is missing
   disable the feature with a logged message naming the member.
4. Consider whether the feature can be done without the grid: rerunning the query on the
   extension's own connection is slower but uses only stable APIs.

## Reacting to typing

Completion, snippets, key interception and adornments use the regular Visual Studio editor
extensibility model: MEF exports such as `IWpfTextViewCreationListener`,
`IVsTextViewCreationListener` and completion sources. Requirements specific to a VSIX for SSMS:

- Add the MEF asset to the manifest, or the exports are never discovered:
  `<Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%|" />`
- Query windows have their own content type, whose name is not documented. Export for the base
  content type (`[ContentType("text")]`) and filter at runtime, for example on the document's
  `.sql` extension or a content type name containing "sql", after logging the actual
  `view.TextBuffer.ContentType.TypeName` once to learn it.
- MEF parts load independently of the package. They cannot assume the package's
  `InitializeAsync` has run.
- Anything on the typing path must be O(1) or limited to the current line. Do not scan the script
  or touch the database per keystroke; do that on an explicit trigger, or in the background with
  a cache. A command filter must never throw: catch, log, and pass the key on.
- MEF caches its catalog. After adding or changing exports, an installed development build may
  need the cache cleared (see [build-install-debug.md](build-install-debug.md)).

## Parsing T-SQL

SSMS ships `Microsoft.SqlServer.TransactSql.ScriptDom.dll` (in `Extensions\Application`), a full
T-SQL parser: `new TSql170Parser(initialQuotedIdentifiers: true).Parse(reader, out errors)`
returns a syntax tree with positions. Check which parser classes the installed version has before
choosing one. Reference it compile-only like the other SSMS assemblies; shipping a different
version in the VSIX has broken extensions on SSMS updates. For logic that lives in a separate,
unit-tested library, the ScriptDom NuGet package can be used there for tests, as long as the copy
is excluded from the VSIX.

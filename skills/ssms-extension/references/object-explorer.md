# Object Explorer

Adding items to the right-click menu of Object Explorer nodes and finding out what the selected
node is. Template code: `ObjectExplorer/ObjectExplorerMenu.cs` and `ObjectExplorerNode.cs`.

## Contents

- [The service](#the-service)
- [Adding context-menu items](#adding-context-menu-items)
- [What a node tells you](#what-a-node-tells-you)
- [Node kinds](#node-kinds)
- [Acting on a node](#acting-on-a-node)
- [Limits and risks](#limits-and-risks)

## The service

`IObjectExplorerService` (in `SqlWorkbench.Interfaces.dll`, namespace
`Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer`) is the public entry point:

```csharp
ThreadHelper.ThrowIfNotOnUIThread();
var explorer = Package.GetGlobalService(typeof(IObjectExplorerService)) as IObjectExplorerService;
```

It returns null until Object Explorer exists, which can be after the package has loaded at
startup, so retry for a while instead of giving up on the first null.

| Member | Use |
| --- | --- |
| `GetSelectedNodes(out int count, out INodeInformation[] nodes)` | The selected node or nodes |
| `FindNode(string urn)` | Finds a node by its URN |
| `SynchronizeTree(INodeInformation node)` | Selects and reveals a node |
| `ConnectToServer(...)`, `DisconnectSelectedServer()` | Manage Object Explorer's connections |

## Adding context-menu items

SSMS builds each node's right-click menu at runtime as a WinForms `ContextMenuStrip`. The `.vsct`
command table has no parent to attach to, so every extension that extends this menu uses the same
technique:

1. Load the package at startup (`[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]`),
   since nothing else would load it before the user right-clicks.
2. Get the tree control from the service. It is exposed by a non-public `Tree` property:

   ```csharp
   var property = explorer.GetType().GetProperty("Tree",
       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
   var tree = property?.GetValue(explorer, null) as System.Windows.Forms.TreeView;
   ```

3. Subscribe to `tree.ContextMenuStripChanged`. SSMS assigns a new menu to the tree each time a
   node is right-clicked; the event is the moment to add items.
4. In the handler, read the selected node, decide which items apply, and append
   `ToolStripMenuItem`s to `tree.ContextMenuStrip`.

Rules for the handler:

- **Tag the items you add** and return early when the menu already contains one. The event can
  fire more than once for the same menu.
- **Catch everything.** The handler runs inside SSMS's menu code.
- **Stay fast.** It runs on every right-click, before the menu shows. No database calls; decide
  from the node's strings.
- Put the extension's items in one submenu named after the extension, after a separator, instead
  of scattering items through SSMS's menu.
- Click handlers are ordinary WinForms event handlers on the UI thread. Wrap each in try/catch and
  start slow work with `Background.Run`.

## What a node tells you

`INodeInformation` (which extends `INodeContext`):

| Member | Example | Meaning |
| --- | --- | --- |
| `UrnPath` | `Server/Database/Table` | The kind of node: type names only, no object names |
| `Context` | `Server[@Name='SRV01']/Database[@Name='Sales']/Table[@Name='Orders' and @Schema='dbo']` | The node as an SMO URN, with names |
| `NavigationContext` | similar to `Context` | Path used for navigation in the tree |
| `Name` | `Orders` | The object's name |
| `InvariantName` | `dbo.Orders` | The name qualified with its schema, for schema-scoped objects |
| `Connection` | | `SqlOlapConnectionInfoBase`: the server connection the node belongs to (`ServerName`, `UserName`, ...) |
| `Parent` | | The parent node, or null at the root |
| `this[string]` | | A named property of the underlying object, when available; inspect before relying on a name |

Parse `Context` with SMO's `Urn` class (`Microsoft.SqlServer.Management.Sdk.Sfc`) instead of
regular expressions; it handles quoting, including names that contain apostrophes:

```csharp
var urn = new Urn(node.Context);
string type     = urn.Type;                         // "Table"
string name     = urn.GetAttribute("Name");         // "Orders"
string schema   = urn.GetAttribute("Schema");       // "dbo"
string database = urn.GetNameForType("Database");   // "Sales"
string server   = urn.GetNameForType("Server");     // "SRV01"
```

Quote identifiers before putting them in T-SQL: `"[" + name.Replace("]", "]]") + "]"`.

## Node kinds

Decide what to offer from `UrnPath`. Common values:

| UrnPath | Node |
| --- | --- |
| `Server` | A connected server |
| `Server/Database` | A database |
| `Server/Database/Table` | A table |
| `Server/Database/View` | A view |
| `Server/Database/StoredProcedure` | A stored procedure |
| `Server/Database/UserDefinedFunction` | A function |
| `Server/Database/Synonym` | A synonym |
| `Server/Database/Table/Column` | A column |
| `Server/JobServer` | SQL Server Agent |
| `Server/JobServer/Job` | A SQL Server Agent job |
| `Server/DatabasesFolder`, `Server/JobServer/JobsFolder` | Folder nodes below the server |
| `Server/Database/UserTablesFolder`, `.../SystemTablesFolder`, `.../ViewsFolder`, `.../StoredProceduresFolder`, `.../SynonymsFolder` | Folder nodes below a database |

Folder nodes have a `UrnPath` ending in a folder word and the `Context` of the object that
contains them, so `urn.Type` and the last `UrnPath` segment differ for folders.

This list is partial and the strings are not documented. To find the value for any node, use the
template's **Copy Node Info** menu item: it puts `UrnPath`, `Context` and the parsed values for
the right-clicked node on the clipboard. Do that for each node kind a feature targets instead of
guessing the string.

## Acting on a node

**Open a query for it.** `QueryWindow.Open(script)` without a connection argument behaves like
SSMS's New Query button: the new window connects to the server selected in Object Explorer. Start
the script with `USE [database];` to pin the database, since the window may open in the login's
default database.

**Run something against its server yourself.** `node.Connection` is the server-level connection
information. For Database Engine nodes it is a `SqlConnectionInfo`:

```csharp
if (node.Connection is SqlConnectionInfo info)
{
    var copy = (SqlConnectionInfo)info.Copy();
    copy.DatabaseName = database;                 // from the URN
    copy.ApplicationName = "My Extension";
    var connection = (DbConnection)copy.CreateConnectionObject();   // unopened
    // open and use it on a background thread
}
```

Creating the connection through `SqlConnectionInfo` keeps the authentication the user chose
(Windows, SQL Server, Microsoft Entra) without the extension handling credentials. Do not build a
connection string from `UserName` and `Password` by hand; it misses the Entra modes and exposes
the password to more code than necessary.

**Refresh after a change.** The service has no refresh method. `SynchronizeTree(node)` selects
and reveals a node; whether a built-in refresh command can be run for it has to be tried in the
installed version, so tell the user to refresh the node when a feature changes objects.

## Limits and risks

- The `Tree` property is non-public. An SSMS update can rename or remove it; when the lookup
  returns null, log it and leave Object Explorer alone. Never fall back to walking the window
  hierarchy on a guess.
- The menu is WinForms. Items do not pick up the SSMS theme automatically; copying `ForeColor`
  and `BackColor` from the tree is what other extensions do when the default colours look wrong
  in a dark theme.
- Changing the tree itself (adding, moving or regrouping nodes) is a different and much more
  fragile undertaking than adding menu items: it depends on the private structure of the tree
  nodes and has needed a separate implementation per SSMS version in the extensions that do it.
  Tell the user about that cost before starting.
- Multi-selection: `GetSelectedNodes` can return several nodes. Decide whether a command applies
  to all of them or only the first, and say which in its text.

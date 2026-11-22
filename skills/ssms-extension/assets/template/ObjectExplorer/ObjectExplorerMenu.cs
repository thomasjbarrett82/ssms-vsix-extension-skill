using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;
using __ExtensionName__.Ssms;
using Task = System.Threading.Tasks.Task;

namespace __ExtensionName__.ObjectExplorer;

/// <summary>
/// Adds a "__DisplayName__" submenu to the right-click menu of Object Explorer nodes.
///
/// SSMS builds these menus at runtime as WinForms ContextMenuStrips, so the .vsct command table
/// cannot place commands in them. The established technique is to take the tree control out of
/// IObjectExplorerService by reflection, listen for the moment SSMS assigns a new menu to it,
/// and append items to that menu. It relies on a non-public property, so it can break with an
/// SSMS update: every step fails soft, and nothing here may throw into SSMS.
/// </summary>
internal static class ObjectExplorerMenu
{
    // Marks the items added here, so a menu is never extended twice.
    private const string ItemTag = "__ExtensionName__.ObjectExplorerMenu";

    private static AsyncPackage? s_package;
    private static IObjectExplorerService? s_explorer;
    private static TreeView? s_tree;

    /// <summary>Starts hooking Object Explorer in the background and returns immediately.</summary>
    public static void Start(AsyncPackage package)
    {
        s_package = package;
        Background.Run(package, "Hooking Object Explorer", () => HookWhenAvailableAsync(package));
    }

    private static async Task HookWhenAvailableAsync(AsyncPackage package)
    {
        // Object Explorer may not exist yet when the package loads at startup.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            if (TryHook())
            {
                return;
            }

            await Task.Delay(1000, package.DisposalToken);
        }

        Log.Error("Object Explorer was not found; its context menu was not extended.");
    }

    private static bool TryHook()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (Package.GetGlobalService(typeof(IObjectExplorerService)) is not IObjectExplorerService explorer)
        {
            return false;
        }

        var treeProperty = explorer.GetType().GetProperty(
            "Tree",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (treeProperty?.GetValue(explorer, null) is not TreeView tree)
        {
            return false;
        }

        s_explorer = explorer;
        s_tree = tree;
        tree.ContextMenuStripChanged += OnContextMenuStripChanged;
        return true;
    }

    /// <summary>SSMS assigns a freshly built menu to the tree each time a node is right-clicked.</summary>
    private static void OnContextMenuStripChanged(object sender, EventArgs e)
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menu = s_tree?.ContextMenuStrip;
            if (menu is null || s_explorer is null)
            {
                return;
            }

            foreach (ToolStripItem item in menu.Items)
            {
                if (ItemTag.Equals(item.Tag))
                {
                    return;
                }
            }

            s_explorer.GetSelectedNodes(out var count, out var nodes);
            if (count == 0 || nodes is null || nodes.Length == 0)
            {
                return;
            }

            var node = ObjectExplorerNode.From(nodes[0]);
            if (node is null)
            {
                return;
            }

            var submenu = BuildSubmenu(node);
            menu.Items.Add(new ToolStripSeparator { Tag = ItemTag });
            menu.Items.Add(submenu);
        }
        catch (Exception ex)
        {
            Log.Error("Extending the Object Explorer menu failed.", ex);
        }
    }

    /// <summary>The items offered for a node. Decide per node kind what makes sense.</summary>
    private static ToolStripMenuItem BuildSubmenu(ObjectExplorerNode node)
    {
        var submenu = new ToolStripMenuItem(PackageIds.DisplayName) { Tag = ItemTag };

        if (node.IsTableOrView)
        {
            AddItem(submenu, "Select Top Rows in New Query", node, SelectTopRows);
            AddItem(submenu, "Copy Quoted Name", node, n => Clipboard.SetText(n.QuotedName));
        }

        // Shows what SSMS reports for the node: use it to find the Kind values to act on.
        AddItem(submenu, "Copy Node Info", node, n => Clipboard.SetText(n.ToString()));
        return submenu;
    }

    private static void AddItem(ToolStripMenuItem parent, string text, ObjectExplorerNode node, Action<ObjectExplorerNode> action)
    {
        var item = new ToolStripMenuItem(text) { Tag = ItemTag };
        item.Click += (_, _) =>
        {
            try
            {
                action(node);
            }
            catch (Exception ex)
            {
                Log.Error($"Object Explorer command '{text}' failed.", ex);
                MessageBox.Show(ex.Message, PackageIds.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        parent.DropDownItems.Add(item);
    }

    private static void SelectTopRows(ObjectExplorerNode node)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // #if Options
        var rows = s_package is null ? 100 : Options.GeneralOptionsPage.Get(s_package).TopRowCount;
        // #else
        const int rows = 100;
        // #endif

        // Without an explicit connection the new window connects to the server selected in
        // Object Explorer, like SSMS's own New Query button. USE then pins the database.
        var script =
            $"USE {ObjectExplorerNode.Quote(node.Database)};{Environment.NewLine}" +
            $"GO{Environment.NewLine}{Environment.NewLine}" +
            $"SELECT TOP ({rows}) *{Environment.NewLine}" +
            $"FROM {node.QuotedName};{Environment.NewLine}";

        if (!QueryWindow.Open(script))
        {
            throw new InvalidOperationException("A new query window could not be opened.");
        }
    }
}

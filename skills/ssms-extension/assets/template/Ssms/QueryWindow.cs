using System;
using System.Data.Common;
using EnvDTE;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.ConnectionDlg;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.SqlServer.Management.UI.VSIntegration.Editors;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace __ExtensionName__.Ssms;

/// <summary>
/// Access to SSMS query windows: the active window's connection and text, opening new windows
/// and running them.
///
/// Everything here goes through SSMS internals (ServiceCache and IScriptFactory in
/// SqlPackageBase.dll and SqlWorkbench.Interfaces.dll). They have been stable for many
/// releases but are undocumented and unsupported, so every member treats "not available" as a
/// normal outcome. All members must be called on the UI thread.
/// </summary>
internal static class QueryWindow
{
    /// <summary>True when the active document is a SQL query window.</summary>
    public static bool IsActive
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ActiveDocumentView()?.GetType().Name.IndexOf("SqlScriptEditorControl", StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>
    /// The connection of the active query window, or null when there is no query window or it
    /// is disconnected. ServerName, UserName and AuthenticationType are the commonly used
    /// members; see <see cref="ActiveDatabase"/> for the database.
    /// </summary>
    public static UIConnectionInfo? ActiveConnection
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var info = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo?.UIConnectionInfo;
                return string.IsNullOrEmpty(info?.ServerName) ? null : info;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the active query window's connection.", ex);
                return null;
            }
        }
    }

    /// <summary>The database the active query window was connected to, or null for the login's default.</summary>
    public static string? ActiveDatabase
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var info = ActiveConnection;
            if (info is null)
            {
                return null;
            }

            var database = UIConnectionInfoUtil.GetDatabaseName(info);
            return string.IsNullOrEmpty(database) ? null : database;
        }
    }

    /// <summary>
    /// Creates a new, unopened connection with the same server, database and authentication as
    /// the active query window. The extension gets its own session: it never shares the query
    /// window's connection, so temp tables, transactions and SET options there are untouched.
    /// Open and use the result on a background thread, never on the UI thread.
    /// </summary>
    /// <returns>Null when there is no connected query window or it is not a Database Engine connection.</returns>
    public static DbConnection? CreateConnection()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var info = ActiveConnection;
        if (info is null || UIConnectionInfoUtil.GetCoreConnectionInfo(info) is not SqlConnectionInfo core)
        {
            return null;
        }

        var copy = (SqlConnectionInfo)core.Copy();
        var database = UIConnectionInfoUtil.GetDatabaseName(info);
        if (!string.IsNullOrEmpty(database))
        {
            copy.DatabaseName = database;
        }

        copy.ApplicationName = PackageIds.DisplayName;
        return copy.CreateConnectionObject() as DbConnection;
    }

    /// <summary>The selected text of the active document, or null when nothing is selected.</summary>
    public static string? SelectedText
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var text = (Dte()?.ActiveDocument?.Selection as TextSelection)?.Text;
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    /// <summary>The whole text of the active document, or null when it is not a text document.</summary>
    public static string? AllText
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Dte()?.ActiveDocument?.Object("TextDocument") is not TextDocument document)
            {
                return null;
            }

            return document.StartPoint.CreateEditPoint().GetText(document.EndPoint);
        }
    }

    /// <summary>Replaces the selection of the active document, or inserts at the caret when nothing is selected.</summary>
    public static bool Insert(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Dte()?.ActiveDocument?.Selection is not TextSelection selection)
        {
            return false;
        }

        selection.Insert(text, (int)vsInsertFlags.vsInsertFlagsContainNewText);
        return true;
    }

    /// <summary>
    /// Opens a new query window containing <paramref name="text"/>.
    /// With <paramref name="connection"/> the window is connected to it. Without, SSMS decides
    /// as it does for its own New Query button: the server selected in Object Explorer or the
    /// active query window's connection.
    /// </summary>
    public static bool Open(string text, UIConnectionInfo? connection = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var factory = ServiceCache.ScriptFactory;
        var dte = Dte();
        if (factory is null || dte is null)
        {
            return false;
        }

        if (connection is null)
        {
            factory.CreateNewBlankScript(ScriptType.Sql);
        }
        else
        {
            factory.CreateNewBlankScript(ScriptType.Sql, connection, null);
        }

        // The new window is now the active document.
        if (dte.ActiveDocument?.Selection is not TextSelection selection)
        {
            return false;
        }

        selection.Insert(text, (int)vsInsertFlags.vsInsertFlagsInsertAtStart);
        selection.StartOfDocument();
        return true;
    }

    /// <summary>Runs the active query window, exactly as pressing F5 does.</summary>
    public static void Execute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Dte()?.ExecuteCommand("Query.Execute");
    }

    /// <summary>Shows a short message in the SSMS status bar.</summary>
    public static void SetStatus(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
        {
            statusBar.SetText($"{PackageIds.DisplayName}: {message}");
        }
    }

    private static DTE? Dte()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return Package.GetGlobalService(typeof(SDTE)) as DTE;
    }

    /// <summary>
    /// The view object of the active document. This is the document SSMS's query commands act
    /// on, even while focus is in its results grid or in a tool window.
    /// </summary>
    private static object? ActiveDocumentView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection selection ||
            selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frameObject) != VSConstants.S_OK ||
            frameObject is not IVsWindowFrame frame ||
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var view) != VSConstants.S_OK)
        {
            return null;
        }

        return view;
    }
}

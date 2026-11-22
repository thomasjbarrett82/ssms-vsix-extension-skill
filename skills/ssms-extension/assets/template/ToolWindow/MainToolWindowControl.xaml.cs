using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using __ExtensionName__.Ssms;

namespace __ExtensionName__.ToolWindow;

/// <summary>Content of the tool window: shows the connection of the active query window.</summary>
public partial class MainToolWindowControl : UserControl
{
    public MainToolWindowControl()
    {
        InitializeComponent();
        Loaded += OnRefresh;
    }

    /// <summary>Runs when the window is first shown and when Refresh is clicked.</summary>
    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        try
        {
            // WPF event handlers run on the UI thread, where the SSMS services must be called.
            ThreadHelper.ThrowIfNotOnUIThread();

            var connection = QueryWindow.ActiveConnection;
            InfoText.Text = connection is null
                ? "No connected query window is active."
                : $"Server: {connection.ServerName}{Environment.NewLine}" +
                  $"Database: {QueryWindow.ActiveDatabase ?? "(default)"}{Environment.NewLine}" +
                  $"Login: {(string.IsNullOrEmpty(connection.UserName) ? "(integrated)" : connection.UserName)}";
        }
        catch (Exception ex)
        {
            Log.Error("Refreshing the tool window failed.", ex);
            InfoText.Text = ex.Message;
        }
    }
}

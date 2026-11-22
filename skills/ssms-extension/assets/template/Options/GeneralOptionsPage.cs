using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace __ExtensionName__.Options;

/// <summary>
/// Tools > Options > __DisplayName__ > General. Every public read/write property becomes a
/// row in a property grid and is saved by SSMS in its own settings store; no file handling is
/// needed. Supported property types are those with a string TypeConverter (bool, int, string,
/// enums, ...).
/// </summary>
[Guid("__OptionsPageGuid__")]
[ComVisible(true)]
public sealed class GeneralOptionsPage : DialogPage
{
    private int _topRowCount = 100;

    [Category("General")]
    [DisplayName("Rows for SELECT TOP")]
    [Description("How many rows generated SELECT TOP scripts ask for.")]
    [DefaultValue(100)]
    public int TopRowCount
    {
        get => _topRowCount;
        set => _topRowCount = value < 1 ? 1 : value;
    }

    /// <summary>The current settings. Call on the UI thread.</summary>
    public static GeneralOptionsPage Get(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return (GeneralOptionsPage)package.GetDialogPage(typeof(GeneralOptionsPage));
    }
}

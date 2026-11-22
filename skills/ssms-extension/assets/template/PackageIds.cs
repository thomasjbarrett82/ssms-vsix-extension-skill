using System;

namespace __ExtensionName__;

/// <summary>
/// GUIDs and command ids shared between code and the .vsct command table.
/// Every value here must match its symbol in __ExtensionName__Package.vsct.
/// </summary>
internal static class PackageIds
{
    public const string DisplayName = "__DisplayName__";
    public const string Description = "__Description__";

    public const string PackageGuidString = "__PackageGuid__";
    public const string CommandSetGuidString = "__CommandSetGuid__";
    public static readonly Guid CommandSet = new Guid(CommandSetGuidString);

    public const int AboutCommandId = 0x0100;
    // #if QueryEditor
    public const int OpenSelectionInNewQueryCommandId = 0x0101;
    // #endif
    // #if ToolWindow
    public const int ShowToolWindowCommandId = 0x0102;
    // #endif
    // #if Options
    public const int OptionsCommandId = 0x0103;
    // #endif
}

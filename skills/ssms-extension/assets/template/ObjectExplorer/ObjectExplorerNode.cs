using System;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;

namespace __ExtensionName__.ObjectExplorer;

/// <summary>
/// What an Object Explorer node stands for, read from SSMS's INodeInformation.
///
/// A node describes itself with two strings:
///   UrnPath - the kind of node, without names: "Server/Database/Table".
///   Context - the node itself, as an SMO URN:
///             "Server[@Name='SRV']/Database[@Name='Sales']/Table[@Name='Orders' and @Schema='dbo']".
/// Folder nodes ("Tables", "Views", ...) have a UrnPath that ends in the folder's own word, and
/// the Context of the object that contains them. The sample "Copy Node Info" menu item shows
/// the exact values for any node, which is the reliable way to learn them.
/// </summary>
internal sealed class ObjectExplorerNode
{
    private ObjectExplorerNode(INodeInformation node)
    {
        UrnPath = node.UrnPath ?? string.Empty;
        Context = node.Context ?? string.Empty;
        Name = node.Name ?? string.Empty;
        QualifiedName = node.InvariantName ?? Name;
        Server = node.Connection?.ServerName ?? string.Empty;

        var lastSlash = UrnPath.LastIndexOf('/');
        Kind = lastSlash < 0 ? UrnPath : UrnPath.Substring(lastSlash + 1);

        try
        {
            var urn = new Urn(Context);
            Database = urn.GetNameForType("Database") ?? string.Empty;

            // Only when the node is the object itself, not a folder below it.
            if (string.Equals(urn.Type, Kind, StringComparison.Ordinal))
            {
                Name = urn.GetAttribute("Name") ?? Name;
                Schema = urn.GetAttribute("Schema") ?? string.Empty;
            }
        }
        catch (Exception)
        {
            // Not every node has a well-formed URN; the values above are then all there is.
        }
    }

    /// <summary>Last segment of <see cref="UrnPath"/>: "Server", "Database", "Table", "View", "StoredProcedure", ...</summary>
    public string Kind { get; }

    public string UrnPath { get; }

    public string Context { get; }

    public string Server { get; }

    /// <summary>Empty for server-level nodes.</summary>
    public string Database { get; } = string.Empty;

    /// <summary>Empty for objects that have no schema.</summary>
    public string Schema { get; } = string.Empty;

    /// <summary>The object's name without schema.</summary>
    public string Name { get; }

    /// <summary>The name as SSMS qualifies it, for example "dbo.Orders".</summary>
    public string QualifiedName { get; }

    public bool IsTableOrView => Kind is "Table" or "View";

    /// <summary>[schema].[name], ready to be used in T-SQL.</summary>
    public string QuotedName => Schema.Length == 0 ? Quote(Name) : $"{Quote(Schema)}.{Quote(Name)}";

    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    public static ObjectExplorerNode? From(INodeInformation? node) => node is null ? null : new ObjectExplorerNode(node);

    public override string ToString() =>
        $"Kind:          {Kind}{Environment.NewLine}" +
        $"UrnPath:       {UrnPath}{Environment.NewLine}" +
        $"Context:       {Context}{Environment.NewLine}" +
        $"Server:        {Server}{Environment.NewLine}" +
        $"Database:      {Database}{Environment.NewLine}" +
        $"Schema:        {Schema}{Environment.NewLine}" +
        $"Name:          {Name}{Environment.NewLine}" +
        $"QualifiedName: {QualifiedName}";
}

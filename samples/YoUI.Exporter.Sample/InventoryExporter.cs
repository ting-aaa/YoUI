using System.Text;
using YoUI.Editor;

namespace YoUI.Exporter.Sample;

/// <summary>Example third-party exporter. No window or graphics dependencies and no file writes.</summary>
public sealed class InventoryExporter : IUiExporter
{
    public string Id => "sample.inventory";
    public string DisplayName => "Plugin · Component inventory";
    public IReadOnlyList<ExportFile> Export(ExportContext context)
    {
        static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        var text = new StringBuilder("# Component inventory\n\n| Name | Type | Command |\n| --- | --- | --- |\n");
        foreach (var node in context.Asset.Root.DescendantsAndSelf()) text.AppendLine($"| {Escape(node.Name)} | {node.Kind} | {Escape(node.Command)} |");
        return [new("inventory.md", text.ToString())];
    }
}

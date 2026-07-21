using System.Collections.ObjectModel;
using System.IO;

namespace MarkdownVault.Models;

/// <summary>Represents a file or directory node inside a vault.</summary>
public class VaultFile
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public VaultFile? Parent { get; set; }
    public List<VaultFile> Children { get; set; } = new();

    public string Extension => Path.GetExtension(FullPath);
    public bool IsMarkdown => Extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
    public bool IsHtml => Extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || Extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    public bool IsMermaid => Extension.Equals(".mermaid", StringComparison.OrdinalIgnoreCase) || Extension.Equals(".mmd", StringComparison.OrdinalIgnoreCase);


    public string RelativePath(string vaultRoot) =>
        Path.GetRelativePath(vaultRoot, FullPath).Replace('\\', '/');
}

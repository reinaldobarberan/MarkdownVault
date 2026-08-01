using System.IO;

namespace MarkdownVault.Services;

/// <summary>
/// Raíz compartida de datos de la aplicación en AppData. Único punto de verdad
/// para <c>%AppData%/MarkdownVault</c> — usado por <see cref="SettingsService"/>
/// y por el almacenamiento sandbox de plugins (<c>PluginData</c>).
/// </summary>
public static class AppPaths
{
    /// <summary>Ruta absoluta de <c>%AppData%/MarkdownVault</c>.</summary>
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarkdownVault");
}

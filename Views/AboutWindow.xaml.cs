using System.Linq;
using System.Reflection;
using System.Windows;

namespace MarkdownVault.Views;

/// <summary>
/// Ventana "Acerca de": autor, versión y sello de compilado.
/// Nada de esto está escrito a mano en el XAML. Todo se lee del ensamblado,
/// que a su vez lo recibe de Version.props en tiempo de compilación.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var assembly = Assembly.GetExecutingAssembly();

        VersionText.Text   = $"Versión {ResolveVersion(assembly)}";
        BuildText.Text     = ResolveBuildStamp(assembly) is { Length: > 0 } stamp
                                ? $"compilado el {stamp}"
                                : string.Empty;
        CopyrightText.Text = ResolveCopyright(assembly);
    }

    /// <summary>
    /// Versión semántica: la parte manual de Version.props (VersionPrefix).
    /// InformationalVersion llega como "1.0.0+build.20260818.1432" — y el SDK
    /// puede sumar todavía otro "+metadata" propio. Cortar en el PRIMER '+'
    /// deja siempre la versión limpia, sin importar cuántos sufijos haya.
    /// </summary>
    private static string ResolveVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>
    /// Fecha y hora en que se generó este ejecutable, ya formateada por
    /// Version.props. Se guarda como AssemblyMetadata con clave "BuildStamp"
    /// para no tener que parsear la versión: el formato se decide en un solo
    /// lugar, en la compilación.
    /// </summary>
    private static string ResolveBuildStamp(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildStamp")?
                .Value
        ?? string.Empty;

    private static string ResolveCopyright(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? string.Empty;
}

using System.Reflection;
using System.Runtime.Loader;

namespace MarkdownVault.Services.Plugins;

/// <summary>
/// Contexto de carga aislado para un plugin. Comparte el ensamblado del SDK con
/// el contexto por defecto para que la identidad de <c>IPlugin</c> coincida entre
/// host y plugin (si no, el cast a IPlugin daría null).
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    // Ensamblados que el host PROVEE y que deben compartirse con los plugins para
    // que la identidad de tipos coincida (si no, un cast a IPlugin / IMarkdownExtension
    // daría null). El SDK es el contrato; Markdig lo necesitan los plugins de sintaxis.
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "MarkdownVault.PluginSdk",
        "Markdig"
    };

    // isCollectible: true → permite descargar el DLL al desactivar el plugin
    // (Unload + GC). Requiere soltar TODA referencia a tipos del plugin antes.
    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Compartido → devolver null delega al contexto por defecto (donde el host
        // ya los tiene cargados).
        if (assemblyName.Name is not null && SharedAssemblies.Contains(assemblyName.Name))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}

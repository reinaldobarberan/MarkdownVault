using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Eisenhower;

/// <summary>
/// Envuelve <see cref="EisenhowerMarkdigExtension"/> sin filtrar el tipo de Markdig al
/// contrato del SDK (mismo patrón que <c>CalloutContribution</c>). Recibe
/// <see cref="IPluginStorage"/> por constructor para que el renderer pueda leer
/// tasks.json en tiempo de render.
/// </summary>
public sealed class EisenhowerContribution : IMarkdownContribution
{
    private readonly IPluginStorage _storage;

    public EisenhowerContribution(IPluginStorage storage)
    {
        _storage = storage;
    }

    public object CreateMarkdigExtension() => new EisenhowerMarkdigExtension(_storage);
}

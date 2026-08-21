using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Las listas editables (SDK 1.4.0) entran al <see cref="PluginRegistry"/> por el
/// MISMO camino que los comandos: etiquetadas con el id del dueño, invisibles
/// mientras el plugin está deshabilitado y soltadas enteras por
/// <see cref="PluginRegistry.RemoveByOwner"/>.
///
/// Eso último no es cosmético y por eso tiene prueba propia: <c>Load</c>,
/// <c>Save</c> y <c>Describe</c> son delegates que apuntan a código del ensamblado
/// del plugin. Si el registry se los quedara, el ALC no se podría descargar y este
/// contrato habría reintroducido justamente el problema que vino a evitar.
/// </summary>
public class PluginListSettingRegistryTests
{
    private static PluginListSetting Setting(string id) => new()
    {
        Id       = id,
        Title    = id,
        KeyLabel = "Término",
        Load     = () => Array.Empty<PluginListEntry>()
    };

    [Fact]
    public void ListSettings_are_hidden_until_the_owner_is_enabled()
    {
        var registry = new PluginRegistry();
        registry.AddListSetting("p1", Setting("uno"));

        Assert.Empty(registry.ListSettings);
        Assert.Empty(registry.ListSettingsFor("p1"));

        registry.SetEnabled("p1", true);

        Assert.Single(registry.ListSettings);
        Assert.Single(registry.ListSettingsFor("p1"));
    }

    [Fact]
    public void ListSettingsFor_only_answers_for_its_own_owner()
    {
        var registry = new PluginRegistry();
        registry.AddListSetting("p1", Setting("uno"));
        registry.AddListSetting("p2", Setting("dos"));
        registry.SetEnabledSet(new[] { "p1", "p2" });

        var mine = Assert.Single(registry.ListSettingsFor("p1"));

        Assert.Equal("uno", mine.Id);
        Assert.Equal(2, registry.ListSettings.Count);
    }

    [Fact]
    public void ListSettingsFor_ignores_case_in_the_plugin_id()
    {
        var registry = new PluginRegistry();
        registry.AddListSetting("core.dictado-voz", Setting("glosario"));
        registry.SetEnabled("core.dictado-voz", true);

        Assert.Single(registry.ListSettingsFor("CORE.DICTADO-VOZ"));
    }

    [Fact]
    public void Disabling_hides_the_lists_without_forgetting_them()
    {
        var registry = new PluginRegistry();
        registry.AddListSetting("p1", Setting("uno"));
        registry.SetEnabled("p1", true);

        registry.SetEnabled("p1", false);
        Assert.Empty(registry.ListSettingsFor("p1"));

        registry.SetEnabled("p1", true);
        Assert.Single(registry.ListSettingsFor("p1"));
    }

    [Fact]
    public void RemoveByOwner_drops_the_delegates_for_good()
    {
        var registry = new PluginRegistry();
        registry.AddListSetting("p1", Setting("uno"));
        registry.AddListSetting("p2", Setting("dos"));
        registry.SetEnabledSet(new[] { "p1", "p2" });

        registry.RemoveByOwner("p1");

        // Re-habilitar no las resucita: se fueron de la lista, no quedaron ocultas.
        registry.SetEnabled("p1", true);
        Assert.Empty(registry.ListSettingsFor("p1"));
        Assert.Single(registry.ListSettingsFor("p2"));
    }

    [Fact]
    public void Clear_empties_the_list_settings_too()
    {
        var registry = new PluginRegistry();
        registry.AddListSetting("p1", Setting("uno"));
        registry.SetEnabled("p1", true);

        registry.Clear();
        registry.SetEnabled("p1", true);

        Assert.Empty(registry.ListSettings);
    }
}

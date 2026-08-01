using System;
using System.IO;
using System.Windows;

namespace MarkdownVault.Plugin.TestFixture;

/// <summary>
/// Fixture para el test de caracterización del pin WPF/ALC (limitación ACEPTADA para v1
/// — ver <c>docs/plugins/GUIA-PLUGINS.md</c> §9 "Plugins con UI WPF y descarga en
/// caliente"). NO implementa <c>IPlugin</c> ni se descubre vía <c>PluginManager</c>/
/// <c>plugin.json</c> — se carga directo por ruta de DLL desde
/// <see cref="MarkdownVault.Services.Plugins.PluginManager.LoadUnloadWithWorkForTest"/>,
/// que invoca <see cref="DoWork"/> por reflexión.
/// </summary>
public static class WpfFixturePlugin
{
    /// <summary>
    /// Abre/cierra una <see cref="Window"/> WPF real (el tipo BASE
    /// <c>System.Windows.Window</c>, NO un subtipo definido en este ensamblado) y
    /// adquiere/libera un <see cref="FileStream"/>, en un hilo STA dedicado (xUnit
    /// corre en MTA). Verificado empíricamente: por sí solo, EN AISLAMIENTO, esto
    /// NUNCA fija (pin) el ALC collectible — usar el tipo base <c>Window</c> sin
    /// subclasificarlo no dispara el registro de <c>DependencyObjectType</c> para un
    /// tipo NUEVO definido dentro de este ALC.
    ///
    /// El test
    /// <c>PluginActivationIntegrationTests.Wpf_window_plugin_pins_its_ALC_documented_v1_limitation</c>
    /// usa este método para demostrar la CONTAMINACIÓN CRUZADA: primero activa el
    /// plugin REAL Eisenhower (que sí define su propio tipo <c>Window</c>-derivado,
    /// <c>CaptureModal</c>, realizado vía <c>Assembly.GetTypes()</c> dentro de
    /// <c>PluginManager.Activate</c>) y RECIÉN DESPUÉS ejercita este fixture — sólo en
    /// ese orden el unload de ESTE ALC (que ni siquiera define un tipo WPF propio)
    /// también queda clavado, porque WPF corrompe sus cachés estáticas de proceso para
    /// el resto de la vida del proceso, no sólo para el tipo/ALC que las disparó.
    /// </summary>
    public static void DoWork()
    {
        var window = new Window
        {
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Width = 1,
            Height = 1,
            Left = -2000,
            Top = -2000
        };
        window.Show();
        window.Close();

        var tempFile = Path.Combine(Path.GetTempPath(), $"mvfixture_{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = File.Create(tempFile))
                fs.WriteByte(1);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }
}

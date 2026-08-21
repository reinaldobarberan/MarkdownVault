using MarkdownVault.Models;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// LA invariante del cierre: si hay algo que perder se pregunta, y la pregunta dice CUÁNTOS y
/// CUÁLES documentos — no un "hay cambios sin guardar" genérico, con el que el usuario no puede
/// decidir nada y termina apretando cualquier botón.
///
/// Solo la decisión (<see cref="UnsavedChangesReport"/>). El diálogo y el ciclo de vida de la
/// ventana NO se cubren a propósito: son la superficie, no el riesgo.
/// </summary>
public class UnsavedChangesReportTests
{
    private static OpenTab Tab(string path, bool dirty) =>
        new(path) { Content = "x", IsDirty = dirty };

    private static IReadOnlyList<IReadOnlyList<OpenTab>> Panes(params OpenTab[][] panes) =>
        panes.Select(p => (IReadOnlyList<OpenTab>)p).ToList();

    [Fact]
    public void SinPestañasSucias_NoSePregunta()
    {
        var report = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\a.md", dirty: false), Tab(@"C:\v\b.md", dirty: false)]),
            busyOperationTitle: null);

        Assert.False(report.ShouldPrompt);
        Assert.Equal(0, report.Count);
    }

    [Fact]
    public void ConOperaciónEnCursoPeroNadaSucio_TampocoSePregunta()
    {
        // Interrumpir un cierre sin nada que perder entrena al usuario a despachar el diálogo
        // sin leerlo, y ahí el diálogo deja de proteger de nada.
        var report = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\a.md", dirty: false)]),
            busyOperationTitle: "Transcripción de archivo");

        Assert.False(report.ShouldPrompt);
    }

    [Fact]
    public void UnSoloDocumento_TitularEnSingularYSinEtiquetaDePanel()
    {
        var report = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\activa.md", dirty: true), Tab(@"C:\v\limpia.md", dirty: false)]),
            busyOperationTitle: null);

        Assert.True(report.ShouldPrompt);
        Assert.Equal(1, report.Count);
        Assert.Equal("Hay 1 documento con cambios sin guardar:", report.Headline);
        Assert.Equal(new[] { "activa.md" }, report.Lines);
        Assert.False(report.ShowPaneLabels);
    }

    [Fact]
    public void PestañaDeSegundoPlanoSucia_TambiénSeCuenta()
    {
        // El caso que el hueco volvió alcanzable: el dictado llenó una pestaña de atrás.
        var activa  = Tab(@"C:\v\activa.md",  dirty: false);
        var dictado = Tab(@"C:\v\dictado.md", dirty: true);

        var report = UnsavedChangesReport.Build(Panes([activa, dictado]), busyOperationTitle: null);

        Assert.True(report.ShouldPrompt);
        Assert.Equal(new[] { "dictado.md" }, report.Lines);
    }

    [Fact]
    public void VariasEnDosPaneles_NombraCadaUnaConSuPanel()
    {
        var report = UnsavedChangesReport.Build(
            Panes(
                [Tab(@"C:\v\uno.md", dirty: true), Tab(@"C:\v\dos.md", dirty: true), Tab(@"C:\v\tres.md", dirty: false)],
                [Tab(@"C:\v\cuatro.md", dirty: false), Tab(@"C:\v\cinco.md", dirty: true)]),
            busyOperationTitle: null);

        Assert.Equal(3, report.Count);
        Assert.Equal("Hay 3 documentos con cambios sin guardar:", report.Headline);
        Assert.True(report.ShowPaneLabels);
        Assert.Equal(
            new[] { "uno.md  (panel 1)", "dos.md  (panel 1)", "cinco.md  (panel 2)" },
            report.Lines);
    }

    [Fact]
    public void PestañaSuciaSinRutaEnDisco_SeCuenta_SeNombraYSeMarca()
    {
        // El guardado automático NO puede salvarla (no hay archivo que actualizar), así que es
        // justamente la que más falta hace nombrar. OpenTab.FileName da cadena vacía sin ruta:
        // sin el nombre de reemplazo la línea saldría en blanco.
        var report = UnsavedChangesReport.Build(
            Panes([Tab(string.Empty, dirty: true), Tab(@"C:\v\otra.md", dirty: true)]),
            busyOperationTitle: null);

        Assert.Equal(2, report.Count);
        Assert.True(report.HasNeverSaved);
        Assert.Equal("(documento sin título)  — nunca se guardó en disco", report.Lines[0]);
        Assert.Equal("otra.md", report.Lines[1]);
        Assert.NotNull(report.NeverSavedWarning);
    }

    [Fact]
    public void TodasConRuta_NoHayAvisoDeNuncaGuardado()
    {
        var report = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\a.md", dirty: true)]), busyOperationTitle: null);

        Assert.False(report.HasNeverSaved);
        Assert.Null(report.NeverSavedWarning);
    }

    [Fact]
    public void OperaciónEnCursoConPestañasSucias_SeAvisaYSeNombra()
    {
        var report = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\dictado.md", dirty: true)]),
            busyOperationTitle: "Dictado en vivo");

        Assert.NotNull(report.BusyWarning);
        Assert.Contains("Dictado en vivo", report.BusyWarning!);
    }

    [Fact]
    public void SinOperaciónEnCurso_NoSeInventaNingúnAviso()
    {
        // Un plugin que trabaje sin abrir scope de progreso es INVISIBLE para el host. Acá no se
        // simula saberlo: el aviso solo aparece cuando la barra de progreso lo reporta de verdad.
        var report = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\a.md", dirty: true)]), busyOperationTitle: null);
        Assert.Null(report.BusyWarning);

        var enBlanco = UnsavedChangesReport.Build(
            Panes([Tab(@"C:\v\a.md", dirty: true)]), busyOperationTitle: "   ");
        Assert.Null(enBlanco.BusyWarning);
    }
}

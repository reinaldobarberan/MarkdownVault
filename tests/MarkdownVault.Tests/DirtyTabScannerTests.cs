using MarkdownVault.Models;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// LA invariante del guardado automático: se persiste TODA pestaña sucia, y el texto de cada una
/// sale de la fuente correcta.
///
/// Por qué esto lleva test aunque en este proyecto los tests van al final: el modo de falla es
/// PÉRDIDA DE DATOS silenciosa por los dos lados. Si se salta una pestaña de segundo plano, un
/// dictado largo nunca toca el disco. Y si se toma la fuente equivocada para la pestaña activa,
/// no es que "no guarda": guarda el modelo RANCIO encima del texto bueno, que es peor.
///
/// Solo se cubre la decisión (<see cref="DirtyTabScanner"/>). Ni el temporizador, ni el I/O, ni
/// el diálogo — eso es infraestructura, no es donde está el riesgo.
/// </summary>
public class DirtyTabScannerTests
{
    private static OpenTab Tab(string path, string content, bool dirty) =>
        new(path) { Content = content, IsDirty = dirty };

    [Fact]
    public void SinPestañasSucias_NoHayNadaQueGuardar()
    {
        var activa = Tab(@"C:\v\a.md", "hola", dirty: false);
        var fondo  = Tab(@"C:\v\b.md", "chau", dirty: false);

        var pendientes = DirtyTabScanner.Scan([activa, fondo], activa, "hola");

        Assert.Empty(pendientes);
    }

    [Fact]
    public void PestañaActiva_UsaElTextoVivoDelPanel_NoSuOpenTabContent()
    {
        // El escenario que rompe todo si se elige mal la fuente: el modelo quedó atrás respecto
        // del control de AvalonEdit. Guardar `tab.Content` acá sería pisar lo escrito con lo viejo.
        var activa = Tab(@"C:\v\activa.md", "TEXTO VIEJO DEL MODELO", dirty: true);

        var pendientes = DirtyTabScanner.Scan([activa], activa, "texto vivo del control");

        var unica = Assert.Single(pendientes);
        Assert.Equal(TabContentSource.LiveEditor, unica.Source);
        Assert.Equal("texto vivo del control", unica.Content);
        Assert.Same(activa, unica.Tab);
    }

    [Fact]
    public void PestañaDeSegundoPlanoSucia_SeGuardaDesdeSuPropioModelo()
    {
        // EL hueco: antes esta pestaña no se guardaba nunca. Es la que llena el dictado.
        var activa  = Tab(@"C:\v\activa.md",  "sin cambios",                 dirty: false);
        var dictado = Tab(@"C:\v\dictado.md", "frase 1. frase 2. frase 3.",  dirty: true);

        var pendientes = DirtyTabScanner.Scan([activa, dictado], activa, "sin cambios");

        var unica = Assert.Single(pendientes);
        Assert.Same(dictado, unica.Tab);
        Assert.Equal(TabContentSource.TabModel, unica.Source);
        Assert.Equal("frase 1. frase 2. frase 3.", unica.Content);
    }

    [Fact]
    public void ActivaYDeFondoSucias_CadaUnaConSuFuente()
    {
        var activa = Tab(@"C:\v\uno.md", "modelo rancio", dirty: true);
        var fondo  = Tab(@"C:\v\dos.md", "dictado",       dirty: true);
        var limpia = Tab(@"C:\v\tres.md", "intacta",      dirty: false);

        var pendientes = DirtyTabScanner.Scan([activa, fondo, limpia], activa, "texto vivo");

        Assert.Equal(2, pendientes.Count);
        Assert.Equal("texto vivo", pendientes[0].Content);
        Assert.Equal(TabContentSource.LiveEditor, pendientes[0].Source);
        Assert.Equal("dictado", pendientes[1].Content);
        Assert.Equal(TabContentSource.TabModel, pendientes[1].Source);
    }

    [Fact]
    public void PestañaSuciaSinRutaEnDisco_SeSalta_PorqueNoHayADóndeEscribir()
    {
        // No se pierde de vista: UnsavedChangesReport SÍ la cuenta al cerrar, para que el usuario
        // se entere en vez de que el guardado automático la ignore en silencio para siempre.
        var sinRuta = Tab(string.Empty,   "borrador", dirty: true);
        var conRuta = Tab(@"C:\v\ok.md",  "texto",    dirty: true);

        var pendientes = DirtyTabScanner.Scan([sinRuta, conRuta], sinRuta, "borrador vivo");

        var unica = Assert.Single(pendientes);
        Assert.Same(conRuta, unica.Tab);
    }

    [Fact]
    public void CanClearDirty_SoloSiElTextoNoCambióMientrasSeEscribía()
    {
        // Si el dictado agregó otra frase entre el await y el retorno, bajar la bandera dejaría
        // ese texto marcado como guardado sin estarlo: ni lo reescribe el guardado automático ni
        // lo nombra el aviso de cierre. Se pierde en silencio.
        Assert.True(DirtyTabScanner.CanClearDirty("frase 1.", "frase 1."));
        Assert.False(DirtyTabScanner.CanClearDirty("frase 1. frase 2.", "frase 1."));
        Assert.False(DirtyTabScanner.CanClearDirty("Frase 1.", "frase 1."));   // ordinal, no cultural
    }

    [Fact]
    public void SinPestañaActiva_TodasSalenDeSuModelo()
    {
        // Un panel puede quedarse sin pestaña activa (se movió la última al otro panel).
        var a = Tab(@"C:\v\a.md", "aaa", dirty: true);
        var b = Tab(@"C:\v\b.md", "bbb", dirty: true);

        var pendientes = DirtyTabScanner.Scan([a, b], activeTab: null, liveContent: "irrelevante");

        Assert.Equal(2, pendientes.Count);
        Assert.All(pendientes, p => Assert.Equal(TabContentSource.TabModel, p.Source));
        Assert.Equal(new[] { "aaa", "bbb" }, pendientes.Select(p => p.Content));
    }
}

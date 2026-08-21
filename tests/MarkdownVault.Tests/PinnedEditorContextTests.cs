using System.IO;
using MarkdownVault.Models;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;
using MarkdownVault.ViewModels;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// LA invariante de <see cref="PinnedEditorContext"/>: el texto que escribe un plugin va
/// SIEMPRE al documento donde la acción EMPEZÓ, no al que esté activo cuando la escritura
/// finalmente llega.
///
/// Por qué esto sí lleva test aunque en este proyecto los tests van al final: el modo de
/// falla es corrupción SILENCIOSA. Hay un solo control de edición por panel y cambiar de
/// pestaña le reemplaza el contenido; un plugin que retiene el <see cref="IEditorContext"/>
/// y escribe más tarde (dictado en vivo: una frase por pausa — transcripción de archivo:
/// minutos después) insertaba en el documento equivocado sin error, sin excepción y sin
/// aviso. Si esto regresiona nadie se entera hasta perder contenido. El resto del editor NO
/// se cubre acá a propósito: solo la decisión de enrutado y el manejo del caret.
///
/// Los tests trabajan sobre la capa VM sin Views (misma técnica que
/// <see cref="WorkbenchInvariantTests"/>). Consecuencia a tener presente: sin
/// <c>EditorView</c> nadie guarda el caret en <see cref="OpenTab.CaretOffset"/> al cambiar
/// de pestaña, así que los tests lo fijan a mano donde importa — es exactamente el estado
/// que la vista habría dejado.
/// </summary>
public class PinnedEditorContextTests : IDisposable
{
    private readonly string          _root;
    private readonly FileService     _fileService = new();
    private readonly PluginRegistry  _registry    = new();
    private readonly MarkdownService _markdownService;
    private readonly FakeDialogService _dialogService = new();

    public PinnedEditorContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mvpin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _markdownService = new MarkdownService(_registry);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ─── Andamiaje ───────────────────────────────────────────────────────────

    private EditorGroupViewModel NewGroup() =>
        new(_fileService, _markdownService, _registry, _dialogService, uiDispatch: a => a());

    private MainViewModel NewWorkbench() =>
        new(_fileService, _markdownService,
            new SettingsService(Path.Combine(_root, "settings.json")),
            _registry, _dialogService, uiDispatch: a => a());

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Captura las tres señales del camino VIVO (las que consume EditorView).</summary>
    private sealed class LiveEditorSpy
    {
        public readonly List<string>                 Snippets  = new();
        public readonly List<string>                 Replaces  = new();
        public readonly List<(string Pre, string Suf)> Wraps   = new();

        public LiveEditorSpy(EditorGroupViewModel group)
        {
            group.SnippetRequested          += t      => Snippets.Add(t);
            group.ReplaceSelectionRequested += t      => Replaces.Add(t);
            group.InsertionRequested        += (p, s) => Wraps.Add((p, s));
        }

        public int Total => Snippets.Count + Replaces.Count + Wraps.Count;
    }

    private static PinnedEditorContext Pin(EditorGroupViewModel group) =>
        (PinnedEditorContext)group.CreatePluginEditorContext();

    // ─── 1. Pestaña fijada SIGUE activa → control vivo ───────────────────────

    [Fact]
    public async Task PinnedTabStillActive_RoutesToLiveEditor()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("a.md", "hola"));
        var spy = new LiveEditorSpy(group);

        var ctx = Pin(group);
        Assert.Equal(PluginWriteRoute.LiveEditor, ctx.ResolveRoute(out var target));
        Assert.Same(group, target);

        ctx.ReplaceSelection("dictado");

        // El camino vivo delega en la vista: se emite el evento y NO se toca el modelo.
        Assert.Equal(new[] { "dictado" }, spy.Replaces);
        Assert.Equal("hola", group.ActiveTab!.Content);
    }

    /// <summary>
    /// Callouts, Highlight, CopyButton, Mermaid y Eisenhower insertan dentro del mismo clic:
    /// la pestaña fijada ES la activa, así que las tres operaciones tienen que seguir saliendo
    /// por el camino vivo, idénticas a antes del arreglo.
    /// </summary>
    [Fact]
    public async Task SynchronousPlugins_AllThreeOperations_StayOnTheLivePath()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("sync.md", "texto"));
        var spy = new LiveEditorSpy(group);

        var ctx = Pin(group);
        ctx.InsertAtCaret("snippet");
        ctx.ReplaceSelection("reemplazo");
        ctx.WrapSelection("**", "**");

        Assert.Equal(new[] { "snippet" },   spy.Snippets);
        Assert.Equal(new[] { "reemplazo" }, spy.Replaces);
        Assert.Equal(("**", "**"),          Assert.Single(spy.Wraps));
        Assert.Equal("texto", group.ActiveTab!.Content);   // el modelo lo toca la vista, no el contexto
    }

    // ─── 2. Pestaña fijada YA NO activa → modelo de esa pestaña ──────────────

    [Fact]
    public async Task UserSwitchedTab_WritesIntoThePinnedTab_NotTheActiveOne()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("origen.md", "uno dos"));
        var origen = group.ActiveTab!;
        origen.CaretOffset = origen.Content.Length;   // lo que habría guardado la vista

        var ctx = Pin(group);   // el usuario aprieta "Iniciar dictado" acá

        await group.OpenFileAsync(WriteFile("otro.md", "NO TOCAR"));
        var otro = group.ActiveTab!;
        var spy  = new LiveEditorSpy(group);

        Assert.Equal(PluginWriteRoute.PinnedTabModel, ctx.ResolveRoute(out _));
        ctx.ReplaceSelection(" tres");

        Assert.Equal("uno dos tres", origen.Content);
        Assert.Equal("NO TOCAR", otro.Content);      // el documento activo queda intacto
        Assert.Equal(0, spy.Total);                  // y no se tocó el control vivo
    }

    [Fact]
    public async Task InactiveWrite_InsertsAtTheSavedCaret_NotAtTheEnd()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("caret.md", "AB CD"));
        var pinned = group.ActiveTab!;
        pinned.CaretOffset = 2;                       // justo después de "AB"

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("otra.md", ""));

        ctx.ReplaceSelection("-X-");

        Assert.Equal("AB-X- CD", pinned.Content);
        Assert.Equal(5, pinned.CaretOffset);          // 2 + len("-X-")
    }

    // ─── 3. Caret: varias frases seguidas se encadenan, no se pisan ──────────

    [Fact]
    public async Task ConsecutiveInactiveWrites_AdvanceTheCaret_InsteadOfOverwriting()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("dictado.md", ""));
        var pinned = group.ActiveTab!;

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("distraccion.md", "otra cosa"));

        // Así dicta DictationSession: una llamada por frase, con espacio al final.
        ctx.ReplaceSelection("primera ");
        ctx.ReplaceSelection("segunda ");
        ctx.ReplaceSelection("tercera ");

        Assert.Equal("primera segunda tercera ", pinned.Content);
        Assert.Equal(pinned.Content.Length, pinned.CaretOffset);
    }

    /// <summary>
    /// El caret guardado puede haber quedado más allá del texto (recarga desde disco de un
    /// archivo que se acortó afuera). Tiene que acotarse, no explotar.
    /// </summary>
    [Fact]
    public async Task InactiveWrite_WithStaleCaretBeyondEnd_ClampsInsteadOfThrowing()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("corto.md", "abc"));
        var pinned = group.ActiveTab!;
        pinned.CaretOffset = 999;

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("libre.md", ""));

        ctx.ReplaceSelection("!");

        Assert.Equal("abc!", pinned.Content);
        Assert.Equal(4, pinned.CaretOffset);
    }

    /// <summary>
    /// <see cref="IEditorContext.InsertAtCaret"/> abre línea nueva si el caret no está al
    /// principio de una — mismo criterio que <c>EditorView.Vm_SnippetRequested</c>, para que
    /// el texto resultante no dependa de por qué camino salió.
    /// </summary>
    [Theory]
    [InlineData("linea", 5, "linea\nX")]     // caret a mitad de línea → salto previo
    [InlineData("linea\n", 6, "linea\nX")]   // caret al principio de línea → sin salto
    [InlineData("", 0, "X")]                 // documento vacío → sin salto
    public async Task InsertAtCaret_OnInactiveTab_MatchesTheLiveFreshLineRule(
        string initial, int caret, string expected)
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("fresh.md", initial));
        var pinned = group.ActiveTab!;
        pinned.CaretOffset = caret;

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("aparte.md", ""));

        ctx.InsertAtCaret("X");

        Assert.Equal(expected, pinned.Content);
    }

    // ─── 4. Documento sucio ──────────────────────────────────────────────────

    [Fact]
    public async Task InactiveWrite_MarksThePinnedTabDirty()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("sucio.md", "base"));
        var pinned = group.ActiveTab!;
        Assert.False(pinned.IsDirty);

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("limpia.md", ""));
        var activa = group.ActiveTab!;

        ctx.ReplaceSelection(" mas");

        // Sin esto, CloseTab no pregunta nada y el usuario pierde el texto al cerrar.
        Assert.True(pinned.IsDirty);
        Assert.Contains("•", pinned.DisplayName);
        Assert.False(activa.IsDirty);   // la pestaña que el usuario está mirando no se ensucia
    }

    // ─── 5. Pestaña cerrada durante la operación ─────────────────────────────

    [Fact]
    public async Task PinnedTabClosedMidOperation_DegradesWithAMessage_AndWritesNowhere()
    {
        var group    = NewGroup();
        var mensajes = new List<string>();
        group.StatusSink = mensajes.Add;

        await group.OpenFileAsync(WriteFile("cerrada.md", "contenido"));
        var pinned = group.ActiveTab!;
        var ctx    = Pin(group);

        await group.OpenFileAsync(WriteFile("sobreviviente.md", "INTACTO"));
        var superviviente = group.ActiveTab!;
        group.CloseTabCommand.Execute(pinned);   // limpia → se cierra sin preguntar

        var spy = new LiveEditorSpy(group);
        Assert.Equal(PluginWriteRoute.TabClosed, ctx.ResolveRoute(out _));

        var ex = Record.Exception(() => ctx.ReplaceSelection("perdido"));

        Assert.Null(ex);                                   // nunca una excepción
        Assert.Equal("contenido", pinned.Content);         // no se escribe en la cerrada
        Assert.Equal("INTACTO", superviviente.Content);    // ni, sobre todo, en otra
        Assert.Equal(0, spy.Total);
        Assert.Contains(mensajes, m => m.Contains("cerrada.md") && m.Contains("se cerró"));
    }

    /// <summary>
    /// El dictado en vivo escribe una vez por frase: si la pestaña se cerró, avisar en cada
    /// una taparía la barra de estado. Se avisa UNA vez y el resto se descarta en silencio.
    /// </summary>
    [Fact]
    public async Task ClosedTab_WarnsOnlyOncePerInvocation()
    {
        var group    = NewGroup();
        var mensajes = new List<string>();
        group.StatusSink = mensajes.Add;

        await group.OpenFileAsync(WriteFile("efimera.md", ""));
        var pinned = group.ActiveTab!;
        var ctx    = Pin(group);

        await group.OpenFileAsync(WriteFile("resto.md", ""));
        group.CloseTabCommand.Execute(pinned);

        ctx.ReplaceSelection("una ");
        ctx.ReplaceSelection("dos ");
        ctx.InsertAtCaret("tres");

        Assert.Single(mensajes);
    }

    /// <summary>
    /// Ejecutar un comando sin ningún documento abierto y que se abra uno DESPUÉS es el mismo
    /// riesgo con otra cara: la escritura diferida no tiene destino legítimo, así que degrada
    /// en vez de caer en la pestaña recién abierta.
    /// </summary>
    [Fact]
    public async Task NoDocumentWhenCommandRan_DoesNotWriteIntoATabOpenedLater()
    {
        var group    = NewGroup();
        var mensajes = new List<string>();
        group.StatusSink = mensajes.Add;

        var ctx = Pin(group);                    // sin pestañas: nada que fijar
        // Sin pestaña NO hay destino, ni siquiera mientras el panel siga vacío: escribir en el
        // control sin OpenTab detrás no lo persistía nadie y la primera apertura lo pisaba.
        Assert.Equal(PluginWriteRoute.NoDocument, ctx.ResolveRoute(out _));

        await group.OpenFileAsync(WriteFile("posterior.md", "AJENO"));
        var posterior = group.ActiveTab!;
        var spy = new LiveEditorSpy(group);

        Assert.Equal(PluginWriteRoute.NoDocument, ctx.ResolveRoute(out _));
        ctx.ReplaceSelection("intruso");

        Assert.Equal("AJENO", posterior.Content);
        Assert.Equal(0, spy.Total);
        Assert.Single(mensajes);
    }

    // ─── 6. Selección: solo existe en el control vivo ────────────────────────

    [Fact]
    public async Task SelectedText_IsLiveWhenPinnedTabIsActive_AndEmptyOnceItIsNot()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("sel.md", "hola mundo"));
        group.SelectedTextProvider = () => "mundo";

        var ctx = Pin(group);
        Assert.Equal("mundo", ctx.SelectedText);

        await group.OpenFileAsync(WriteFile("sel2.md", ""));

        // No hay selección que devolver: OpenTab guarda caret y scroll, no selección.
        Assert.Equal(string.Empty, ctx.SelectedText);
    }

    [Fact]
    public async Task WrapSelection_OnInactiveTab_DegradesToInsertionAtTheCaret()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("wrap.md", "abcd"));
        var pinned = group.ActiveTab!;
        pinned.CaretOffset = 2;

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("wrap2.md", ""));

        ctx.WrapSelection("**", "**");

        Assert.Equal("ab****cd", pinned.Content);
        Assert.Equal(4, pinned.CaretOffset);   // el caret queda ENTRE los marcadores
        Assert.True(pinned.IsDirty);
    }

    /// <summary>Prefijo de línea ("# "): la vista lo mete al principio de la línea, no en el caret.</summary>
    [Fact]
    public async Task WrapSelection_LinePrefix_OnInactiveTab_LandsAtTheStartOfTheLine()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("head.md", "uno\ndos"));
        var pinned = group.ActiveTab!;
        pinned.CaretOffset = 6;                 // dentro de "dos"

        var ctx = Pin(group);
        await group.OpenFileAsync(WriteFile("head2.md", ""));

        ctx.WrapSelection("## ", "");

        Assert.Equal("uno\n## dos", pinned.Content);
    }

    // ─── 7. Content lee la pestaña FIJADA, no la activa ──────────────────────

    [Fact]
    public async Task Content_ReadsThePinnedTab_EvenAfterTheUserSwitchedAway()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("leido.md", "TEXTO FIJADO"));
        var ctx = Pin(group);

        await group.OpenFileAsync(WriteFile("distinto.md", "OTRO TEXTO"));

        Assert.Equal("TEXTO FIJADO", ctx.Content);
        Assert.Equal("OTRO TEXTO", group.Content);
    }

    // ─── 8. Panel dividido ───────────────────────────────────────────────────

    [Fact]
    public async Task SplitPanes_PinnedTabInPaneA_IsUnaffectedByPaneB()
    {
        var vm = NewWorkbench();
        var paneA = vm.Groups[0];
        await paneA.OpenFileAsync(WriteFile("A.md", "A"));
        var pinned = paneA.ActiveTab!;
        pinned.CaretOffset = 1;

        var ctx = Pin(paneA);

        vm.EnterSplit();
        var paneB = vm.Groups[1];
        await paneB.OpenFileAsync(WriteFile("B.md", "B"));
        var tabB = paneB.ActiveTab!;

        // Pane A sigue con su pestaña fijada activa → camino vivo, en el panel A.
        Assert.Equal(PluginWriteRoute.LiveEditor, ctx.ResolveRoute(out var target));
        Assert.Same(paneA, target);

        // Ahora el usuario cambia de pestaña en el panel A: se pasa al modelo de la fijada.
        await paneA.OpenFileAsync(WriteFile("A2.md", "A2"));
        Assert.Equal(PluginWriteRoute.PinnedTabModel, ctx.ResolveRoute(out _));

        ctx.ReplaceSelection("!");

        Assert.Equal("A!", pinned.Content);
        Assert.Equal("B",  tabB.Content);       // el otro panel ni se entera
    }

    /// <summary>
    /// «Mover al otro panel» MIGRA la instancia de <see cref="OpenTab"/>: la pestaña no se
    /// cerró, cambió de dueño. La escritura tiene que seguir a la PESTAÑA (y salir por el
    /// panel que ahora la tiene), no darla por cerrada.
    /// </summary>
    [Fact]
    public async Task TabMovedToTheOtherPane_FollowsTheTab_NotTheOriginalPane()
    {
        var vm = NewWorkbench();
        var paneA = vm.Groups[0];
        await paneA.OpenFileAsync(WriteFile("viajera.md", "texto"));
        var pinned = paneA.ActiveTab!;

        var ctx = Pin(paneA);

        vm.EnterSplit();
        var paneB = vm.Groups[1];
        vm.MoveTabToOtherGroup(pinned);

        Assert.Same(pinned, paneB.ActiveTab);
        Assert.DoesNotContain(pinned, paneA.OpenTabs);

        var spyA = new LiveEditorSpy(paneA);
        var spyB = new LiveEditorSpy(paneB);

        Assert.Equal(PluginWriteRoute.LiveEditor, ctx.ResolveRoute(out var target));
        Assert.Same(paneB, target);

        ctx.ReplaceSelection("dictado");

        Assert.Equal(new[] { "dictado" }, spyB.Replaces);
        Assert.Equal(0, spyA.Total);
    }

    // ─── 9. Un contexto POR INVOCACIÓN (guardia de regresión del bug) ────────

    /// <summary>
    /// La causa raíz era un único <c>EditorContextAdapter</c> cacheado por panel y repartido a
    /// todos los items de la barra: cada invocación heredaba el estado de la anterior y ninguna
    /// sabía en qué documento había arrancado. Este test es la guardia directa contra eso.
    /// </summary>
    [Fact]
    public async Task EachCommandInvocation_GetsItsOwnContext_PinnedToTheTabActiveAtThatMoment()
    {
        var group = NewGroup();
        await group.OpenFileAsync(WriteFile("primera.md", ""));
        var primera = group.ActiveTab!;

        var capturados = new List<IEditorContext>();
        var item = PluginToolbarItemViewModel.Single(
            new PluginCommand { Id = "t.cmd", Title = "T", Execute = capturados.Add },
            group.CreatePluginEditorContext);

        item.Command!.Execute(null);              // clic 1, sobre "primera"

        await group.OpenFileAsync(WriteFile("segunda.md", ""));
        var segunda = group.ActiveTab!;

        item.Command!.Execute(null);              // clic 2, sobre "segunda"

        Assert.Equal(2, capturados.Count);
        Assert.NotSame(capturados[0], capturados[1]);
        Assert.Same(primera, ((PinnedEditorContext)capturados[0]).PinnedTab);
        Assert.Same(segunda, ((PinnedEditorContext)capturados[1]).PinnedTab);
    }
}

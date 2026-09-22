using MarkdownVault.Models;
using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Cubre la política de posición de lectura del editor: <see cref="EditorScrollAnchor"/>,
/// <see cref="ScrollSettle"/> y <see cref="EditorScrollPolicy"/>. Es lógica PURA —recibe
/// números del árbol de alturas y devuelve decisiones— así que se ejercita headless, sin un
/// <c>TextEditor</c> vivo, igual que <see cref="TextSearch"/> y los servicios del grafo.
///
/// Lo que estos tests protegen es un bug REAL y caro de rastrear: guardar la posición en
/// PÍXELES no sobrevive a un cambio de pestaña con <c>WordWrap</c>, porque AvalonEdit le da la
/// altura por defecto a toda línea que todavía no renderizó y el ScrollViewer recorta contra
/// esa altura falsa, en silencio. Medido sobre un editor real: se guardaba la línea 136 y se
/// aterrizaba en la 217.
/// </summary>
public class EditorScrollPolicyTests
{
    // ─── EditorScrollAnchor ───────────────────────────────────────────────────

    [Fact]
    public void Capture_stores_the_line_and_how_far_into_it_the_viewport_starts()
    {
        // La línea 40 arranca en 1000px y el viewport está en 1012 → 12px adentro de esa línea.
        var anchor = EditorScrollAnchor.Capture(topLineNumber: 40, lineVisualTop: 1000, verticalOffset: 1012);

        Assert.Equal(40, anchor.Line);
        Assert.Equal(12, anchor.LineDelta);
    }

    [Fact]
    public void Capture_never_produces_a_line_below_one_or_a_negative_delta()
    {
        var anchor = EditorScrollAnchor.Capture(topLineNumber: 0, lineVisualTop: 500, verticalOffset: 480);

        Assert.Equal(1, anchor.Line);
        Assert.Equal(0, anchor.LineDelta);
    }

    [Fact]
    public void A_fresh_tab_anchor_is_the_top_of_the_document()
    {
        Assert.True(EditorScrollAnchor.Top.IsTop);
        Assert.True(new OpenTab("x.md").ScrollAnchor.IsTop);
        Assert.False(new EditorScrollAnchor(40, 0).IsTop);
        Assert.False(new EditorScrollAnchor(1, 30).IsTop);
    }

    [Fact]
    public void DesiredOffset_adds_the_sub_line_delta_when_the_line_is_tall_enough()
    {
        var anchor = new EditorScrollAnchor(40, 12);

        Assert.Equal(1012, anchor.DesiredOffset(lineVisualTop: 1000, lineHeight: 160));
    }

    /// <summary>
    /// EL test del recorte, y la razón por la que <see cref="EditorScrollAnchor.DesiredOffset"/>
    /// recibe el alto: mientras la línea destino no se midió vale la altura por defecto, y un
    /// delta guardado sobre el párrafo YA envuelto se pasaría de largo hacia las líneas
    /// siguientes. Ahí el asentamiento queda trabado —el destino es coherente consigo mismo y
    /// aun así muestra otra línea— y no converge nunca. Caso medido: delta 103,7px sobre una
    /// línea que todavía valía ~20px, y la restauración aterrizaba en la línea 33 en vez de la 29.
    /// </summary>
    [Fact]
    public void DesiredOffset_clamps_the_delta_so_it_can_never_overshoot_into_the_next_line()
    {
        var anchor = new EditorScrollAnchor(29, 103.7);

        // Línea todavía sin medir (altura por defecto ~20px): el destino se queda DENTRO.
        Assert.Equal(1049 + 19, anchor.DesiredOffset(lineVisualTop: 1049, lineHeight: 20));

        // Ya medida como párrafo envuelto: el delta completo vuelve a ser aplicable.
        Assert.Equal(1049 + 103.7, anchor.DesiredOffset(lineVisualTop: 1049, lineHeight: 160));
    }

    [Fact]
    public void DesiredOffset_never_returns_a_negative_offset()
    {
        Assert.Equal(0, new EditorScrollAnchor(1, 0).DesiredOffset(lineVisualTop: -50, lineHeight: 20));
    }

    // ─── ScrollSettle ─────────────────────────────────────────────────────────

    [Fact]
    public void Settle_is_done_as_soon_as_the_editor_really_shows_the_target()
    {
        var settle = new ScrollSettle();

        // Los píxeles ni siquiera coinciden: manda lo que se está MOSTRANDO.
        Assert.Equal(ScrollSettleAction.Done, settle.Next(arrived: true, currentOffset: 0, desiredOffset: 4857));
        Assert.Equal(0, settle.Attempts);
    }

    [Fact]
    public void Settle_keeps_asking_while_the_target_moves_away_under_it()
    {
        var settle = new ScrollSettle();

        // El árbol de alturas crece en cada pasada, así que el destino sube y hay que reintentar.
        double current = 0;
        foreach (var desired in new double[] { 1300, 2500, 3600, 4400, 4857 })
        {
            Assert.Equal(ScrollSettleAction.Apply, settle.Next(false, current, desired));
            current = desired;   // el ScrollViewer nos deja llegar
        }

        Assert.Equal(ScrollSettleAction.Done, settle.Next(true, current, 4857));
        Assert.Equal(5, settle.Attempts);
    }

    [Fact]
    public void Settle_gives_up_instead_of_iterating_without_a_ceiling()
    {
        var settle = new ScrollSettle(maxAttempts: 3);

        for (var i = 0; i < 3; i++)
            Assert.Equal(ScrollSettleAction.Apply, settle.Next(false, i * 100, (i + 1) * 100));

        Assert.Equal(ScrollSettleAction.GiveUp, settle.Next(false, 300, 400));
    }

    /// <summary>
    /// Válvula de seguridad: volver a pedir el offset en el que YA estamos no mueve nada ni
    /// dispara otro pase de layout, así que el bucle se quedaría suscripto sin avanzar. Eso pasa
    /// cuando el destino cae dentro de la última pantalla del documento y no hay más recorrido.
    /// </summary>
    [Fact]
    public void Settle_stops_when_the_target_is_already_where_we_are_and_we_still_did_not_arrive()
    {
        var settle = new ScrollSettle();

        Assert.Equal(ScrollSettleAction.Apply,  settle.Next(false, 0, 1300));
        Assert.Equal(ScrollSettleAction.GiveUp, settle.Next(false, 1300, 1300));
    }

    [Fact]
    public void Settle_allows_the_very_first_apply_even_if_it_starts_on_target()
    {
        // Sin la guarda de "ya hubo un intento", una restauración que arranca en el offset
        // correcto pero mostrando otra línea se cortaría antes de intentar siquiera una vez.
        Assert.Equal(ScrollSettleAction.Apply, new ScrollSettle().Next(false, 500, 500));
    }

    // ─── EditorScrollPolicy.RevealOffset ──────────────────────────────────────

    [Fact]
    public void RevealOffset_does_not_move_a_line_that_is_already_fully_on_screen()
    {
        // Sin esta rama, repetir F3 dentro de la misma pantalla haría saltar la vista en cada
        // coincidencia.
        Assert.Equal(1000, EditorScrollPolicy.RevealOffset(
            lineTop: 1200, lineHeight: 40, currentOffset: 1000, viewportHeight: 600));
    }

    [Fact]
    public void RevealOffset_centers_a_line_below_the_fold_in_the_viewport()
    {
        // Centrada, igual que el ScrollTo anterior de AvalonEdit: el arreglo cambió el
        // mecanismo, no el tacto de Buscar/F3.
        Assert.Equal(5000 - 300, EditorScrollPolicy.RevealOffset(
            lineTop: 5000, lineHeight: 40, currentOffset: 0, viewportHeight: 600));
    }

    [Fact]
    public void RevealOffset_also_moves_for_a_line_above_the_viewport()
    {
        // 300 - 600/2 = 0: la línea está tan cerca del principio que centrarla equivale a
        // llevar la vista al tope. Lo importante es que SE MUEVE, no que aterrice en 0.
        Assert.Equal(0, EditorScrollPolicy.RevealOffset(
            lineTop: 300, lineHeight: 40, currentOffset: 1000, viewportHeight: 600));
    }

    [Fact]
    public void RevealOffset_clamps_at_the_top_of_the_document()
    {
        Assert.Equal(0, EditorScrollPolicy.RevealOffset(
            lineTop: 50, lineHeight: 40, currentOffset: 900, viewportHeight: 600));
    }

    [Fact]
    public void RevealOffset_handles_a_paragraph_taller_than_the_viewport()
    {
        // Nunca entra entera, así que "ya visible" jamás es cierto: se la lleva al centro y
        // ahí se queda (el destino deja de cambiar, el bucle converge).
        var first = EditorScrollPolicy.RevealOffset(
            lineTop: 4000, lineHeight: 2000, currentOffset: 0, viewportHeight: 600);

        Assert.Equal(3700, first);
        Assert.Equal(first, EditorScrollPolicy.RevealOffset(4000, 2000, first, 600));
    }

    [Fact]
    public void RevealOffset_is_a_no_op_before_the_control_has_a_size()
    {
        Assert.Equal(1234, EditorScrollPolicy.RevealOffset(
            lineTop: 5000, lineHeight: 40, currentOffset: 1234, viewportHeight: 0));
    }
}

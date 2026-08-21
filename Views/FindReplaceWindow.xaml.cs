using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Formulario flotante de Buscar/Reemplazar. NO es modal: es una ventana propiedad de
/// <see cref="MainWindow"/>, así que queda siempre por encima de ella pero el editor sigue
/// respondiendo — se puede hacer clic en el texto, corregir a mano y volver acá.
/// </summary>
/// <remarks>
/// Cerrarla la ESCONDE en vez de destruirla, para no perder el patrón ni las opciones entre
/// una búsqueda y la siguiente. Eso obliga a un cierre real explícito
/// (<see cref="ForceClose"/>) cuando la app termina: el <c>ShutdownMode</c> por defecto es
/// <c>OnLastWindowClose</c>, y una ventana escondida que cancela su propio Closing dejaría
/// el proceso vivo sin nada visible en pantalla.
/// </remarks>
public partial class FindReplaceWindow : Window
{
    private readonly FindReplaceViewModel _vm;
    private bool _allowClose;
    private bool _positioned;

    /// <summary>Se dispara al esconder la ventana, para devolverle el foco al editor.</summary>
    public event Action? Dismissed;

    public FindReplaceWindow(FindReplaceViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
    }

    /// <summary>
    /// Muestra el formulario (o lo trae al frente si ya estaba abierto) con el foco en el
    /// campo de búsqueda y su contenido seleccionado, de modo que tipear lo pise.
    /// </summary>
    public void ShowFor(bool showReplace)
    {
        _vm.PrepareForShow(showReplace);

        // Colocar ANTES de mostrar: si no, la ventana aparece en la posición por defecto
        // del SO y salta a su lugar un frame después.
        PositionOverOwner();

        if (!IsVisible) Show();
        Activate();

        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Cierre real, para el apagado de la app. Ver el remark de la clase.</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    // ─── Colocación ──────────────────────────────────────────────────────────

    /// <summary>
    /// Arriba a la derecha de la ventana principal, no centrada: centrada taparía justo el
    /// texto al que la búsqueda acaba de saltar. Se calcula una sola vez — si el usuario la
    /// arrastra a otro lado, ahí se queda.
    /// </summary>
    private void PositionOverOwner()
    {
        if (_positioned || Owner is null) return;

        // Con la principal maximizada, Owner.Left/Top devuelven la posición RESTAURADA
        // (puede ser negativa), no la real en pantalla. En ese caso se usa el área de
        // trabajo. Limitación conocida: SystemParameters.WorkArea es la del monitor
        // primario, así que maximizada en un monitor secundario la posición inicial puede
        // no ser la ideal — la ventana se arrastra y queda donde el usuario la deje.
        var bounds = Owner.WindowState == WindowState.Maximized
            ? SystemParameters.WorkArea
            : new Rect(Owner.Left, Owner.Top, Owner.ActualWidth, Owner.ActualHeight);

        Left = Math.Max(bounds.Left + 10, bounds.Right - Width - 40);
        Top  = bounds.Top + 90;

        _positioned = true;
    }

    // ─── Teclado ─────────────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        Dismiss();
        e.Handled = true;
    }

    // ─── Cierre ──────────────────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _allowClose) return;

        e.Cancel = true;
        Dismiss();
    }

    private void Dismiss()
    {
        Hide();
        Dismissed?.Invoke();
    }
}

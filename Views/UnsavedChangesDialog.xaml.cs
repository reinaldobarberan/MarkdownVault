using System.Windows;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Pregunta de cierre con cambios sin guardar. Es SOLO la superficie: todo lo que decide qué
/// mostrar (cuántos documentos, cuáles, en qué panel, si hay una operación en vuelo) ya lo
/// resolvió <see cref="UnsavedChangesReport"/>, que se prueba sin WPF.
///
/// Convención del proyecto (InputDialog, LinkPickerDialog, AboutWindow): ventana propia con
/// <c>Owner</c>, colores por <c>DynamicResource</c> para seguir el tema, y el resultado leído de
/// una propiedad en vez de mapear <c>DialogResult</c> a tres estados.
/// </summary>
public partial class UnsavedChangesDialog : Window
{
    /// <summary>
    /// Qué eligió el usuario. Arranca en <see cref="ConfirmResult.Cancel"/> a propósito: cerrar el
    /// diálogo con Esc o con la X NO puede significar "descartá mi trabajo".
    /// </summary>
    public ConfirmResult Result { get; private set; } = ConfirmResult.Cancel;

    internal UnsavedChangesDialog(UnsavedChangesReport report)
    {
        InitializeComponent();

        HeadlineText.Text        = report.Headline;
        DocumentList.ItemsSource = report.Lines;

        if (report.BusyWarning is { } busy)
        {
            BusyText.Text     = busy;
            BusyPanel.Visibility = Visibility.Visible;
        }

        if (report.NeverSavedWarning is { } neverSaved)
        {
            NeverSavedText.Text       = neverSaved;
            NeverSavedText.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => SaveButton.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result       = ConfirmResult.Yes;
        DialogResult = true;
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Result       = ConfirmResult.No;
        DialogResult = true;
    }
}

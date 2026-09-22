using System.Windows;

namespace MarkdownVault.Services;

/// <summary>
/// Concrete <see cref="IDialogService"/> backed by real WPF dialogs — a straight lift of the
/// MessageBox/SaveFileDialog/OpenFileDialog/LinkPickerDialog bodies that used to live directly
/// in EditorViewModel. Not unit-tested by design: this is the untestable edge, its whole job is
/// to be the thing tests replace with FakeDialogService.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public ConfirmResult ConfirmSaveChanges(string fileName)
    {
        var result = MessageBox.Show(
            $"'{fileName}' tiene cambios sin guardar. ¿Guardar antes de cerrar?",
            "Cambios sin guardar",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.No  => ConfirmResult.No,
            _                    => ConfirmResult.Cancel
        };
    }

    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? AskSaveFilePath(string suggestedFileName, string filter, string defaultExt)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Guardar como",
            Filter     = filter,
            DefaultExt = defaultExt,
            FileName   = suggestedFileName
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? AskImagePath()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Seleccionar imagen",
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.svg"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>
    /// Link-anchors change: the dialog's own anchor-aware second step needs to read an arbitrary
    /// note's headings/markers, which needs a <see cref="FileService"/> and a
    /// <see cref="MarkdownService"/> — reached here from the app-wide statics (this class is
    /// already documented as "the untestable edge", same spirit as <c>MainWindow.xaml.cs</c>'s
    /// <c>Plugins_Click</c> reaching <c>App.PluginManager</c>) and handed to the dialog
    /// explicitly, so <c>LinkPickerDialog</c> itself stays decoupled from <c>App</c>.
    /// </summary>
    public string? PickInternalLinkMarkdown(IReadOnlyList<string> vaultFiles, string currentFilePath, string vaultRoot)
    {
        var dlg = new Views.LinkPickerDialog(
            new List<string>(vaultFiles), currentFilePath, vaultRoot,
            App.FileService, App.MarkdownService)
        {
            Owner = Application.Current.MainWindow
        };
        return dlg.ShowDialog() == true ? dlg.ResultMarkdown : null;
    }
}

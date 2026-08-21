using System.ComponentModel;
using System.Windows;
using MarkdownVault.Services;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Ventana de administración de plugins: lista, activa, desactiva y —desde el SDK
/// 1.4.0— edita las LISTAS que los plugins contribuyen (glosarios, diccionarios).
///
/// El guardado de esas listas es EXPLÍCITO, así que esta ventana asume la
/// contrapartida: no se cierra tragándose cambios sin guardar. La pregunta vive
/// acá y no en el VM porque es la superficie modal (mismo criterio que
/// <see cref="WpfDialogService"/>, que también usa <c>MessageBox</c> para
/// exactamente esta pregunta).
/// </summary>
public partial class PluginsWindow : Window
{
    public PluginsWindow()
    {
        InitializeComponent();

        // El DataContext lo asigna quien abre la ventana (MainWindow), así que el
        // hook se engancha cuando ya está puesto, no en el constructor.
        Loaded += (_, _) =>
        {
            if (DataContext is PluginsViewModel vm)
                vm.ConfirmPendingChanges = Ask;
        };
    }

    /// <summary>
    /// Pregunta por las listas con cambios sin guardar. Sí = guardarlas,
    /// No = descartarlas, Cancelar = no seguir con lo que se iba a hacer.
    /// </summary>
    private ConfirmResult Ask(IReadOnlyList<string> titles)
    {
        var names  = string.Join(", ", titles);
        var plural = titles.Count == 1 ? "una lista" : $"{titles.Count} listas";

        var answer = MessageBox.Show(
            this,
            $"Hay cambios sin guardar en {plural}: {names}.\n\n¿Los guardo?",
            "Cambios sin guardar",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return answer switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.No  => ConfirmResult.No,
            _                    => ConfirmResult.Cancel
        };
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not PluginsViewModel vm) return;

        // TryReleasePending devuelve false si el usuario canceló O si el guardado que
        // pidió falló. En los dos casos la ventana se queda abierta: cerrar igual
        // sería tirar los cambios justo después de que pidió conservarlos.
        if (!vm.TryReleasePending(vm.PendingLists))
            e.Cancel = true;
    }
}

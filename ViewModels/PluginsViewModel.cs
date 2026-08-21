using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.ViewModels;

/// <summary>
/// Backing VM for the Plugins window: lists discovered plugins, toggles them, and
/// —desde el SDK 1.4.0— dibuja las LISTAS EDITABLES que cada plugin contribuye
/// (<see cref="PluginListSetting"/>). El plugin declara los datos; esta ventana
/// pone la interfaz, así que ningún plugin necesita declarar una Window propia
/// para tener configuración editable (y conserva la descarga en caliente).
/// </summary>
public partial class PluginsViewModel : ObservableObject
{
    private readonly PluginManager   _manager;
    private readonly PluginRegistry? _registry;

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

    /// <summary>Ruta de la carpeta escaneada (se muestra en el encabezado).</summary>
    public string PluginsFolder { get; }

    public bool HasPlugins => Plugins.Count > 0;

    /// <summary>
    /// Cómo se pregunta por los cambios sin guardar de las listas. La VISTA lo
    /// enchufa (es un MessageBox), el VM solo decide cuándo hace falta preguntar.
    /// Sin hook, ninguna acción destructiva sigue adelante: es preferible que
    /// "Recargar" no haga nada a que se coma media hora de glosario en silencio.
    /// </summary>
    public Func<IReadOnlyList<string>, ConfirmResult>? ConfirmPendingChanges { get; set; }

    public PluginsViewModel(PluginManager manager, PluginRegistry? registry = null)
    {
        _manager      = manager;
        _registry     = registry;
        PluginsFolder = manager.PluginsRoot;
        Build();
    }

    private void Build()
    {
        foreach (var row in Plugins) row.ClearListSettings();
        Plugins.Clear();

        foreach (var descriptor in _manager.Plugins)
            Plugins.Add(new PluginRowViewModel(descriptor, this));

        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    /// <summary>Las listas de este plugin que el registry expone AHORA (vacío si está desactivado).</summary>
    internal IReadOnlyList<PluginListSetting> ListSettingsFor(string pluginId) =>
        _registry?.ListSettingsFor(pluginId) ?? Array.Empty<PluginListSetting>();

    // ─── Cambios sin guardar ─────────────────────────────────────────────────

    /// <summary>Todas las listas con ediciones sin guardar, de todos los plugins.</summary>
    public IReadOnlyList<PluginListSettingViewModel> PendingLists =>
        Plugins.SelectMany(p => p.ListSettings).Where(l => l.IsDirty).ToList();

    public bool HasPendingChanges => PendingLists.Count > 0;

    /// <summary>Lo llama una lista al ensuciarse o limpiarse.</summary>
    internal void OnListDirtyChanged() => OnPropertyChanged(nameof(HasPendingChanges));

    /// <summary>
    /// Deja el camino libre para una acción que va a tirar los cambios en curso
    /// (cerrar la ventana, recargar, desactivar el plugin). Devuelve false si el
    /// usuario canceló o si el guardado que pidió falló — en los dos casos, quien
    /// llamó NO debe seguir.
    /// </summary>
    public bool TryReleasePending(IReadOnlyList<PluginListSettingViewModel> pending)
    {
        if (pending.Count == 0) return true;

        var answer = ConfirmPendingChanges?.Invoke(pending.Select(l => l.Title).ToList())
                     ?? ConfirmResult.Cancel;

        switch (answer)
        {
            case ConfirmResult.Yes:
                foreach (var list in pending) list.Save();
                // Si alguno no pudo guardar, su IsDirty sigue en true y el mensaje del
                // error quedó al lado de la lista: no se sigue adelante.
                return pending.All(l => !l.IsDirty);

            case ConfirmResult.No:
                foreach (var list in pending) list.Reload();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Re-escanea la carpeta y reconstruye la lista.</summary>
    [RelayCommand]
    private void Reload()
    {
        if (!TryReleasePending(PendingLists)) return;

        // ANTES del LoadAll, que descarga los ALC de todos los plugins activos: cada
        // lista retiene delegates que apuntan a código del plugin, y una sola de esas
        // referencias viva alcanza para clavar el contexto de carga.
        foreach (var row in Plugins) row.ClearListSettings();

        _manager.LoadAll();
        Build();
    }

    /// <summary>
    /// Activa o desactiva un plugin. Pasa por acá y no directo por el manager
    /// porque DESACTIVAR borra las contribuciones del plugin (incluidas sus
    /// listas), así que primero hay que preguntar por lo que quedó sin guardar.
    /// Devuelve false si el cambio no se hizo (el usuario canceló).
    /// </summary>
    internal bool RequestToggle(PluginRowViewModel row, bool enabled)
    {
        if (!enabled)
        {
            if (!TryReleasePending(row.ListSettings.Where(l => l.IsDirty).ToList()))
                return false;

            // ORDEN LOAD-BEARING: soltar las listas ANTES de desactivar. Desactivar
            // termina en Unload() + GC.Collect() del ALC del plugin, y los delegates
            // Load/Save/Describe apuntan a código de ese ensamblado. Si el VM los
            // tuviera todavía agarrados en ese momento, la descarga en caliente se
            // rompería exactamente por el mismo motivo que con Eisenhower — solo que
            // esta vez la culpa sería del host, no del plugin.
            row.ClearListSettings();
        }

        _manager.SetEnabled(row.Id, enabled);
        row.RefreshFromDescriptor();
        OnPropertyChanged(nameof(HasPendingChanges));
        return true;
    }
}

/// <summary>Una fila de la lista de plugins.</summary>
public partial class PluginRowViewModel : ObservableObject
{
    private readonly PluginDescriptor _descriptor;
    private readonly PluginsViewModel _parent;

    /// <summary>Evita que revertir el CheckBox tras un "Cancelar" vuelva a disparar el toggle.</summary>
    private bool _suppressToggle;

    public PluginRowViewModel(PluginDescriptor descriptor, PluginsViewModel parent)
    {
        _descriptor = descriptor;
        _parent     = parent;

        Id          = descriptor.Metadata.Id;
        Name        = string.IsNullOrWhiteSpace(descriptor.Metadata.Name) ? descriptor.Metadata.Id : descriptor.Metadata.Name;
        Version     = descriptor.Metadata.Version;
        Author      = descriptor.Metadata.Author;
        Description = descriptor.Metadata.Description;

        // Set the backing field directly to avoid firing OnEnabledChanged during init.
        _enabled = descriptor.Enabled;

        LoadListSettings();
    }

    public string  Id          { get; }
    public string  Name        { get; }
    public string  Version     { get; }
    public string  Author      { get; }
    public string  Description { get; }

    public bool    IsFailed  => _descriptor.State == PluginState.Failed;
    public string? Error     => _descriptor.Error;
    public string  StateText => _descriptor.State switch
    {
        PluginState.Active => "Activo",
        PluginState.Failed => "Error",
        _                  => "Inactivo"
    };

    /// <summary>Un plugin fallido no se puede activar.</summary>
    public bool CanToggle => !IsFailed;

    /// <summary>Las listas editables que contribuye este plugin (vacío si no contribuye ninguna).</summary>
    public ObservableCollection<PluginListSettingViewModel> ListSettings { get; } = new();

    public bool HasListSettings => ListSettings.Count > 0;

    [ObservableProperty] private bool _enabled;

    partial void OnEnabledChanged(bool value)
    {
        if (_suppressToggle) return;
        if (_parent.RequestToggle(this, value)) return;

        // El usuario canceló: el CheckBox vuelve a donde estaba sin re-disparar nada.
        _suppressToggle = true;
        Enabled         = !value;
        _suppressToggle = false;
    }

    /// <summary>
    /// Vuelve a leer del descriptor y del registry lo que pudo cambiar al
    /// activar/desactivar: el estado (una activación puede FALLAR) y el juego de
    /// listas editables, que aparece al activar y desaparece al desactivar.
    /// </summary>
    internal void RefreshFromDescriptor()
    {
        if (Enabled != _descriptor.Enabled)
        {
            _suppressToggle = true;
            Enabled         = _descriptor.Enabled;   // p. ej. Activate() falló y quedó apagado
            _suppressToggle = false;
        }

        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanToggle));

        LoadListSettings();
    }

    private void LoadListSettings()
    {
        ClearListSettings();

        foreach (var setting in _parent.ListSettingsFor(Id))
        {
            var vm = new PluginListSettingViewModel(setting) { DirtyChanged = _parent.OnListDirtyChanged };
            ListSettings.Add(vm);
        }

        OnPropertyChanged(nameof(HasListSettings));
    }

    /// <summary>
    /// Suelta las listas de este plugin. Importa de verdad: cada
    /// <see cref="PluginListSettingViewModel"/> retiene los delegates Load/Save/Describe,
    /// que apuntan a código del ensamblado del plugin. Dejarlos vivos en un VM
    /// huérfano es exactamente el tipo de referencia que impide descargar el ALC, así
    /// que esto tiene que correr ANTES de cualquier desactivación o recarga, nunca
    /// después.
    /// </summary>
    internal void ClearListSettings()
    {
        foreach (var list in ListSettings) list.DirtyChanged = null;
        ListSettings.Clear();
        OnPropertyChanged(nameof(HasListSettings));
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.ViewModels;

/// <summary>Backing VM for the Plugins window: lists discovered plugins and toggles them.</summary>
public partial class PluginsViewModel : ObservableObject
{
    private readonly PluginManager _manager;

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

    /// <summary>Ruta de la carpeta escaneada (se muestra en el encabezado).</summary>
    public string PluginsFolder { get; }

    public bool HasPlugins => Plugins.Count > 0;

    public PluginsViewModel(PluginManager manager)
    {
        _manager      = manager;
        PluginsFolder = manager.PluginsRoot;
        Build();
    }

    private void Build()
    {
        Plugins.Clear();
        foreach (var descriptor in _manager.Plugins)
            Plugins.Add(new PluginRowViewModel(descriptor, _manager.SetEnabled));
        OnPropertyChanged(nameof(HasPlugins));
    }

    /// <summary>Re-escanea la carpeta y reconstruye la lista.</summary>
    [RelayCommand]
    private void Reload()
    {
        _manager.LoadAll();
        Build();
    }
}

/// <summary>Una fila de la lista de plugins.</summary>
public partial class PluginRowViewModel : ObservableObject
{
    private readonly Action<string, bool> _onToggle;

    public PluginRowViewModel(PluginDescriptor descriptor, Action<string, bool> onToggle)
    {
        _onToggle   = onToggle;
        Id          = descriptor.Metadata.Id;
        Name        = string.IsNullOrWhiteSpace(descriptor.Metadata.Name) ? descriptor.Metadata.Id : descriptor.Metadata.Name;
        Version     = descriptor.Metadata.Version;
        Author      = descriptor.Metadata.Author;
        Description = descriptor.Metadata.Description;
        IsFailed    = descriptor.State == PluginState.Failed;
        Error       = descriptor.Error;
        StateText   = descriptor.State switch
        {
            PluginState.Active => "Activo",
            PluginState.Failed => "Error",
            _                  => "Inactivo"
        };

        // Set the backing field directly to avoid firing OnEnabledChanged during init.
        _enabled = descriptor.Enabled;
    }

    public string  Id          { get; }
    public string  Name        { get; }
    public string  Version     { get; }
    public string  Author      { get; }
    public string  Description { get; }
    public string  StateText   { get; }
    public bool    IsFailed    { get; }
    public string? Error       { get; }

    /// <summary>Un plugin fallido no se puede activar.</summary>
    public bool CanToggle => !IsFailed;

    [ObservableProperty] private bool _enabled;

    partial void OnEnabledChanged(bool value) => _onToggle(Id, value);
}

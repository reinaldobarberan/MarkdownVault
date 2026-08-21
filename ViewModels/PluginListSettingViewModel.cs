using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownVault.PluginSdk;
using MarkdownVault.Services.Plugins;

namespace MarkdownVault.ViewModels;

/// <summary>
/// La lista editable de UN <see cref="PluginListSetting"/>, tal como la dibuja la
/// ventana de complementos. Acá está TODO el comportamiento genérico —agregar,
/// editar, borrar, filtrar, avisar duplicados, guardar— y ni una línea propia del
/// plugin: lo específico entra por los tres delegates del contrato
/// (<c>Load</c> / <c>Save</c> / <c>Describe</c>).
///
/// Los tres delegates son CÓDIGO DEL PLUGIN. Se llaman siempre dentro de un
/// try/catch: un plugin que explota al guardar tiene que dejar un mensaje al lado
/// de su lista, no tumbar la ventana de complementos.
///
/// La lógica que se puede equivocar (normalización, duplicados, filtro) no vive
/// acá sino en <see cref="PluginListRules"/>, que se prueba sin WPF.
/// </summary>
public sealed partial class PluginListSettingViewModel : ObservableObject
{
    private readonly PluginListSetting _setting;

    /// <summary>Todas las entradas, filtradas o no. <see cref="Visible"/> es una vista de esto.</summary>
    private readonly List<PluginListEntryViewModel> _all = new();

    /// <summary>Le avisa al VM padre que cambió el estado "hay cambios sin guardar".</summary>
    internal Action? DirtyChanged;

    public PluginListSettingViewModel(PluginListSetting setting)
    {
        _setting    = setting;
        Title       = string.IsNullOrWhiteSpace(setting.Title) ? setting.Id : setting.Title;
        Description = setting.Description;
        KeyLabel    = string.IsNullOrWhiteSpace(setting.KeyLabel) ? "Entrada" : setting.KeyLabel;
        ValueLabel  = setting.ValueLabel;

        Reload();
    }

    // ─── Forma de la lista (la declara el plugin, no cambia en vida de la ventana) ──

    public string  Title       { get; }
    public string? Description { get; }
    public string  KeyLabel    { get; }
    public string? ValueLabel  { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Dos columnas o una. Lo decide <see cref="PluginListSetting.ValueLabel"/>:
    /// null ⇒ una sola. El glosario del dictado usa una; el diccionario de
    /// pronunciación del Lector usará dos con este mismo código.
    /// </summary>
    public bool HasValueColumn => ValueLabel is not null;

    /// <summary>Las entradas que se dibujan (todas, o las que pasan el filtro).</summary>
    public ObservableCollection<PluginListEntryViewModel> Visible { get; } = new();

    // ─── Estado editable ─────────────────────────────────────────────────────

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty] private string _filter = "";

    [ObservableProperty] private string _newKey = "";

    [ObservableProperty] private string? _newValue;

    /// <summary>Aviso bajo el campo de agregar (vacío o duplicado). Null = sin aviso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewEntryProblem))]
    private string? _newEntryProblem;

    /// <summary>Lo que devolvió <c>Describe</c> del plugin. Null = el plugin no dijo nada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescribeText))]
    private string? _describeText;

    /// <summary>Resultado de la última acción ("Guardado.", o el error del plugin).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _hasProblems;

    public bool HasNewEntryProblem => NewEntryProblem is not null;
    public bool HasDescribeText    => DescribeText    is not null;
    public bool HasStatusText      => StatusText      is not null;

    /// <summary>Guardar solo cuando hay algo que guardar y nada que corregir.</summary>
    public bool CanSave => IsDirty && !HasProblems;

    /// <summary>Contador del encabezado: "36 entradas", o "12 de 36" con filtro puesto.</summary>
    public string HeaderBadge =>
        Visible.Count == _all.Count
            ? $"{_all.Count} {(_all.Count == 1 ? "entrada" : "entradas")}"
            : $"{Visible.Count} de {_all.Count}";

    // ─── Carga y filtro ──────────────────────────────────────────────────────

    /// <summary>
    /// Vuelve a pedirle los datos al plugin y tira los cambios en curso. Es lo que
    /// hace "Descartar", y también lo que corre al construir el VM.
    /// </summary>
    public void Reload()
    {
        // El mensaje anterior ("Guardado: 42 entradas") deja de ser cierto en cuanto
        // se relee: se limpia primero para que lo que quede abajo sea de ESTA carga.
        StatusText = null;

        IReadOnlyList<PluginListEntry> loaded;
        try
        {
            loaded = _setting.Load() ?? Array.Empty<PluginListEntry>();
        }
        catch (Exception ex)
        {
            loaded     = Array.Empty<PluginListEntry>();
            StatusText = $"El complemento no pudo leer la lista: {ex.Message}";
        }

        _all.Clear();
        // Sanitize también acá: si el plugin devuelve claves repetidas o con
        // espacios, la pantalla arranca ya en el estado que se va a poder guardar.
        foreach (var entry in PluginListRules.Sanitize(loaded))
            _all.Add(new PluginListEntryViewModel(this, entry));

        NewKey           = "";
        NewValue         = null;
        NewEntryProblem  = null;
        SetDirty(false);
        ApplyFilter();
        Revalidate();
        RefreshDescribe();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Visible.Clear();
        foreach (var entry in _all)
            if (PluginListRules.Matches(entry.ToEntry(), Filter))
                Visible.Add(entry);

        OnPropertyChanged(nameof(HeaderBadge));
        OnPropertyChanged(nameof(HasVisibleEntries));
        OnPropertyChanged(nameof(EmptyText));
    }

    public bool HasVisibleEntries => Visible.Count > 0;

    public string EmptyText =>
        _all.Count == 0
            ? "La lista está vacía. Agregá la primera entrada acá abajo."
            : "Ninguna entrada coincide con la búsqueda.";

    // ─── Alta, baja y edición ────────────────────────────────────────────────

    /// <summary>
    /// Agrega lo que haya en los campos de alta. Es lo que dispara Enter — el
    /// botón está para quien prefiera el mouse, pero el camino pensado es teclear
    /// el término y apretar Enter, sin soltar el teclado entre uno y otro.
    /// </summary>
    [RelayCommand]
    public void AddNew()
    {
        var problem = PluginListRules.Validate(Snapshot(), NewKey);
        if (problem != ListEntryProblem.None)
        {
            NewEntryProblem = PluginListRules.Message(problem, KeyLabel);
            return;
        }

        var entry = PluginListRules.Normalize(new PluginListEntry(NewKey, NewValue));
        _all.Add(new PluginListEntryViewModel(this, entry));

        NewKey          = "";
        NewValue        = null;
        NewEntryProblem = null;

        // Si el filtro puesto esconde justo lo que se acaba de agregar, el usuario
        // ve "no pasó nada". Se limpia el filtro: agregar gana sobre buscar.
        if (!PluginListRules.Matches(entry, Filter)) Filter = "";
        else                                          ApplyFilter();

        SetDirty(true);
        Revalidate();
        RefreshDescribe();
    }

    internal void Remove(PluginListEntryViewModel entry)
    {
        if (!_all.Remove(entry)) return;
        Visible.Remove(entry);

        SetDirty(true);
        ApplyFilter();
        Revalidate();
        RefreshDescribe();
    }

    /// <summary>Una fila se editó en el lugar: revalidar todo y recalcular el aviso del plugin.</summary>
    internal void OnEntryEdited()
    {
        SetDirty(true);
        Revalidate();
        RefreshDescribe();
    }

    /// <summary>
    /// Marca duplicados y vacíos fila por fila. O(n²) sobre decenas de entradas:
    /// irrelevante, y evita tener que mantener un índice sincronizado con la
    /// edición en vivo.
    /// </summary>
    private void Revalidate()
    {
        var snapshot = Snapshot();
        var any      = false;

        for (var i = 0; i < _all.Count; i++)
        {
            var problem = PluginListRules.Validate(snapshot, _all[i].Key, exceptIndex: i);
            _all[i].Problem = PluginListRules.Message(problem, KeyLabel);
            any |= problem != ListEntryProblem.None;
        }

        HasProblems = any;

        // El aviso del alta se recalcula solo si ya había uno: si no, tipear en las
        // filas de arriba no tiene por qué encender un error abajo.
        if (NewEntryProblem is not null)
        {
            var problem = PluginListRules.Validate(snapshot, NewKey);
            NewEntryProblem = PluginListRules.Message(problem, KeyLabel);
        }
    }

    private IReadOnlyList<PluginListEntry> Snapshot()
    {
        var list = new List<PluginListEntry>(_all.Count);
        foreach (var entry in _all) list.Add(entry.ToEntry());
        return list;
    }

    // ─── Guardar / descartar ─────────────────────────────────────────────────

    /// <summary>
    /// Guardado EXPLÍCITO. El delegate del plugin tiene efectos de verdad (reescribe
    /// un archivo, reconfigura un motor): dispararlo en cada tecla sería ruido y
    /// dejaría a medio guardar cualquier estado inválido. El precio —que se puedan
    /// perder cambios— lo cubre el aviso al cerrar la ventana.
    /// </summary>
    [RelayCommand]
    public void Save()
    {
        if (HasProblems)
        {
            StatusText = "Corregí los avisos de la lista antes de guardar.";
            return;
        }

        var entries = PluginListRules.Sanitize(Snapshot());
        try
        {
            _setting.Save(entries);
        }
        catch (Exception ex)
        {
            // NO se limpia IsDirty: el guardado no ocurrió, y el aviso al cerrar la
            // ventana tiene que seguir apareciendo.
            StatusText = $"No se pudo guardar: {ex.Message}";
            return;
        }

        SetDirty(false);
        StatusText = $"Guardado: {entries.Count} {(entries.Count == 1 ? "entrada" : "entradas")}.";
        RefreshDescribe();
    }

    /// <summary>Tira los cambios en curso y vuelve a lo que tiene guardado el plugin.</summary>
    [RelayCommand]
    public void Discard()
    {
        Reload();
        // Sin pisar un error de lectura: si Reload dejó un mensaje, ese es el que importa.
        StatusText ??= "Cambios descartados.";
    }

    private void SetDirty(bool value)
    {
        if (IsDirty == value) return;
        IsDirty = value;
        if (value) StatusText = null;   // el "Guardado." anterior ya no vale
        DirtyChanged?.Invoke();
    }

    /// <summary>
    /// Le pregunta al plugin qué decir de la lista que hay en pantalla. Se lo
    /// llama con lo que el usuario ve, guardado o no: es lo que permite que el
    /// dictado avise "el motor está corriendo con otro glosario" apenas cambia
    /// algo, en vez de después.
    /// </summary>
    private void RefreshDescribe()
    {
        if (_setting.Describe is null) { DescribeText = null; return; }

        try   { DescribeText = _setting.Describe(PluginListRules.Sanitize(Snapshot())); }
        catch { DescribeText = null; }   // un Describe que explota no vale un cartel de error
    }
}

/// <summary>Una fila editable de la lista.</summary>
public sealed partial class PluginListEntryViewModel : ObservableObject
{
    private readonly PluginListSettingViewModel _owner;

    public PluginListEntryViewModel(PluginListSettingViewModel owner, PluginListEntry entry)
    {
        _owner = owner;
        // Campos directos, no propiedades: escribirlos por el setter dispararía
        // OnEntryEdited y la lista arrancaría marcada como "sin guardar".
        _key   = entry.Key;
        _value = entry.Value;
    }

    [ObservableProperty] private string  _key;
    [ObservableProperty] private string? _value;

    /// <summary>Aviso de esta fila (duplicada o vacía). Null = está bien.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    public bool HasProblem => Problem is not null;

    /// <summary>Espejo del padre, para que el DataTemplate de la fila no tenga que ir a buscarlo.</summary>
    public bool    HasValueColumn => _owner.HasValueColumn;
    public string  KeyLabel       => _owner.KeyLabel;
    public string? ValueLabel     => _owner.ValueLabel;

    partial void OnKeyChanged(string value)    => _owner.OnEntryEdited();
    partial void OnValueChanged(string? value) => _owner.OnEntryEdited();

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    public PluginListEntry ToEntry() => new(Key, Value);
}

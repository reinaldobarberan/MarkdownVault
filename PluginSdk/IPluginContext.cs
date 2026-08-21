namespace MarkdownVault.PluginSdk;

/// <summary>
/// Lo que el host le entrega al plugin en <see cref="IPlugin.Configure"/>:
/// una fachada de solo-lectura hacia el host más los métodos de registro de
/// contribuciones.
/// </summary>
public interface IPluginContext
{
    /// <summary>Fachada de solo-lectura hacia el host.</summary>
    IHostServices Host { get; }

    /// <summary>Almacenamiento sandbox por plugin (lectura y escritura), aislado del vault.</summary>
    IPluginStorage Storage { get; }

    /// <summary>Metadata resuelta desde el plugin.json (id, versión, etc.).</summary>
    PluginMetadata Metadata { get; }

    void AddMarkdownExtension(IMarkdownContribution extension, int order = 0);
    void AddPreviewAsset(PreviewAsset asset);
    void AddCommand(PluginCommand command);
    void AddCommandGroup(PluginCommandGroup group);
    void AddPanel(PluginPanel panel);

    /// <summary>
    /// Registra una LISTA EDITABLE que el host dibuja en la ventana de
    /// complementos (SDK 1.4.0). El plugin declara los datos y sus reglas; el
    /// host pone la interfaz —agregar, editar, borrar, filtrar, avisar
    /// duplicados— y nunca le pide al plugin un tipo de WPF. Ver
    /// <see cref="PluginListSetting"/> para el porqué de ese reparto.
    ///
    /// Igual que <see cref="AddCommand"/>: la contribución queda etiquetada con
    /// el id del plugin y se suelta entera al desactivarlo, así que registrar una
    /// lista NO compromete la descarga en caliente.
    /// </summary>
    void AddListSetting(PluginListSetting setting);

    void OnVaultEvent(Action<VaultEvent> handler);

    /// <summary>Log dirigido a la consola de diagnóstico del host.</summary>
    void Log(string message);

    /// <summary>
    /// Pide un re-render del preview activo aunque el contenido no haya cambiado
    /// (p. ej. tras escribir en <see cref="Storage"/>). Sincrónico, sin marshaling
    /// de hilo a cargo del llamador; no-op seguro si no hay documento abierto.
    /// </summary>
    void RequestPreviewRefresh();
}

/// <summary>
/// Superficie mínima y de SOLO-LECTURA que el host expone a los plugins.
/// Deliberadamente no incluye escritura ni borrado del vault.
/// </summary>
public interface IHostServices
{
    string? VaultRoot { get; }
    string? ActiveFilePath { get; }
    bool    IsDarkTheme { get; }

    /// <summary>Lee un archivo del vault por su ruta relativa. Lanza si escapa del vault.</summary>
    Task<string> ReadFileAsync(string relativePath);

    /// <summary>
    /// Aviso INSTANTÁNEO en la barra de estado (esquina inferior derecha). Sirve
    /// para "ya está" / "falló esto": un texto que el usuario lee de reojo y se
    /// pisa con el siguiente. Para operaciones LARGAS —donde el usuario tiene que
    /// esperar— usá <see cref="BeginProgress"/>: esto es letra chica en un rincón
    /// y no alcanza para contar minutos de trabajo.
    /// </summary>
    void ShowStatus(string message);

    /// <summary>
    /// Abre un canal de progreso VISIBLE para una operación larga (SDK 1.3.0+).
    /// Devuelve un handle con ciclo de vida: mostralo con <c>using</c> y el
    /// indicador se limpia solo al salir del bloque. Nunca devuelve null — si el
    /// host no tiene barra de progreso conectada, devuelve
    /// <see cref="NoOpProgressScope.Instance"/>, así que el plugin no necesita
    /// comprobar nada.
    ///
    /// <paramref name="title"/> es el rótulo fijo de la operación ("Dictado de
    /// voz"), no el paso actual: el paso va en <see cref="IProgressScope.Report"/>.
    ///
    /// Se puede llamar desde cualquier hilo. Ver <see cref="IProgressScope"/> para
    /// el contrato completo (cancelación, concurrencia, descarga en caliente).
    /// </summary>
    IProgressScope BeginProgress(string title);

    /// <summary>
    /// Abre un archivo del vault (ruta relativa) en el editor del host. Confinado al
    /// vault: no-op silencioso si la ruta escapa del vault o el archivo no existe —
    /// nunca lanza. A diferencia de <see cref="ReadFileAsync"/>, no devuelve contenido;
    /// solo pide al host que active/abra la pestaña correspondiente.
    /// </summary>
    void OpenVaultFile(string relativePath);
}

/// <summary>Acceso acotado al editor para los comandos contribuidos.</summary>
public interface IEditorContext
{
    /// <summary>Texto completo del documento activo.</summary>
    string Content { get; }

    /// <summary>Texto actualmente seleccionado (vacío si no hay selección).</summary>
    string SelectedText { get; }

    /// <summary>Inserta texto en el cursor (en una línea nueva si hace falta).</summary>
    void InsertAtCaret(string text);

    /// <summary>Envuelve la selección con <paramref name="before"/>/<paramref name="after"/>.</summary>
    void WrapSelection(string before, string after);

    /// <summary>Reemplaza la selección (o inserta en el cursor si no hay selección).</summary>
    void ReplaceSelection(string text);
}

# MarkdownVault — Contexto del Proyecto

## Descripción General

MarkdownVault es un **editor de Markdown tipo Obsidian** para escritorio, construido con WPF y .NET 8. Permite gestionar uno o **varios "vaults" (carpetas) abiertos simultáneamente** — workspace multi-root — con archivos Markdown, HTML y Mermaid, vista previa en tiempo real, temas claro/oscuro, y edición con formato enriquecido.

## Stack Tecnológico

| Componente | Tecnología | Versión |
|------------|-----------|---------|
| Framework | .NET 8 (WPF) | `net8.0-windows` |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| Editor de código | Quicker.AvalonEdit | 6.3.1 |
| Parser Markdown | Markdig | 1.1.2 |
| Vista previa HTML | Microsoft.Web.WebView2 | 1.0.3912.50 |
| Interacciones XAML | Microsoft.Xaml.Behaviors.Wpf | 1.1.135 |
| Diagramas | Mermaid.js (CDN) | 11.15.0 |

## Arquitectura

Patrón **MVVM** con inyección manual de servicios en `App.xaml.cs`.

### Estructura de Carpetas

```
MarkdownVault/
├── App.xaml / App.xaml.cs        # Startup, inyección de servicios
├── MainWindow.xaml / .cs         # Shell raíz (redirige a Views/MainWindow)
├── Models/
│   ├── AppSettings.cs            # Configuración persistida (tema, fuente, OpenVaultPaths = set de vaults abiertos)
│   ├── OpenTab.cs                # Modelo de pestaña abierta
│   ├── VaultFile.cs              # Nodo de archivo/directorio en el vault
│   └── ViewMode.cs               # Enum: EditorOnly | EditAndPreview | ViewerOnly
├── ViewModels/
│   ├── MainViewModel.cs          # VM principal (vaults abiertos, tema, fuente, explorador)
│   ├── EditorGroupViewModel.cs   # VM del editor (tabs, contenido, preview, formato)
│   ├── FileTreeViewModel.cs      # VM del árbol de archivos (una sección por vault abierto)
│   ├── VaultsViewModel.cs        # VM de "Administrar vaults" (abrir/cerrar cada vault conocido)
│   ├── GraphViewModel.cs         # VM del grafo de notas, scopeado al vault del tab activo
│   └── FindReplaceViewModel.cs   # VM del formulario Buscar/Reemplazar (patrón, opciones, comandos)
├── Views/
│   ├── MainWindow.xaml / .cs     # Ventana principal (layout, WebView2, tabs)
│   ├── EditorView.xaml / .cs     # Editor AvalonEdit + toolbar de formato
│   ├── FileTreeView.xaml / .cs   # Árbol lateral del vault
│   ├── FindReplaceWindow.xaml / .cs # Formulario flotante de Buscar/Reemplazar (no modal)
│   ├── FindCommands.cs           # RoutedUICommands de Buscar/Reemplazar (menú + atajos)
│   └── InputDialog.xaml / .cs    # Diálogo para input de usuario
├── Services/
│   ├── FileService.cs            # I/O de archivos, escaneo; mantiene VaultRoots (multi-root)
│   ├── GraphService.cs           # Grafo de notas/enlaces, scopeado a un vault root por vez
│   ├── MarkdownService.cs        # Markdown → HTML (Markdig) + CSS + Mermaid
│   ├── SettingsService.cs        # Persistencia de configuración
│   ├── ISpellCheckService.cs     # Contrato del corrector + record SpellError
│   ├── WindowsSpellCheckService.cs # Motor COM Windows ISpellChecker
│   ├── TextSearch.cs             # Motor puro de Buscar/Reemplazar (string in / offsets out)
│   └── IFindReplaceTarget.cs     # Costura formulario de búsqueda ↔ editor con foco
├── Helpers/
│   ├── BoolToIconConverter.cs    # Converter WPF
│   ├── BoolToVisibilityConverter.cs
│   ├── SpellCheckColorizer.cs    # Subrayado ondulado (DocumentColorizingTransformer)
│   └── MarkdownProseMask.cs      # Enmascara código/URLs/links para el corrector
└── Resources/
    └── Themes/
        ├── LightTheme.xaml       # Diccionario de recursos tema claro
        └── DarkTheme.xaml        # Diccionario de recursos tema oscuro
```

### Servicios (Singleton vía App.xaml.cs)

- **`FileService`** — Lectura/escritura de archivos, escaneo recursivo, gestión de rutas. Mantiene **`VaultRoots`** (lista ordenada de todos los vaults abiertos, no un único vault activo) con un `FileSystemWatcher` por root; expone `AddRoot`/`RemoveRoot` para abrir/cerrar un vault sin tocar los demás, y `GetOwningRoot(path)` (longest-prefix match, `null` si el path no está bajo ningún root abierto) para que wikilinks, grafo, imágenes y el picker de enlaces resuelvan siempre contra el vault **dueño** del archivo activo.
- **`MarkdownService`** — Convierte Markdown a HTML completo con CSS GitHub-flavored inlineado, soporte para Mermaid.js, y tablas con scroll horizontal.
- **`SettingsService`** — Persistencia de `IsDarkTheme` y otras configuraciones entre sesiones.
- **`WindowsSpellCheckService`** (`ISpellCheckService`) — Corrector ortográfico vía la API COM `ISpellChecker` de Windows (usa los diccionarios del SO). Resuelve el idioma desde el setting `SpellCheckLanguage` con fallback a la cultura del SO. Degrada a `IsAvailable=false` si el API o el idioma no están disponibles.

### Layout de la Ventana Principal

```
┌─────────────────────────────────────────────────────┐
│  Menu Bar (Archivo | Vista)                         │
├─────────────────────────────────────────────────────┤
│  Tab Bar (pestañas de archivos abiertos)            │
├──────────┬──────────────────┬───────────────────────┤
│          │                  │                       │
│  File    │   AvalonEdit     │   WebView2 Preview    │
│  Tree    │   (Editor)       │   (HTML renderizado)  │
│          │                  │                       │
├──────────┴──────────────────┴───────────────────────┤
│  Status Bar (vault, línea, columna, palabras)       │
└─────────────────────────────────────────────────────┘
```

El Grid tiene 5 columnas: Explorer (240px) | Splitter | Editor (*) | Splitter | Preview (*).
El explorador se puede ocultar con `Ctrl+\`. Los modos de vista controlan qué columnas son visibles.
El **File Tree muestra una sección por cada vault abierto** (multi-root): no hay "el vault activo", pueden verse y expandirse varias secciones a la vez. `[[wikilinks]]`, el picker de enlaces internos y el grafo de notas siempre resuelven contra el vault **dueño** del archivo del tab activo (`FileService.GetOwningRoot`), nunca contra otro vault abierto en paralelo.

## Funcionalidades

- **Multi-vault (workspace multi-root)**: varios vaults pueden estar abiertos a la vez, cada uno con su propia sección en el explorador y su propio `FileSystemWatcher`. "Administrar vaults" (`VaultsViewModel` / `VaultsWindow`) es un **toggle abrir/cerrar por fila** — no hay un único vault "activo" para seleccionar. El set de vaults abiertos persiste en `AppSettings.OpenVaultPaths` entre sesiones
- **Editor**: AvalonEdit con syntax highlighting, números de línea, word wrap
- **Corrector ortográfico**: Subrayado rojo ondulado bajo palabras mal escritas, usando los diccionarios del SO (Windows `ISpellChecker`). Idioma configurable vía `SpellCheckLanguage` (empty = auto por cultura del SO). Solo en `.md/.markdown/.txt`; saltea bloques de código, frontmatter YAML, URLs, HTML y links
- **Buscar y reemplazar**: Menú `Editar` + `Ctrl+F` (buscar), `Ctrl+H` (reemplazar), `F3` / `Shift+F3` (siguiente / anterior sin abrir el formulario). Formulario flotante NO modal: el editor sigue editable mientras está abierto. Opciones mayúsculas/minúsculas, palabra completa y regex (con grupos `$1` en el reemplazo). Alcance: el archivo del panel con foco — no busca en todo el vault
- **Formato rápido**: Toolbar con Bold, Italic, Code, H1-H3, listas, enlaces, imágenes, bloques de código por lenguaje
- **Vista previa**: WebView2 renderizando HTML con CSS GitHub-flavored
- **Modos de vista**: Solo editor | Editor + Preview | Solo visor (ciclo con botón en toolbar)
- **Tabs**: Múltiples archivos abiertos, Ctrl+Tab/Ctrl+Shift+Tab para navegar, middle-click para cerrar
- **Temas**: Light/Dark con persistencia entre sesiones
- **Archivos soportados**: `.md`, `.html`, `.htm`, `.mermaid`, `.mmd`
- **Mermaid.js**: Renderizado de diagramas (v11.15.0) — mindmap, timeline, flowchart, etc.
- **Drag & Drop**: Arrastrar imágenes al editor las inserta como referencia
- **Pegar imágenes (Ctrl+V)**: Captura de pantalla → Ctrl+V guarda en `attachments/` e inserta `![screenshot](attachments/nombre.png)`
- **Imágenes**: Virtual host mapping (`vault.local`) para resolver rutas relativas sin archivos temporales
- **Auto-save**: Guardado automático de cambios
- **Tablas responsivas**: Layout fluido (max-width 95vw/1600px) con scroll horizontal para tablas grandes

## Gotchas y Decisiones Técnicas

### Multi-Vault (workspace multi-root)
- **Modelo A**: `FileService.VaultRoots` es una **lista ordenada** de todos los vaults abiertos (no un único `VaultRoot`). Cada root tiene su propio `FileSystemWatcher`, creado/dispuesto en `AddRoot`/`RemoveRoot`. `VaultRoot` (singular) sigue existiendo como accessor legacy = el primer/top root, o `null` si no hay ninguno abierto — lo usan callers que aún no migraron a la lista completa.
- **`GetOwningRoot(path)`** resuelve el vault dueño de un path por **longest-prefix match** entre los roots abiertos (soporta roots anidados/superpuestos), y devuelve `null` si el path no está bajo ningún vault abierto. Es el punto central de scoping: wikilinks, el grafo de notas, el picker de enlaces internos (`InsertInternalLink`), pegar/soltar imágenes y el preview (`vault.local` base href) resuelven **siempre** contra `GetOwningRoot(CurrentFilePath)` — nunca contra otro vault que también esté abierto. Si el archivo activo no pertenece a ningún vault abierto (buffer sin guardar), cae al top root (`VaultRoot`) como fallback, igual que el comportamiento legacy de un solo vault.
- **`GraphService.BuildAsync(root)`** construye el grafo para un único root — ningún nodo/edge cruza a otro vault abierto, aunque dos vaults tengan una nota con el mismo nombre. `GraphViewModel` reconstruye el grafo solo cuando el foco cambia de vault (`BuildIfRootChangedAsync`), no en cada cambio de tab dentro del mismo vault.
- **Explorador**: `FileTreeViewModel.RootNodes` tiene una sección por vault abierto (`AddRoot`/`RemoveRoot` agregan/quitan solo esa sección, sin reconstruir las demás). El refresh ante cambios en disco es scopeado por root vía `FileService.VaultChanged` (trae el root que cambió, no reconstruye todo el árbol).
- **"Administrar vaults"** (`VaultsViewModel` + `Views/VaultsWindow.xaml`) es un **toggle abrir/cerrar** por fila del set de vaults conocidos — no existe un único vault "activo" para seleccionar. Cerrar un vault quita su sección del explorador y su watcher, pero **no cierra los tabs ya abiertos** de ese vault (quedan editables). Un vault abierto no se puede eliminar de la lista de conocidos hasta cerrarlo primero.
- **Persistencia**: `AppSettings.OpenVaultPaths` guarda el set de vaults abiertos (orden = orden de apertura, índice 0 = top vault) y se restaura al arrancar con un `FileService.AddRoot` por entrada. Es distinto de `KnownVaultPaths` (nunca se achica al cerrar un vault) y del legacy `LastVaultPath` (un solo path). Hay una **migración de una sola vez**: si `OpenVaultPaths` está vacío y `LastVaultPath` tiene valor, se semilla `OpenVaultPaths` con ese único path; el flag `AppSettings.VaultPathsMigrated` evita que esto se repita en cada arranque (para que un vault que el usuario cerró deliberadamente no "resucite").

### Plugins: progreso y log (SDK 1.3.0)
- **Un plugin tiene DOS canales hacia el usuario, no uno.** `IHostServices.ShowStatus` es para avisos INSTANTÁNEOS (barra de estado, esquina inferior derecha). `IHostServices.BeginProgress(title)` devuelve un `IProgressScope` (`using`) para operaciones LARGAS: barra de ancho completo sobre la barra de estado, con título, paso, porcentaje (o indeterminada) y botón de cancelar. Usar `ShowStatus` para algo que dura minutos produce una app que *parece colgada* — es exactamente lo que pasó con `core.dictado-voz` y 574 MB de descarga.
- **El marshaling al hilo de UI lo hace el HOST**, no el plugin: `PluginProgressCoordinator` recibe el delegate de marshaling en `App.xaml.cs` (`Dispatcher.BeginInvoke`), igual que `StatusSink`/`OpenFileAction`. Un plugin reporta desde cualquier hilo y nunca toca un `Dispatcher`. El coordinador además FUSIONA ráfagas (bandera `_postPending`) y deduplica snapshots idénticos.
- **Concurrencia = pila LIFO**, no cola: se muestra el scope más reciente y la barra anota "+N en segundo plano". Los trabajos largos se anidan por causalidad (transcripción → arranque del motor → descarga del modelo); con FIFO el paso que realmente avanza nunca se vería.
- **Propiedad por plugin vía decorador.** `HostServices` es UNA instancia compartida por todos los plugins, así que no sabe quién llama. `HostPluginContext` envuelve la fachada en `PluginHostServices`, que estampa el id del plugin en cada scope. Sin eso no se puede cumplir el invariante duro: **al desactivar un plugin, `PluginManager.Deactivate` cierra y cancela TODOS sus scopes** (antes de `OnDeactivatedAsync`, para que el trabajo en vuelo reciba la señal de corte).
- **No hay timeout para un scope olvidado** — a propósito. Las dos salidas son: el botón de cancelar de DOS TIEMPOS (1ª = cancela el token y pasa a decir «Descartar»; 2ª = saca la barra aunque el plugin no coopere) y el barrido al desactivar.
- **`context.Log` ya no cae en un pozo.** Además de `Debug.WriteLine` (que desaparece entero en Release por `[Conditional("DEBUG")]`), va a `%AppData%/MarkdownVault/logs/plugins.log` vía `FilePluginLogSink`: cola acotada con `DropWrite` (nunca bloquea ni crece sin techo), un hilo escritor que drena en lotes, rotación a 1 MB con un solo respaldo (`plugins.1.log`), y auto-apagado tras 5 fallos de escritura seguidos. **Nunca lanza hacia el plugin.** El sumidero se inyecta en `App.xaml.cs`; el default de `PluginManager` es `NullPluginLogSink` para que las pruebas no escriban en el `%AppData%` del usuario.
- **Agregar un miembro a `IHostServices` es aditivo para quien la CONSUME y rompiente para quien la IMPLEMENTA.** Los únicos implementadores del repo son `HostServices` y el doble `FakeHost` de tests; ningún plugin la implementa. Por eso la subida es de *minor*.

### Plugins: listas editables (SDK 1.4.0)
- **`IPluginContext.AddListSetting(PluginListSetting)`** deja que un plugin declare una lista editable (clave, o clave+valor si `ValueLabel` no es `null`) sin definir ninguna `Window` propia. El HOST la dibuja entera (`Views/PluginsWindow.xaml` + `ViewModels/PluginListSettingViewModel.cs`): alta, baja, edición, filtro, aviso de duplicados/vacíos y guardado explícito. El plugin solo aporta `Load`/`Save`/`Describe`. Es la salida a la limitación de WPF que clava el `AssemblyLoadContext` (ver `docs/plugins/GUIA-PLUGINS.md` §9): declarar una `Window` propia pierde la descarga en caliente; declarar una lista, no.
- **Normalización y duplicados son responsabilidad del host** (`Services/Plugins/PluginListRules.cs`, lógica pura sin WPF): recorte de espacios, descarte de claves vacías, deduplicación `OrdinalIgnoreCase` (los acentos sí distinguen). `Save` recibe la lista YA normalizada; el plugin no tiene que volver a limpiarla.
- **`core.dictado-voz` es el único consumidor real hoy** (el glosario técnico, ver `plugins/DictadoVoz/DictadoVozPlugin.cs` + `TechnicalGlossary.cs`). El diccionario de pronunciación de `core.lector-documentos` es el caso pensado para `ValueLabel` (segunda columna) pero **todavía no lo adoptó**.

### Buscar / Reemplazar
- **El `SearchPanel` de AvalonEdit NO tiene reemplazo.** Verificado sobre el ensamblado de `Quicker.AvalonEdit` 6.3.1: `ICSharpCode.AvalonEdit.Search.SearchPanel` solo expone `FindNext`/`FindPrevious`/`Open`/`Close` y las tres opciones (`MatchCase`, `WholeWords`, `UseRegex`). Además nunca se llamó a `SearchPanel.Install(...)`, así que tampoco estaba el Ctrl+F integrado. Por eso hay motor propio (`Services/TextSearch.cs`) en vez de envolver el del fork.
- **El motor es C# puro sobre un `string`** — recibe texto y devuelve offsets; no conoce AvalonEdit ni WPF. Toda la lógica que importa (bordes de palabra, wrap, expansión de grupos, patrones de largo cero) se testea headless en `TextSearchTests`. La vista solo traduce offsets a selección y scroll.
- **"Palabra completa" usa lookarounds `(?<!\w)…(?!\w)`, NO `\b`.** `\b` es un borde ENTRE un carácter de palabra y uno que no lo es, así que un patrón que empieza o termina en símbolo (`->`, `(x)`) nunca coincidiría con `\b` a los costados. Es un caso real en Markdown técnico.
- **Un patrón de largo cero cuelga el recorrido si no se saltea explícitamente.** `a*` o `^` coinciden con la cadena vacía en CADA posición: `TextSearch.Enumerate` avanza un carácter a mano cuando `m.Length == 0`. Además toda regex se compila con un `matchTimeout` de 2s — sin él, un `(a+)+$` escrito por el usuario congela el hilo de UI.
- **El destino es el panel con FOCO, no "el editor".** Con `IsSplit` hay dos `EditorView`. Por eso `FindReplaceViewModel` no guarda un `IFindReplaceTarget` sino un `Func<IFindReplaceTarget?>` que se vuelve a consultar en CADA operación: cambiar de panel o de pestaña con el formulario abierto no lo deja apuntando al documento equivocado.
- **La ventana NO es modal, a propósito.** Es una ventana *owned* por `MainWindow`: flota siempre encima pero el editor sigue vivo (se puede hacer clic en el texto y corregir a mano sin cerrarla). Un `ShowDialog()` bloquearía el documento justo cuando el usuario acaba de saltar a él.
- **Cerrar el formulario lo ESCONDE, no lo destruye** — así sobreviven el patrón y las opciones, y `F3` repite la última búsqueda con la ventana cerrada. Consecuencia obligatoria: `MainWindow.Window_Closing` llama a `FindReplaceWindow.ForceClose()`. El `ShutdownMode` por defecto es `OnLastWindowClose`, y una ventana escondida que cancela su propio `Closing` dejaría el proceso vivo sin nada en pantalla.
- **"Reemplazar todo" se aplica de la ÚLTIMA a la primera** dentro de un `Document.BeginUpdate()/EndUpdate()`. Los offsets se calculan contra el texto original; aplicar de adelante hacia atrás los correría a todos. El `BeginUpdate` además agrupa las N ediciones en un solo Ctrl+Z.
- **"Reemplazar" (singular) solo toca el documento si la selección ES exactamente una coincidencia** (`TextSearch.ReplacementAt`). Sin esa guarda, el botón pisaría cualquier texto que el usuario haya seleccionado a mano. Si no calza, solo posiciona en la siguiente — el segundo clic ya reemplaza.
- **`$` en el reemplazo es literal salvo en modo regex.** `Match.Result` expande `$1`/`$&`; en modo texto plano se inserta el reemplazo tal cual, para que escribir `US$1` no se convierta en un grupo capturado.
- **El `CheckBox` necesita `Foreground` explícito.** No hay estilo implícito de `CheckBox` en los diccionarios de tema, así que sin setearlo el texto sale negro sobre fondo oscuro.

### WPF ToolBar
- WPF ToolBar aplica sus propios estilos implícitos (`ToolBar.ButtonStyleKey`) a los hijos. Para que los botones respeten el tema oscuro, hay que mapear explícitamente el style key dentro de `ToolBar.Resources`.
- `TextElement.Foreground` debe setearse en el ToolBar Y usar `TemplateBinding` en el ContentPresenter.

### WebView2 Preview
- El preview usa `NavigateToString()` — no navega a URLs reales.
- Las imágenes locales se resuelven via virtual host mapping: `vault.local` → carpeta del vault.
- El CSS está **inlineado** en `MarkdownService.GithubCss` (no se hacen requests HTTP para estilos).
- **Gotcha de lanzamiento**: `EnsureCoreWebView2Async()` no fija `UserDataFolder`, así que WebView2 crea su carpeta de datos **al lado del ejecutable**. Si se lanza con `dotnet bin/.../MarkdownVault.dll`, el proceso es `dotnet.exe` (en `Program Files`, read-only) → WebView2 falla en silencio → **preview en blanco**. Para probar/verificar SIEMPRE correr el `.exe` real, NO `dotnet <dll>`.

### Temas
- Se cambian dinámicamente reemplazando el `ResourceDictionary` en `Application.Resources`.
- La persistencia se hace via `SettingsService` → se lee `IsDarkTheme` al startup y se llama `ApplyTheme()` explícitamente (sin esto, el tema no se aplica aunque la config esté guardada).

### Tablas en Preview
- CSS usa `max-width: min(95vw, 1600px)` en lugar de un ancho fijo de 980px.
- Las tablas se envuelven en un `<div class="table-wrapper">` via JavaScript al cargar el DOM, dando scroll horizontal independiente.
- Scrollbar estilizado (6px, themed para dark mode).

### Corrector Ortográfico
- **AvalonEdit NO soporta `SpellCheck.IsEnabled` de WPF** (eso es solo para `TextBox`/`RichTextBox`). Hay que implementarlo a mano: motor + pintado + (futuro) sugerencias.
- **El pintado usa `DocumentColorizingTransformer`, NO `IBackgroundRenderer`.** En este fork (`Quicker.AvalonEdit` 6.3.1), el `IBackgroundRenderer.Draw` vive en `OnRender` y **no se re-dispara** con `Redraw()`, `InvalidateVisual()` ni `InvalidateLayer()`. El colorizer corre en la construcción de líneas visuales, que SÍ se reconstruyen al tipear/scrollear — por eso se re-aplica solo, sin redibujo manual.
- **El idioma NO sale de `CultureInfo.CurrentUICulture`** — esa es la UI del SO, no el idioma que se escribe (ej: Windows en inglés pero se escribe en español). Se usa el setting explícito `AppSettings.SpellCheckLanguage` (`"es"`, `"es-ES"` o vacío = auto). Un código de dos letras se mapea a su variante regional (prefiere `{lang}-{LANG}`, ej. `es → es-ES`).
- El subrayado ondulado es un `TextDecoration` con `Pen` de `DrawingBrush` tileado (onda triangular repetida).
- El corrector cachea por texto de línea y saltea fenced code / frontmatter (skip-set recacheado cuando cambia el `TextLength` del documento).
- **Interop COM**: el orden de los métodos en las interfaces (`ISpellCheckerFactory`, `ISpellChecker`, etc.) DEBE calcar el vtable de `Spellcheck.h`; solo se declaran los métodos hasta el último usado.

## Cómo Compilar

```bash
dotnet build MarkdownVault.sln
```

## Cómo Ejecutar

```bash
dotnet run --project MarkdownVault.csproj
```

> **Requisito**: WebView2 Runtime debe estar instalado (viene con Windows 11, en Windows 10 puede requerir instalación manual).

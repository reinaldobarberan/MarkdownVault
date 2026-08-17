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
│   └── GraphViewModel.cs         # VM del grafo de notas, scopeado al vault del tab activo
├── Views/
│   ├── MainWindow.xaml / .cs     # Ventana principal (layout, WebView2, tabs)
│   ├── EditorView.xaml / .cs     # Editor AvalonEdit + toolbar de formato
│   ├── FileTreeView.xaml / .cs   # Árbol lateral del vault
│   └── InputDialog.xaml / .cs    # Diálogo para input de usuario
├── Services/
│   ├── FileService.cs            # I/O de archivos, escaneo; mantiene VaultRoots (multi-root)
│   ├── GraphService.cs           # Grafo de notas/enlaces, scopeado a un vault root por vez
│   ├── MarkdownService.cs        # Markdown → HTML (Markdig) + CSS + Mermaid
│   ├── SettingsService.cs        # Persistencia de configuración
│   ├── ISpellCheckService.cs     # Contrato del corrector + record SpellError
│   └── WindowsSpellCheckService.cs # Motor COM Windows ISpellChecker
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

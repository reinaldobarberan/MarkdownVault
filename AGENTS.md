# MarkdownVault — Contexto del Proyecto

## Descripción General

MarkdownVault es un **editor de Markdown tipo Obsidian** para escritorio, construido con WPF y .NET 8. Permite gestionar un "vault" (carpeta) de archivos Markdown, HTML y Mermaid con vista previa en tiempo real, temas claro/oscuro, y edición con formato enriquecido.

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
│   ├── AppSettings.cs            # Configuración persistida (tema, fuente)
│   ├── OpenTab.cs                # Modelo de pestaña abierta
│   ├── VaultFile.cs              # Nodo de archivo/directorio en el vault
│   └── ViewMode.cs               # Enum: EditorOnly | EditAndPreview | ViewerOnly
├── ViewModels/
│   ├── MainViewModel.cs          # VM principal (vault, tema, fuente, explorador)
│   ├── EditorViewModel.cs        # VM del editor (tabs, contenido, preview, formato)
│   └── FileTreeViewModel.cs      # VM del árbol de archivos
├── Views/
│   ├── MainWindow.xaml / .cs     # Ventana principal (layout, WebView2, tabs)
│   ├── EditorView.xaml / .cs     # Editor AvalonEdit + toolbar de formato
│   ├── FileTreeView.xaml / .cs   # Árbol lateral del vault
│   └── InputDialog.xaml / .cs    # Diálogo para input de usuario
├── Services/
│   ├── FileService.cs            # I/O de archivos, escaneo del vault
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

- **`FileService`** — Lectura/escritura de archivos, escaneo recursivo del vault, gestión de rutas.
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

## Funcionalidades

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

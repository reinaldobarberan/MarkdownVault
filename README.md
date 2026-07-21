# MarkdownVault

Editor de Markdown de escritorio al estilo **Obsidian**, construido con **WPF y .NET 8**. Gestiona un *vault* (carpeta) de notas en Markdown, HTML y Mermaid, con vista previa en tiempo real, enlaces internos entre notas y una vista de grafo para explorar sus relaciones.

## Características

- **Editor** con AvalonEdit: resaltado de sintaxis, números de línea, ajuste de línea y barra de formato rápido (negrita, cursiva, títulos, listas, enlaces, imágenes, bloques de código).
- **Vista previa** en tiempo real (WebView2) con CSS estilo GitHub, tablas responsivas y temas claro/oscuro.
- **Vista de grafo** tipo Obsidian: cada nota es un nodo y cada enlace interno una arista, con simulación dirigida por fuerzas, filtros y zoom.
- **Enlaces internos** entre notas: wikilinks `[[nota]]` y enlaces Markdown `[texto](nota.md)`, con resolución en todo el vault y navegación con clic.
- **Mermaid.js** (v11.15): diagramas de flujo, secuencia, clases, estados, Gantt, pie, mindmap y timeline, con un menú de ejemplos listos para insertar.
- **Pestañas** de archivos, explorador lateral con búsqueda, y modos de vista (solo editor / editor + preview / solo visor).
- **Temas** claro/oscuro (estilo VS Code) con persistencia, incluida la barra de título nativa.
- **Imágenes**: arrastrar y soltar, y pegar capturas de pantalla (Ctrl+V) directo a `attachments/`.
- **Auto-guardado** y exportación de la vista previa a PNG.

## Stack

| Componente        | Tecnología                  |
| ----------------- | --------------------------- |
| Framework         | .NET 8 (WPF) — `net8.0-windows` |
| MVVM              | CommunityToolkit.Mvvm       |
| Editor de código  | AvalonEdit                  |
| Parser Markdown   | Markdig                     |
| Vista previa      | Microsoft.Web.WebView2      |
| Diagramas         | Mermaid.js                  |

## Arquitectura

Patrón **MVVM** con inyección manual de servicios en `App.xaml.cs`.

```
MarkdownVault/
├── Models/       # AppSettings, OpenTab, VaultFile, GraphNode…
├── ViewModels/   # MainViewModel, EditorViewModel, FileTreeViewModel, GraphViewModel
├── Views/        # MainWindow, EditorView, FileTreeView, GraphView, SplashWindow…
├── Services/     # FileService, MarkdownService, GraphService, SettingsService
└── Resources/    # Temas (DarkTheme / LightTheme)
```

## Requisitos

- **.NET 8 SDK**
- **WebView2 Runtime** (incluido en Windows 11; en Windows 10 puede requerir instalación manual)

## Cómo compilar y ejecutar

```bash
dotnet build MarkdownVault.sln
dotnet run --project MarkdownVault.csproj
```

## Uso rápido

1. **Archivo → Abrir vault…** y elegí una carpeta con tus notas.
2. Escribí en Markdown; la vista previa se actualiza en vivo.
3. Enlazá notas con `[[nombre]]` o desde la barra (*Insertar enlace interno*).
4. Insertá diagramas desde el menú **Mermaid ▾**.
5. Abrí la **vista de grafo** para ver cómo se conectan tus notas.

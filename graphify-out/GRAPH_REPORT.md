# Graph Report - MarkdownVault  (2026-08-21)

## Corpus Check
- 234 files · ~243,064 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3200 nodes · 6589 edges · 173 communities (154 shown, 19 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 439 edges (avg confidence: 0.77)
- Token cost: 463,737 input · 0 output

## Community Hubs (Navigation)
- Plugin List Normalization Rules
- Lector Documentos Plugin Core
- RMS Silence Detection
- Speech Text Extraction
- WPF Dialog Service
- Main View Model
- Editor Commands And Tabs
- File Tree Commands
- Plugin List Setting UI
- Markdig Extension Plugins
- Plugin Storage Sandbox
- Eisenhower Task Tests
- Vaults View Model Tests
- WPF UI Primitives
- Plugin Contribution Model
- File Tree View Model
- Eisenhower Task Store
- Progress And Timer State
- Vault File Service
- Plugin Log Sink
- Editor Toolbar Commands
- Microphone Audio Capture
- Graph Canvas Rendering
- Eisenhower Grid Render Tests
- Window Event Handling
- Dictado Settings And Rendering
- File Audio Source
- Pinned Editor Context Tests
- Graph View Screenshot
- Glossary Capacity Tests
- App Startup Wiring
- Dictado Voz Plugin
- View Mode Events
- Supported File Types
- Plugin Progress Coordinator
- Graph View Controls
- Windows Spell Check COM
- Plugin Metadata And Descriptors
- Lector Documentos Plugin Shell
- Find Replace View Model
- Text Search Tests
- Core Namespaces And Helpers
- Dictado Voz Test Suite
- Diff Service
- Compare Files Command Tests
- Transcript Formatter Tests
- Workbench Commands
- Main Window Screenshot
- Audio Playback Pipeline
- Dirty Tab Scanner Tests
- Whisper Process Job Object
- Internal Link Picker Dialog
- Graph View Model
- App Settings Persistence
- Open Tab Model
- Plugin Host Services
- File Icon Converter
- Whisper Model Catalog
- Progress Coordinator Tests
- Plugin Editor Context
- Text Search Engine
- Eisenhower Plugin Shell
- Whisper Service
- Unsaved Changes Report Tests
- Dictado Glossary Documentation
- Dictation Session
- Plugin Row View Model
- Find Replace UI Bindings
- Self Write Detection Tests
- Disposal Tests
- Small Markdig Plugins
- Plugin Load Context Tests
- Diff Merge
- Workbench Invariant Tests
- Markdown Rendering Service
- Diff Merge Tests
- Eisenhower Plugin Documentation
- Dark Theme Resources
- Diff HTML Renderer
- Diff HTML Renderer Tests
- File Tree Reconciliation Tests
- Vault List UI Bindings
- Spell Check Service Contract
- Graph Data Model
- Plugin List Settings API
- Image Paste And Assets
- Plugin Manager Tests
- Project Context Documentation
- Split View Layout Bindings
- Bool To Icon Converter
- Spell Check Word Resolver
- Piper TTS Engine
- Plugin Context Contract
- Plugin Registry Tests
- Main Window Shell
- Editor Text Colorizing
- Whisper HTTP Client
- Model Catalog Tests
- Assorted Unit Tests
- Progress Scope Contract
- Export File Naming
- Input Dialog
- Agent Skill Registry Docs
- Ordered Inserter Tests
- Transcript Golden Tests
- Async Chunk Draining
- Tab Close Operations
- View Model Format Tests
- About Window
- Application Bootstrap
- Diff Window
- Microphone Error Mapping
- Multi Vault Design Rationale
- Eisenhower Project Files
- Host Services Open File Tests
- Title Bar Theming
- Transcript Formatter
- Dictado Voz Project Files
- Callouts Mermaid Project Files
- Microphone Error Tests
- Find Replace Window
- Multi Vault Service Docs
- Find Replace Design Rationale
- Solution And Plugin Projects
- Logo Brand Identity
- Payments Meeting Transcript Fixture
- Technical Glossary
- Lector Documentos Project Files
- Light Theme Resources
- Host Services Implementation
- Copy Button Plugin Tests
- Markdown Render Tests
- Core Test Project Files
- Vault Migration Tests
- Compare View Contract
- Settings Persistence Rationale
- Whisper Model Download Docs
- Plugin Assembly Load Context
- Find Routed Commands
- Fake Storage Test Double
- App Icon Brand Identity
- Main Project Dependencies
- Multi Vault Model Decision
- Multi Root Workspace Spec
- WAV Header Tests
- Syntax Highlighter
- Vault Scoped Resolution Spec
- Plugin Manifest Tests
- Spell Check Design Docs
- Mermaid Plugin
- Plugin Unload Rationale
- Markdown Prose Mask
- Splash Window
- Plugin Toolbar Template Selector
- SDD Change Cycle Docs
- Multi Vault Exploration
- Multi Vault Proposal
- Percent Normalization Tests
- Graph Visibility Bindings
- WPF Test Fixture Plugin
- Dictado Config Documentation
- Spelling Error COM Interfaces
- File Tree Views
- Tab Strip Views
- Copy Button Project File
- Find Command Definitions
- App Paths Helper
- Preview WebView Host
- WPF ToolBar Theming Gotcha

## God Nodes (most connected - your core abstractions)
1. `MainViewModel` - 115 edges
2. `EditorGroupViewModel` - 86 edges
3. `EisenhowerTests` - 84 edges
4. `Window` - 71 edges
5. `MarkdownVault.Services` - 55 edges
6. `MarkdownVault.PluginSdk` - 49 edges
7. `FileService` - 49 edges
8. `Window` - 49 edges
9. `SpeechTextExtractorTests` - 44 edges
10. `PluginRegistry` - 43 edges

## Surprising Connections (you probably didn't know these)
- `GetOwningRoot (longest-prefix vault scoping)` --semantically_similar_to--> `PathConfinement (resolución léxica confinada)`  [INFERRED] [semantically similar]
  AGENTS.md → docs/plugins/GUIA-PLUGINS.md
- `TextSearch (motor puro de Buscar/Reemplazar)` --semantically_similar_to--> `IEditorContext`  [INFERRED] [semantically similar]
  AGENTS.md → docs/plugins/GUIA-PLUGINS.md
- `UserControl` --references--> `ForceLink`  [INFERRED]
  Views/GraphView.xaml → ViewModels/GraphViewModel.cs
- `UserControl` --references--> `Search`  [INFERRED]
  Views/GraphView.xaml → ViewModels/GraphViewModel.cs
- `UserControl` --references--> `ShowLabels`  [INFERRED]
  Views/GraphView.xaml → ViewModels/GraphViewModel.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Flujo de desactivación en caliente de un plugin** — docs_plugins_guia_plugins_pluginmanager, agents_pluginprogresscoordinator, agents_pluginhostservices, docs_plugins_guia_plugins_pluginregistry, docs_plugins_guia_plugins_pluginloadcontext, docs_plugins_guia_plugins_hot_unload [EXTRACTED 1.00]
- **Plugins de primera parte que implementan IPlugin** — docs_plugins_plugins_mermaid, docs_plugins_plugins_highlight, docs_plugins_plugins_copybutton, docs_plugins_plugins_callouts, docs_plugins_plugins_eisenhower, docs_plugins_plugins_lector_documentos, docs_plugins_plugins_dictado_voz, docs_plugins_guia_plugins_iplugin [EXTRACTED 1.00]
- **Scoping del workspace multi-root** — agents_fileservice, agents_vaultroots, agents_getowningroot, agents_graphservice, agents_filetreeviewmodel, agents_openvaultpaths, agents_vaultsviewmodel [EXTRACTED 1.00]
- **multi-vault SDD artifact chain (explore to archive)** — openspec_changes_archive_2026_08_14_multi_vault_exploration_multi_vault_exploration, openspec_changes_archive_2026_08_14_multi_vault_proposal_multi_vault_proposal, openspec_changes_archive_2026_08_14_multi_vault_design_multi_vault_design, openspec_changes_archive_2026_08_14_multi_vault_tasks_multi_vault_tasks, openspec_changes_archive_2026_08_14_multi_vault_verify_report_multi_vault_verify_report, openspec_changes_archive_2026_08_14_multi_vault_archive_report_multi_vault_change [EXTRACTED 1.00]
- **Owning-root resolution drives every vault-scoped call site** — openspec_changes_archive_2026_08_14_multi_vault_design_getowningroot, openspec_specs_vault_scoped_resolution_spec_owning_root_resolution, openspec_specs_vault_scoped_resolution_spec_vault_scoped_wikilink_resolution, openspec_specs_vault_scoped_resolution_spec_vault_scoped_image_paste, openspec_specs_vault_scoped_resolution_spec_preview_host_scoped_to_focused_tab, openspec_specs_vault_scoped_resolution_spec_graph_scoped_to_focused_vault [EXTRACTED 1.00]
- **Nomina file processing flow (pendientes to procesados)** — plugins_dictadovoz_tests_fixtures_dictado_transcript_golden_pipeline_de_nominas, plugins_dictadovoz_tests_fixtures_dictado_transcript_golden_motor_de_recurrencia, plugins_dictadovoz_tests_fixtures_dictado_transcript_golden_motor_de_abonos, plugins_dictadovoz_tests_fixtures_dictado_transcript_golden_fpay [EXTRACTED 1.00]
- **Physics tuning trio drives the force simulation** — image_grafos_fuerza_central, image_grafos_repulsion, image_grafos_enlaces_force, image_grafos_force_directed_layout [INFERRED 0.85]
- **Three-pane workspace: explorer, editor, graph** — image_grafos_file_explorer_sidebar, image_grafos_editor_toolbar, image_grafos_graph_view, image_grafos_layout_mode_buttons [INFERRED 0.85]
- **Vault notes rendered as graph nodes (7 notas, 1 enlace)** — image_grafos_node_archivo, image_grafos_node_code, image_grafos_node_mejoras_agenda_virtual, image_grafos_node_modificaciones_pendientes, image_grafos_node_mejoras_motor_asignacion, image_grafos_node_mejoras_cuarto_paquete, image_grafos_node_diagrama, image_grafos_graph_stats_bar [EXTRACTED 1.00]
- **Edit-to-Preview Rendering Pipeline** — image_principal_markdown_editor_pane, image_principal_markdig, image_principal_webview2, image_principal_live_preview_pane, image_principal_mermaid_js [INFERRED 0.85]
- **Obsidian-style Workspace Shell** — image_principal_file_tree_sidebar, image_principal_markdown_editor_pane, image_principal_live_preview_pane, image_principal_file_tabs, image_principal_view_mode_toggles, image_principal_status_bar [EXTRACTED 1.00]
- **WPF .NET 8 MVVM Application Stack** — image_principal_wpf_dotnet8, image_principal_communitytoolkit_mvvm, image_principal_mvvm_architecture, image_principal_services_layer, image_principal_avalonedit [EXTRACTED 1.00]
- **Visual Narrative: Markdown Glyph Locked Inside a Vault** — image_logo_hash_glyph_motif, image_logo_vault_enclosure_motif, image_logo_padlock_motif, image_logo_secure_markdown_storage_product [INFERRED 0.85]
- **Brand System: Squircle Icon, Blue Gradient, Wordmark** — image_logo_squircle_app_icon_format, image_logo_blue_gradient_palette, image_logo_wordmark_markdownvault, image_logo_markdownvault_brand [INFERRED 0.75]
- **MarkdownVault Visual Identity System** — image_logo_app_hash_markdown_motif, image_logo_app_padlock_security_motif, image_logo_app_wordmark_typography, image_logo_app_blue_gradient_palette, image_logo_app_squircle_icon_format [INFERRED 0.85]
- **Secure Markdown Notes Product Promise** — image_logo_app_hash_markdown_motif, image_logo_app_padlock_security_motif, image_logo_app_vault_metaphor, image_logo_app_markdownvault_brand [INFERRED 0.85]

## Communities (173 total, 19 thin omitted)

### Community 0 - "Plugin List Normalization Rules"
Cohesion: 0.08
Nodes (18): IEnumerable, IReadOnlyList, ListEntryProblem, PluginListRules, Fact, InlineData, IReadOnlyList, Theory (+10 more)

### Community 1 - "Lector Documentos Plugin Core"
Cohesion: 0.06
Nodes (18): MarkdownVault.Plugin.LectorDocumentos, MarkdownVault.Plugin.LectorDocumentos.Tests, KeyValuePair, Action, Dictionary, IEnumerable, IReadOnlyDictionary, JsonSerializerOptions (+10 more)

### Community 2 - "RMS Silence Detection"
Cohesion: 0.11
Nodes (16): Detector, Events, Index, bool, double, int, RmsSilenceDetector, SilenceDetectorOptions (+8 more)

### Community 3 - "Speech Text Extraction"
Cohesion: 0.08
Nodes (11): IEnumerable, int, IReadOnlyList, List, Regex, StringBuilder, SpeechTextExtractor, Fact (+3 more)

### Community 4 - "WPF Dialog Service"
Cohesion: 0.07
Nodes (24): ConfirmResult, IReadOnlyList, WpfDialogService, Action, Fact, string, Task, EditorGroupViewModelTests (+16 more)

### Community 5 - "Main View Model"
Cohesion: 0.04
Nodes (41): Action, bool, double, Func, IReadOnlyList, ObservableCollection, string, ViewMode (+33 more)

### Community 6 - "Editor Commands And Tabs"
Cohesion: 0.04
Nodes (50): CancelProgressCommand, CompareFilesCommand, DecreaseFontSizeCommand, FocusedGroup.ActiveTab, FocusedGroup.CloseTabCommand, FocusedGroup.CurrentColumn, FocusedGroup.CurrentLine, FocusedGroup.GoBackCommand (+42 more)

### Community 7 - "File Tree Commands"
Cohesion: 0.05
Nodes (43): CreateFileCommand, CreateFolderCommand, DataContext, DataContext.CloseTabCommand, DeleteNodeCommand, DisplayName, HasFile, IsActive (+35 more)

### Community 8 - "Plugin List Setting UI"
Cohesion: 0.04
Nodes (47): AddNewCommand, Author, CanSave, DescribeText, Description, DiscardCommand, EmptyText, Enabled (+39 more)

### Community 9 - "Markdig Extension Plugins"
Cohesion: 0.06
Nodes (24): MarkdownVault.Plugin.Eisenhower.Tests, MarkdownVault.Plugin.Eisenhower, MarkdownVault.Plugin.Callouts, IMarkdownExtension, LiteralInline, MarkdownDocument, IMarkdownRenderer, List (+16 more)

### Community 10 - "Plugin Storage Sandbox"
Cohesion: 0.12
Nodes (11): PathConfinement, Task, UTF8Encoding, PluginStorage, Fact, string, PathConfinementTests, Fact (+3 more)

### Community 11 - "Eisenhower Task Tests"
Cohesion: 0.11
Nodes (3): InlineData, Theory, EisenhowerTests

### Community 12 - "Vaults View Model Tests"
Cohesion: 0.09
Nodes (13): Fact, int, List, string, Harness, VaultsViewModelTests, Action, Func (+5 more)

### Community 13 - "WPF UI Primitives"
Cohesion: 0.11
Nodes (16): Border, Button, CheckBox, Grid, bool, Brush, Color, FrameworkElement (+8 more)

### Community 14 - "Plugin Contribution Model"
Cohesion: 0.09
Nodes (20): IReadOnlyList, AssetKind, AssetPlacement, AssetSource, IMarkdownContribution, PluginCommandGroup, PluginPanel, PreviewAsset (+12 more)

### Community 15 - "File Tree View Model"
Cohesion: 0.12
Nodes (10): ObservableObject, bool, IList, ObservableCollection, RelayCommand, string, VaultFile, FileTreeViewModel (+2 more)

### Community 16 - "Eisenhower Task Store"
Cohesion: 0.10
Nodes (18): DateTimeOffset, bool, Guid, int, IReadOnlyList, JsonSerializerOptions, List, string (+10 more)

### Community 17 - "Progress And Timer State"
Cohesion: 0.08
Nodes (10): DispatcherTimer, Stack, Action, bool, Func, int, ObservableCollection, RelayCommand (+2 more)

### Community 18 - "Vault File Service"
Cohesion: 0.12
Nodes (13): FileSystemWatcher, Dictionary, IReadOnlyList, List, object, TimeSpan, FileService, Fact (+5 more)

### Community 19 - "Plugin Log Sink"
Cohesion: 0.10
Nodes (18): Encoding, bool, Channel, int, long, string, Task, UTF8Encoding (+10 more)

### Community 20 - "Editor Toolbar Commands"
Cohesion: 0.07
Nodes (30): CanGoBack, Command, DataContext.HasOpenDocument, GoBackCommand, GoBackFileName, HasIcon, HasOpenDocument, Icon (+22 more)

### Community 21 - "Microphone Audio Capture"
Cohesion: 0.08
Nodes (19): BufferedWaveProvider, float, ISampleProvider, MMDevice, MMDeviceEnumerator, Action, bool, Channel (+11 more)

### Community 22 - "Graph Canvas Rendering"
Cohesion: 0.09
Nodes (15): DrawingContext, FrameworkElement, MouseEventArgs, MouseWheelEventArgs, Pen, Point, Typeface, bool (+7 more)

### Community 24 - "Window Event Handling"
Cohesion: 0.09
Nodes (9): bool, DependencyPropertyChangedEventArgs, EventArgs, IReadOnlyList, KeyEventArgs, MouseButtonEventArgs, PropertyChangedEventArgs, Regex (+1 more)

### Community 25 - "Dictado Settings And Rendering"
Cohesion: 0.10
Nodes (17): CodeBlock, CodeBlockRenderer, HtmlObjectRenderer, HtmlRenderer, Action, JsonSerializerOptions, string, DictadoSettings (+9 more)

### Community 26 - "File Audio Source"
Cohesion: 0.09
Nodes (22): IAsyncDisposable, SequencedChunk, CancellationToken, IAsyncEnumerable, string, ValueTask, FileAudioSource, CancellationToken (+14 more)

### Community 27 - "Pinned Editor Context Tests"
Cohesion: 0.25
Nodes (8): Fact, InlineData, List, string, Task, Theory, LiveEditorSpy, PinnedEditorContextTests

### Community 28 - "Graph View Screenshot"
Cohesion: 0.10
Nodes (30): Grafos.png - MarkdownVault Graph View Screenshot, Active Note Highlight (orange node = archivo.md), Bidirectional Wikilink Knowledge Base, Dark Theme UI, Markdown Editor Toolbar (B/I/H1-H3, Codigo, Mermaid), Enlaces slider (link force, 1.0), File Explorer Sidebar (Documento vault tree), Filtros Panel (node search + local graph toggle) (+22 more)

### Community 29 - "Glossary Capacity Tests"
Cohesion: 0.14
Nodes (6): Action, IEnumerable, Fact, TechnicalGlossaryCapacityTests, Fact, TechnicalGlossaryTests

### Community 30 - "App Startup Wiring"
Cohesion: 0.11
Nodes (8): MarkdownVault.ViewModels, MarkdownVault.Models, MarkdownVault.Views, FileSystemEventArgs, ViewMode, VaultChange, Fact, OpenTabTests

### Community 31 - "Dictado Voz Plugin"
Cohesion: 0.15
Nodes (7): Action, CancellationTokenSource, Dispatcher, IReadOnlyList, string, Task, DictadoVozPlugin

### Community 32 - "View Mode Events"
Cohesion: 0.13
Nodes (10): bool, CancelEventArgs, DependencyPropertyChangedEventArgs, double, PropertyChangedEventArgs, RoutedEventArgs, string, Task (+2 more)

### Community 33 - "Supported File Types"
Cohesion: 0.11
Nodes (10): IReadOnlySet, IReadOnlyDictionary, SupportedExtensions, List, VaultFile, Fact, InlineData, string (+2 more)

### Community 34 - "Plugin Progress Coordinator"
Cohesion: 0.12
Nodes (12): Action, bool, CancellationToken, CancellationTokenSource, double, int, List, object (+4 more)

### Community 35 - "Graph View Controls"
Cohesion: 0.09
Nodes (22): ForceCenter, ForceLink, ForceRepel, LinkCount, LocalGraph, NoteCount, Search, ShowLabels (+14 more)

### Community 36 - "Windows Spell Check COM"
Cohesion: 0.13
Nodes (12): IEnumSpellingError, IEnumString, ISpellChecker, ISpellCheckerFactory, MarshalAs, IReadOnlyList, List, ISpellChecker (+4 more)

### Community 37 - "Plugin Metadata And Descriptors"
Cohesion: 0.13
Nodes (13): string, PluginMetadata, SdkInfo, PluginDescriptor, PluginState, Dictionary, IEnumerable, IReadOnlyList (+5 more)

### Community 38 - "Lector Documentos Plugin Shell"
Cohesion: 0.16
Nodes (7): Action, Dispatcher, IReadOnlyList, string, Task, LectorDocumentosPlugin, PluginListEntry

### Community 39 - "Find Replace View Model"
Cohesion: 0.15
Nodes (7): IFindReplaceTarget, bool, Func, Regex, RelayCommand, string, FindReplaceViewModel

### Community 40 - "Text Search Tests"
Cohesion: 0.22
Nodes (3): Fact, Regex, TextSearchTests

### Community 41 - "Core Namespaces And Helpers"
Cohesion: 0.14
Nodes (3): MarkdownVault.Helpers, MarkdownVault.Tests, MarkdownVault.Services

### Community 42 - "Dictado Voz Test Suite"
Cohesion: 0.12
Nodes (9): MarkdownVault.Plugin.DictadoVoz, MarkdownVault.Plugin.DictadoVoz.Tests, Fact, InlineData, string, Theory, WhisperOutputFilterTests, string (+1 more)

### Community 43 - "Diff Service"
Cohesion: 0.14
Nodes (16): op, IEqualityComparer, leftIndex, Op, rightIndex, bool, IReadOnlyList, left (+8 more)

### Community 44 - "Compare Files Command Tests"
Cohesion: 0.21
Nodes (7): FakeCompareView, Fact, int, string, Task, CompareFilesCommandTests, FakeCompareView

### Community 45 - "Transcript Formatter Tests"
Cohesion: 0.27
Nodes (3): Fact, TranscriptFormatterTests, IReadOnlyList

### Community 47 - "Main Window Screenshot"
Cohesion: 0.11
Nodes (24): MarkdownVault Main Window Screenshot, AvalonEdit Code Editor Component, CommunityToolkit.Mvvm, Dark Theme Visual Design, File Tab Bar, File Explorer Sidebar with Search, Quick Formatting Toolbar, Obsidian-style Graph View (+16 more)

### Community 48 - "Audio Playback Pipeline"
Cohesion: 0.14
Nodes (14): MediaPlayer, Action, bool, CancellationToken, CancellationTokenSource, ChannelReader, ChannelWriter, Dispatcher (+6 more)

### Community 49 - "Dirty Tab Scanner Tests"
Cohesion: 0.17
Nodes (8): Fact, DirtyTabScannerTests, IEnumerable, List, DirtyTabScanner, PendingTabSave, TabContentSource, IReadOnlyList

### Community 50 - "Whisper Process Job Object"
Cohesion: 0.15
Nodes (15): IO_COUNTERS, JOBOBJECT_BASIC_LIMIT_INFORMATION, JOBOBJECT_EXTENDED_LIMIT_INFORMATION, Action, DllImport, int, IntPtr, long (+7 more)

### Community 51 - "Internal Link Picker Dialog"
Cohesion: 0.12
Nodes (15): TextChangedEventArgs, FileList, SearchBox, StdLinkRadio, WikiLinkRadio, Window, KeyEventArgs, List (+7 more)

### Community 52 - "Graph View Model"
Cohesion: 0.12
Nodes (13): bool, double, GraphService, IReadOnlyList, RelayCommand, string, Task, GraphViewModel (+5 more)

### Community 53 - "App Settings Persistence"
Cohesion: 0.14
Nodes (10): Dictionary, List, ViewMode, AppSettings, JsonSerializerOptions, string, SettingsService, Fact (+2 more)

### Community 54 - "Open Tab Model"
Cohesion: 0.16
Nodes (7): bool, int, string, OpenTab, bool, PinnedEditorContext, PluginWriteRoute

### Community 56 - "File Icon Converter"
Cohesion: 0.17
Nodes (10): CultureInfo, string, Type, FileNodeToIconConverter, IMultiValueConverter, Fact, InlineData, string (+2 more)

### Community 57 - "Whisper Model Catalog"
Cohesion: 0.16
Nodes (12): IReadOnlyList, ModelCatalog, ModelSpec, Action, CancellationToken, double, HttpClient, int (+4 more)

### Community 58 - "Progress Coordinator Tests"
Cohesion: 0.22
Nodes (3): Fact, List, PluginProgressCoordinatorTests

### Community 59 - "Plugin Editor Context"
Cohesion: 0.17
Nodes (6): IRelayCommand, PluginCommand, IEditorContext, Func, IReadOnlyList, PluginToolbarItemViewModel

### Community 60 - "Text Search Engine"
Cohesion: 0.18
Nodes (10): Match, IReadOnlyList, IEnumerable, IReadOnlyList, Regex, TimeSpan, TextMatch, TextReplacement (+2 more)

### Community 62 - "Whisper Service"
Cohesion: 0.11
Nodes (16): Action, bool, CancellationTokenSource, EventArgs, Func, HttpClient, int, List (+8 more)

### Community 63 - "Unsaved Changes Report Tests"
Cohesion: 0.29
Nodes (6): Fact, IReadOnlyList, UnsavedChangesReportTests, IReadOnlyList, UnsavedChangesReport, UnsavedDocument

### Community 64 - "Dictado Glossary Documentation"
Cohesion: 0.14
Nodes (19): PluginListRules (normalización y deduplicación), PluginsWindow / PluginListSettingViewModel (UI de listas), glosario.json (respaldo del glosario), Glosario técnico (prompt de vocabulario), Techo de ~224 tokens del prompt inicial, runtime/ intercambiable CUDA ↔ CPU, Transcripción reproducible (sin arrastre de contexto), whisper.cpp (reconocimiento de voz local) (+11 more)

### Community 65 - "Dictation Session"
Cohesion: 0.18
Nodes (13): Action, CancellationToken, ChannelReader, ChannelWriter, Dictionary, Dispatcher, double, int (+5 more)

### Community 66 - "Plugin Row View Model"
Cohesion: 0.18
Nodes (8): bool, Func, IReadOnlyList, ObservableCollection, RelayCommand, PluginRowViewModel, PluginsViewModel, CancelEventArgs

### Community 67 - "Find Replace UI Bindings"
Cohesion: 0.12
Nodes (17): BoolToVis, FindNextCommand, FindPreviousCommand, IsError, MatchCase, ReplaceAllCommand, ReplaceCommand, ReplaceText (+9 more)

### Community 68 - "Self Write Detection Tests"
Cohesion: 0.26
Nodes (4): Fact, string, Task, FileServiceExternalChangeTests

### Community 69 - "Disposal Tests"
Cohesion: 0.14
Nodes (5): Fact, GraphService, string, Task, GraphServiceTests

### Community 70 - "Small Markdig Plugins"
Cohesion: 0.13
Nodes (11): MarkdownVault.Plugin.Highlight, MarkdownVault.Plugin.CopyButton, string, CalloutsPlugin, string, CopyButtonPlugin, string, HighlightPlugin (+3 more)

### Community 71 - "Plugin Load Context Tests"
Cohesion: 0.24
Nodes (5): MethodImpl, Fact, string, PluginActivationIntegrationTests, WeakReference

### Community 72 - "Diff Merge"
Cohesion: 0.25
Nodes (8): Func, IReadOnlyList, left, List, right, DiffMerge, MergeDirection, CompareMergeRequest

### Community 73 - "Workbench Invariant Tests"
Cohesion: 0.41
Nodes (4): Fact, string, Task, WorkbenchInvariantTests

### Community 74 - "Markdown Rendering Service"
Cohesion: 0.19
Nodes (5): IEnumerable, MarkdownPipeline, Regex, string, MarkdownService

### Community 75 - "Diff Merge Tests"
Cohesion: 0.25
Nodes (4): Fact, left, right, DiffMergeTests

### Community 76 - "Eisenhower Plugin Documentation"
Cohesion: 0.15
Nodes (16): Bloque ```eisenhower (grilla embebida de solo lectura), Enlace de tarea a nota del vault (confinado), tasks.json (sandbox de core.eisenhower), UI esencial vs UI de feature, IHostServices (fachada segura de solo lectura), IMarkdownContribution, IPluginContext, IPluginStorage (sandbox por plugin) (+8 more)

### Community 77 - "Dark Theme Resources"
Cohesion: 0.13
Nodes (15): Arrow, Gesture, PART_ContentHost, PART_Popup, PART_Track, ResourceDictionary, Root, Th (+7 more)

### Community 78 - "Diff HTML Renderer"
Cohesion: 0.23
Nodes (5): IReadOnlyList, DiffHtmlRenderer, Theme, DiffLineKind, Theme

### Community 79 - "Diff HTML Renderer Tests"
Cohesion: 0.26
Nodes (4): Fact, DiffHtmlRendererTests, Fact, DiffServiceTests

### Community 80 - "File Tree Reconciliation Tests"
Cohesion: 0.42
Nodes (3): Fact, string, FileTreeReconciliationTests

### Community 81 - "Vault List UI Bindings"
Cohesion: 0.13
Nodes (14): AddVaultCommand, DataContext.RemoveVaultCommand, DataContext.ToggleOpenCommand, FullPath, HasVaults, IsMissing, IsOpen, IsRemovable (+6 more)

### Community 82 - "Spell Check Service Contract"
Cohesion: 0.18
Nodes (8): ContextMenu, IReadOnlyList, ISpellCheckService, SpellError, Dictionary, HashSet, IReadOnlyList, FakeSpell

### Community 83 - "Graph Data Model"
Cohesion: 0.18
Nodes (13): GeneratedRegex, bool, double, int, GraphLink, GraphNode, BuildAsync(), IReadOnlyList (+5 more)

### Community 84 - "Plugin List Settings API"
Cohesion: 0.35
Nodes (5): Action, Func, PluginListSetting, Fact, PluginListSettingRegistryTests

### Community 86 - "Plugin Manager Tests"
Cohesion: 0.36
Nodes (3): Fact, string, PluginManagerTests

### Community 87 - "Project Context Documentation"
Cohesion: 0.22
Nodes (14): Skill Registry — MarkdownVault, FilePluginLogSink (plugins.log acotado y rotado), MarkdownVault (contexto del proyecto), MVVM con inyección manual en App.xaml.cs, Dictado y Transcripción de Voz — Guía de usuario, EisenhowerWindow (ventana WPF propia del plugin), Eisenhower — Guía de usuario, Matriz de Eisenhower (urgente × importante) (+6 more)

### Community 88 - "Split View Layout Bindings"
Cohesion: 0.18
Nodes (13): DataContext.IsSplit, FontFamily, FontSize, Groups[0], Groups[1], DragCompletedEventArgs, EditorPanelA, EditorPanelB (+5 more)

### Community 89 - "Bool To Icon Converter"
Cohesion: 0.19
Nodes (8): CultureInfo, Type, BoolToIconConverter, CultureInfo, Type, BoolToVisibilityConverter, IValueConverter, Visibility

### Community 90 - "Spell Check Word Resolver"
Cohesion: 0.25
Nodes (6): MisspelledWord, SpellCheckWordResolver, Fact, InlineData, Theory, SpellCheckWordResolverTests

### Community 91 - "Piper TTS Engine"
Cohesion: 0.20
Nodes (8): Action, CancellationToken, IEnumerable, IReadOnlyList, Process, Task, PiperEngine, PiperVoice

### Community 92 - "Plugin Context Contract"
Cohesion: 0.19
Nodes (5): Task, IHostServices, string, Task, PluginHostServices

### Community 94 - "Main Window Shell"
Cohesion: 0.15
Nodes (8): MarkdownVault, Window, MainWindow, IReadOnlyList, PluginsWindow, SplashWindow, VaultsWindow, Window

### Community 95 - "Editor Text Colorizing"
Cohesion: 0.19
Nodes (9): DocumentColorizingTransformer, DocumentLine, Dictionary, HashSet, int, IReadOnlyList, TextDocument, SpellCheckColorizer (+1 more)

### Community 96 - "Whisper HTTP Client"
Cohesion: 0.22
Nodes (8): JsonElement, CancellationToken, HttpClient, IReadOnlyList, Task, TimeSpan, TranscriptionResult, WhisperClient

### Community 97 - "Model Catalog Tests"
Cohesion: 0.27
Nodes (4): Fact, InlineData, Theory, ModelCatalogTests

### Community 98 - "Assorted Unit Tests"
Cohesion: 0.23
Nodes (7): Fact, InlineData, int, List, long, Theory, ModelManagerDownloadGateTests

### Community 99 - "Progress Scope Contract"
Cohesion: 0.18
Nodes (5): CancellationToken, IProgressScope, NoOpProgressScope, Task, FakeHost

### Community 100 - "Export File Naming"
Cohesion: 0.22
Nodes (6): string, ExportNaming, Fact, InlineData, Theory, ExportNamingTests

### Community 101 - "Input Dialog"
Cohesion: 0.18
Nodes (9): InputBox, LabelText, Window, Title, KeyEventArgs, RoutedEventArgs, InputDialog, TextBlock (+1 more)

### Community 102 - "Agent Skill Registry Docs"
Cohesion: 0.17
Nodes (12): Compact Rules (auto-resolved sub-agent standards), dotnet test MarkdownVault.sln (xUnit), MarkdownService, Virtual host mapping vault.local para imágenes, Gotcha de lanzamiento de WebView2 (usar el .exe real), Contribution Model, IActivatablePlugin, IPlugin (+4 more)

### Community 103 - "Ordered Inserter Tests"
Cohesion: 0.33
Nodes (5): Emitted, Instance, Fact, List, OrderedInserterTests

### Community 104 - "Transcript Golden Tests"
Cohesion: 0.24
Nodes (8): Fact, IReadOnlyList, JsonSerializerOptions, List, SegmentDto, TranscriptFormatterGoldenTests, WhisperResponseDto, SegmentDto

### Community 105 - "Async Chunk Draining"
Cohesion: 0.24
Nodes (5): CancellationToken, Exception, IEnumerable, Task, StreamReader

### Community 107 - "View Model Format Tests"
Cohesion: 0.26
Nodes (4): Fact, InlineData, Theory, MainViewModelFormatTests

### Community 108 - "About Window"
Cohesion: 0.23
Nodes (7): BuildText, CopyrightText, VersionText, Window, Assembly, AboutWindow, TextBlock

### Community 109 - "Application Bootstrap"
Cohesion: 0.22
Nodes (4): Application, App, StartupEventArgs, Task

### Community 110 - "Diff Window"
Cohesion: 0.20
Nodes (6): CoreWebView2WebMessageReceivedEventArgs, bool, string, Task, DiffWindow, WebView2

### Community 111 - "Microphone Error Mapping"
Cohesion: 0.25
Nodes (6): Exception, CancellationToken, IAsyncEnumerable, int, MicErrors, MicUnavailableException

### Community 112 - "Multi Vault Design Rationale"
Cohesion: 0.22
Nodes (11): Copy-on-write VaultRoots under _rootsLock, GetOwningRoot longest-prefix resolver, Design: Multi-vault workspace (Model A), Testing Strategy (tests-last, manual layer), Watcher disposal ordering in RemoveRoot, Seven open risks of multi-root, Seven-decision risk table (plain language), Tasks: Multi-vault workspace (8 phases) (+3 more)

### Community 113 - "Eisenhower Project Files"
Cohesion: 0.18
Nodes (9): net8.0-windows, Markdig (1.1.2), Microsoft.NET.Sdk, net8.0-windows, Markdig (1.1.2), Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2) (+1 more)

### Community 114 - "Host Services Open File Tests"
Cohesion: 0.40
Nodes (3): Fact, string, HostServicesOpenVaultFileTests

### Community 115 - "Title Bar Theming"
Cohesion: 0.31
Nodes (5): Color, DllImport, int, IntPtr, NativeTitleBar

### Community 116 - "Transcript Formatter"
Cohesion: 0.24
Nodes (6): char, Regex, TimeSpan, FormatOptions, TranscriptFormatter, TranscriptSegment

### Community 117 - "Dictado Voz Project Files"
Cohesion: 0.20
Nodes (8): NAudio.Wasapi (2.2.1), net8.0-windows, Microsoft.NET.Sdk, net8.0-windows, Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2), Microsoft.NET.Sdk

### Community 118 - "Callouts Mermaid Project Files"
Cohesion: 0.20
Nodes (7): net8.0, Markdig (1.1.2), Microsoft.NET.Sdk, net8.0, Microsoft.NET.Sdk, net8.0, Microsoft.NET.Sdk

### Community 119 - "Microphone Error Tests"
Cohesion: 0.42
Nodes (3): Fact, int, MicErrorsTests

### Community 120 - "Find Replace Window"
Cohesion: 0.27
Nodes (4): bool, CancelEventArgs, KeyEventArgs, FindReplaceWindow

### Community 121 - "Multi Vault Service Docs"
Cohesion: 0.28
Nodes (9): FileService, FileTreeViewModel (una sección por vault abierto), GetOwningRoot (longest-prefix vault scoping), GraphService, GraphViewModel, FileService.VaultRoots (workspace multi-root), VaultsViewModel (Administrar vaults como toggle), Vista de grafo de notas enlazadas (+1 more)

### Community 122 - "Find Replace Design Rationale"
Cohesion: 0.22
Nodes (9): FindReplaceViewModel, FindReplaceWindow no modal (owned, hide en vez de destruir), IFindReplaceTarget (costura formulario ↔ panel con foco), Reemplazar todo de la última a la primera en un BeginUpdate, TextSearch (motor puro de Buscar/Reemplazar), Palabra completa con lookarounds en vez de \b, Guarda de patrón de largo cero + matchTimeout 2s, IEditorContext (+1 more)

### Community 124 - "Logo Brand Identity"
Cohesion: 0.36
Nodes (9): Deep Blue Gradient Palette with Light Blue Glyph, MarkdownVault App Icon (logo.png), Hash (#) Glyph Motif - Markdown Heading Symbol, MarkdownVault Brand Identity, Padlock Motif - Security and Privacy Signal, Secure Markdown Document Storage Product Positioning, Squircle App Icon Format with Transparent Background, Vault Enclosure Motif - Bracketed Frame Around Glyph (+1 more)

### Community 125 - "Payments Meeting Transcript Fixture"
Cohesion: 0.25
Nodes (9): VaultChange record (root-tagged watcher event), Requirement: Per-Root File Watcher, Dictado transcript golden fixture, FPAY, Motor de abonos, Motor de recurrencia (cobros), Otros Medios de Pago (Colombia), Pipeline de nominas (pendientes / a procesar / procesados) (+1 more)

### Community 126 - "Technical Glossary"
Cohesion: 0.22
Nodes (5): int, IReadOnlyList, JsonSerializerOptions, string, TechnicalGlossary

### Community 127 - "Lector Documentos Project Files"
Cohesion: 0.22
Nodes (7): net8.0-windows, Microsoft.NET.Sdk, net8.0-windows, Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2), Microsoft.NET.Sdk

### Community 128 - "Light Theme Resources"
Cohesion: 0.22
Nodes (8): PART_ContentHost, PART_Track, ResourceDictionary, Th, Text, Border, ScrollViewer, Track

### Community 129 - "Host Services Implementation"
Cohesion: 0.22
Nodes (6): Action, FileService, Func, string, Task, HostServices

### Community 130 - "Copy Button Plugin Tests"
Cohesion: 0.44
Nodes (3): Fact, string, CopyButtonPluginTests

### Community 132 - "Core Test Project Files"
Cohesion: 0.22
Nodes (7): net8.0-windows, Microsoft.NET.Test.Sdk (17.11.1), xunit (2.9.2), xunit.runner.visualstudio (2.8.2), Microsoft.NET.Sdk, net8.0-windows, Microsoft.NET.Sdk

### Community 133 - "Vault Migration Tests"
Cohesion: 0.53
Nodes (3): Fact, string, VaultMigrationTests

### Community 135 - "Settings Persistence Rationale"
Cohesion: 0.29
Nodes (8): AppSettings, AppSettings.OpenVaultPaths + migración VaultPathsMigrated, PluginHostServices (decorador de propiedad por plugin), SettingsService, Persistencia de tema (ApplyTheme explícito al startup), Versionado del contrato: minSdk + SemVer, Manifiesto plugin.json, PluginManager

### Community 136 - "Whisper Model Download Docs"
Cohesion: 0.29
Nodes (8): PluginProgressCoordinator, Modelo large-v3-turbo-q5_0, Descarga reanudable del modelo con checksum SHA-256, Modelo small-q5_1, IProgressScope, Concurrencia de progreso como pila LIFO, NoOpProgressScope, Botón de cancelar de dos tiempos (sin timeout de rescate)

### Community 137 - "Plugin Assembly Load Context"
Cohesion: 0.25
Nodes (6): AssemblyDependencyResolver, AssemblyLoadContext, AssemblyName, Assembly, HashSet, PluginLoadContext

### Community 139 - "Fake Storage Test Double"
Cohesion: 0.29
Nodes (3): IDisposable, Task, FakeStorage

### Community 140 - "App Icon Brand Identity"
Cohesion: 0.46
Nodes (8): MarkdownVault App Icon, Deep Blue Gradient Palette, Hash Symbol Markdown Motif, MarkdownVault Brand Identity, Padlock Security Motif, Squircle App Icon Format, Vault Metaphor for Secure Note Storage, Two-Tone MarkdownVault Wordmark

### Community 141 - "Main Project Dependencies"
Cohesion: 0.25
Nodes (7): net8.0-windows, Markdig (1.1.2), Microsoft.NET.Sdk, CommunityToolkit.Mvvm (8.4.2), Microsoft.Web.WebView2 (1.0.3912.50), Microsoft.Xaml.Behaviors.Wpf (1.1.135), Quicker.AvalonEdit (6.3.1)

### Community 142 - "Multi Vault Model Decision"
Cohesion: 0.29
Nodes (8): InsertInternalLink_TwoVaultsOpen_CandidatesScopedToOwningVault regression test, Post-Verify Cleanup (2026-08-14), Closed namespace per vault, Model A: multi-root workspace, Model B: merged cross-vault pool (rejected), WARNING: stale AGENTS.md architecture doc, WARNING: wikilink-autocomplete vault-scoping test gap, Requirement: Vault-Scoped Wikilink Resolution

### Community 143 - "Multi Root Workspace Spec"
Cohesion: 0.32
Nodes (8): Additive settings migration (seed once), Archived delta spec: multi-root-workspace, WARNING: new-file default target test gap, Multi-Root Workspace Specification, Requirement: New File Default Target, Requirement: One-Time Settings Migration, Requirement: Open Vault Set, Requirement: Persisted Open Set

### Community 146 - "Syntax Highlighter"
Cohesion: 0.33
Nodes (4): DocumentHighlighter, Color, TextDocument, SyntaxHighlighter

### Community 147 - "Vault Scoped Resolution Spec"
Cohesion: 0.43
Nodes (7): Archived delta spec: vault-scoped-resolution, WARNING: image-paste VM-level test gap, Requirement: Graph Scoped To Focused Vault, Requirement: Owning Root Resolution, Requirement: Preview Host Scoped To Focused Tab, Requirement: Vault-Scoped Image Paste, Vault-Scoped Resolution Specification

### Community 149 - "Plugin Manifest Tests"
Cohesion: 0.33
Nodes (4): Fact, InlineData, Theory, PluginManifestTests

### Community 150 - "Spell Check Design Docs"
Cohesion: 0.33
Nodes (6): ISpellCheckService, MarkdownProseMask, SpellCheckLanguage (idioma explícito, no CurrentUICulture), SpellCheckColorizer (DocumentColorizingTransformer), WindowsSpellCheckService (COM ISpellChecker), Filtro de Markdown para lectura en voz alta

### Community 151 - "Mermaid Plugin"
Cohesion: 0.40
Nodes (3): MarkdownVault.Plugin.Mermaid, string, MermaidPlugin

### Community 152 - "Plugin Unload Rationale"
Cohesion: 0.33
Nodes (6): Diálogo mediado por el host (fix diferido), Descarga en caliente (RemoveByOwner + Unload + GC), PluginLoadContext (AssemblyLoadContext collectible), Pin del ALC por tipos WPF propios (limitación v1), Piper como proceso aparte (aislamiento de fallos), Carga dinámica de plugins desde Plugins/

### Community 153 - "Markdown Prose Mask"
Cohesion: 0.47
Nodes (3): Regex, StringBuilder, MarkdownProseMask

### Community 154 - "Splash Window"
Cohesion: 0.33
Nodes (5): Pulse, PulseShift, Window, Border, TranslateTransform

### Community 155 - "Plugin Toolbar Template Selector"
Cohesion: 0.50
Nodes (4): DataTemplate, DataTemplateSelector, DependencyObject, PluginToolbarItemTemplateSelector

### Community 156 - "SDD Change Cycle Docs"
Cohesion: 0.60
Nodes (5): multi-vault Change (Model A multi-root workspace), SDD Cycle (Explore to Archive), Manual task 8.6 (two-vault live smoke test), Verify Report: multi-vault, Verdict: PASS WITH WARNINGS

### Community 157 - "Multi Vault Exploration"
Cohesion: 0.40
Nodes (5): Exploration: Multi-vault (Modelo A), vault.local WebView2 virtual host mapping, Single VaultRoot coupling, Vault-agnostic editor/tab/diff layer, Requirement: Close Toggle Behavior

### Community 158 - "Multi Vault Proposal"
Cohesion: 0.40
Nodes (5): Capability: multi-root-workspace, Proposal: Multi-vault workspace (Model A), Rollback plan (additive settings field), Multi-vault success criteria, Capability: vault-scoped-resolution

### Community 160 - "Graph Visibility Bindings"
Cohesion: 0.50
Nodes (4): DataContext.ShowGraph, EditorRegion, InverseBoolToVisConverter, Grid

### Community 162 - "Dictado Config Documentation"
Cohesion: 0.50
Nodes (4): config.json de core.dictado-voz, Detector de silencio (bloque dictation), Dictado en vivo por micrófono, paragraphGapSeconds calibrado en 0,6 s

### Community 163 - "Spelling Error COM Interfaces"
Cohesion: 0.50
Nodes (3): ISpellingError, PreserveSig, IEnumSpellingError

### Community 164 - "File Tree Views"
Cohesion: 0.67
Nodes (3): FileTree, FileTreePanel, FileTreeView

### Community 165 - "Tab Strip Views"
Cohesion: 0.67
Nodes (3): FocusedGroup, PreviewTabStrip, TabStripView

## Ambiguous Edges - Review These
- `Force-Directed Graph Layout` → `Node Size Encodes Link Degree`  [AMBIGUOUS]
  image/Grafos.png · relation: conceptually_related_to

## Knowledge Gaps
- **339 isolated node(s):** `IsDarkTheme`, `IsExplorerVisible`, `ViewMode`, `SetViewModeCommand`, `CycleViewModeCommand` (+334 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **19 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `Force-Directed Graph Layout` and `Node Size Encodes Link Degree`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `MarkdownVault.PluginSdk` connect `Plugin Host Services` to `Lector Documentos Plugin Core`, `Markdig Extension Plugins`, `Plugin Contribution Model`, `Mermaid Plugin`, `Dictado Settings And Rendering`, `File Audio Source`, `Plugin Progress Coordinator`, `Plugin Metadata And Descriptors`, `Core Namespaces And Helpers`, `Dictado Voz Test Suite`, `Open Tab Model`, `Whisper Model Catalog`, `Plugin Editor Context`, `Eisenhower Plugin Shell`, `Whisper Service`, `Dictation Session`, `Small Markdig Plugins`, `Plugin Context Contract`, `Progress Scope Contract`?**
  _High betweenness centrality (0.154) - this node is a cross-community bridge._
- **Why does `EditorGroupViewModel` connect `Progress And Timer State` to `WPF Dialog Service`, `Main View Model`, `Compare View Contract`, `File Tree Commands`, `Plugin Contribution Model`, `File Tree View Model`, `Vault File Service`, `Window Event Handling`, `Pinned Editor Context Tests`, `View Mode Events`, `Workbench Commands`, `Dirty Tab Scanner Tests`, `Open Tab Model`, `Plugin Host Services`, `Plugin Editor Context`, `Workbench Invariant Tests`, `Markdown Rendering Service`, `Image Paste And Assets`, `Tab Close Operations`, `Application Bootstrap`?**
  _High betweenness centrality (0.132) - this node is a cross-community bridge._
- **Why does `MainViewModel` connect `Main View Model` to `WPF Dialog Service`, `Vault Migration Tests`, `Compare View Contract`, `Vaults View Model Tests`, `Plugin Contribution Model`, `File Tree View Model`, `Progress And Timer State`, `Vault File Service`, `Pinned Editor Context Tests`, `App Startup Wiring`, `View Mode Events`, `Compare Files Command Tests`, `Workbench Commands`, `Graph View Model`, `App Settings Persistence`, `Unsaved Changes Report Tests`, `Diff Merge`, `Workbench Invariant Tests`, `Markdown Rendering Service`, `Image Paste And Assets`, `Main Window Shell`, `View Model Format Tests`, `Application Bootstrap`?**
  _High betweenness centrality (0.122) - this node is a cross-community bridge._
- **Are the 33 inferred relationships involving `MainViewModel` (e.g. with `Window` and `CancelProgressCommand`) actually correct?**
  _`MainViewModel` has 33 INFERRED edges - model-reasoned connections that need verification._
- **What connects `IsDarkTheme`, `IsExplorerVisible`, `ViewMode` to the rest of the system?**
  _339 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Plugin List Normalization Rules` be split into smaller, more focused modules?**
  _Cohesion score 0.07688492063492064 - nodes in this community are weakly interconnected._
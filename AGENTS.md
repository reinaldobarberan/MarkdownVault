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
│   ├── GraphNode.cs              # Nodo del grafo + su estado de física, grupo, isla y destino
│   ├── GraphSettings.cs          # Perillas del grafo que se recuerdan por vault
│   └── ViewMode.cs               # Enum: EditorOnly | EditAndPreview | ViewerOnly
├── ViewModels/
│   ├── MainViewModel.cs          # VM principal (vaults abiertos, tema, fuente, explorador)
│   ├── EditorGroupViewModel.cs   # VM del editor (tabs, contenido, preview, formato)
│   ├── FileTreeViewModel.cs      # VM del árbol de archivos (una sección por vault abierto)
│   ├── VaultsViewModel.cs        # VM de "Administrar vaults" (abrir/cerrar cada vault conocido)
│   ├── GraphViewModel.cs         # VM del grafo de notas, scopeado al vault del tab activo
│   ├── GraphGroupViewModel.cs    # Fila de la leyenda de carpetas (color, cantidad, on/off)
│   └── FindReplaceViewModel.cs   # VM del formulario Buscar/Reemplazar (patrón, opciones, comandos)
├── Views/
│   ├── MainWindow.xaml / .cs     # Ventana principal (layout, WebView2, tabs)
│   ├── EditorView.xaml / .cs     # Editor AvalonEdit + toolbar de formato
│   ├── FileTreeView.xaml / .cs   # Árbol lateral del vault
│   ├── FindReplaceWindow.xaml / .cs # Formulario flotante de Buscar/Reemplazar (no modal)
│   ├── FindCommands.cs           # RoutedUICommands de Buscar/Reemplazar (menú + atajos)
│   ├── GraphView.xaml / .cs      # Overlays del grafo (filtros, leyenda de carpetas, zoom)
│   ├── GraphCanvas.cs            # Superficie inmediata: simulación + dibujo + cámara
│   └── InputDialog.xaml / .cs    # Diálogo para input de usuario
├── Services/
│   ├── FileService.cs            # I/O de archivos, escaneo; mantiene VaultRoots (multi-root)
│   ├── GraphService.cs           # Grafo de notas/enlaces, scopeado a un vault root por vez
│   ├── GraphFilter.cs            # Motor PURO de filtrado (grado, carpetas, saltos, búsqueda)
│   ├── GraphGrouping.cs          # Grupos por carpeta de 1er nivel + anclas en anillo
│   ├── GraphComponents.cs        # Islas (componentes conexos) por union-find
│   ├── GraphLayout.cs            # Política de layout: destinos por nodo + aparcado de huérfanas
│   ├── GraphOctree.cs            # Barnes-Hut 3D para la repulsión (reemplaza el O(n²))
│   ├── GraphCollision.cs         # Separación de nodos solapados por grilla uniforme
│   ├── GraphMetrics.cs           # Radio del nodo — fuente ÚNICA para dibujo, colisión y encuadre
│   ├── GraphCamera.cs            # Cámara órbita + proyección/des-proyección en perspectiva
│   ├── GraphPalette.cs           # Paleta de colores de grupo (fuente única canvas + leyenda)
│   ├── GraphSettingsService.cs   # Ajustes del grafo por vault, en AppData, con escritura diferida
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
- **Grafo de notas**: vista tipo Obsidian del vault del tab activo. Color y agrupación por carpeta de primer nivel con leyenda para encender/apagar cada una; islas (componentes conexos) separadas dentro de cada carpeta; notas sin enlaces aparcadas en anillos exteriores; filtros por enlaces mínimos, saltos del grafo local y texto; etiquetas que se descartan solas cuando se pisarían; arrastrar fija un nodo (clic derecho o el botón 📌 lo libera)
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

### Grafo de notas
- **La política vive en servicios PUROS, el canvas solo dibuja.** `GraphFilter`, `GraphGrouping`, `GraphComponents`, `GraphLayout`, `GraphOctree`, `GraphCamera` y `GraphPalette` no referencian WPF: reciben nodos/enlaces y devuelven decisiones. Por eso todo el comportamiento del grafo se testea headless (`GraphFilterTests`, `GraphGroupingTests`, `GraphComponentsTests`, `GraphLayoutTests`, `GraphOctreeTests`, `GraphCameraTests`), sin un solo test de UI. Misma convención que `TextSearch`. La capa de servicios define su propio `GraphPoint` justamente para no arrastrar `System.Windows.Point` adentro.
- **`GraphFilter`, `GraphComponents` y `GraphPalette` tienen CERO referencias a coordenadas.** El filtrado trabaja sobre grados, carpetas y texto; los componentes son topología pura. Por eso pasar el grafo de 2D a 3D no les tocó una línea — y es la mejor evidencia de por qué conviene mantener esa separación.
- **Agrupar NO suma una fuerza nueva: cambia el destino de la gravedad que ya existía.** Con "Agrupar por carpeta" encendido, cada nodo es atraído a `TargetX/TargetY` (el lugar de su isla dentro de su carpeta) en vez de al origen. Apilar una "fuerza de cluster" sobre una gravedad global que tira al centro produce dos fuerzas peleándose y un layout que nunca se define.
- **Las anclas de carpeta son FIJAS en un anillo, no centroides vivos.** Un centroide calculado por frame deriva hacia los otros y el grafo vuelve a colapsar en una sola bola. El anillo garantiza la separación pase lo que pase con la simulación.
- **Los índices de grupo se ordenan alfabéticamente (raíz primero), nunca por aparición ni por tamaño.** El índice es también el slot de la paleta: si cambiara al agregar una nota, el vault entero se recolorearía. La misma regla aplica a las islas en `GraphComponents`, renumeradas por la nota alfabéticamente primera de cada una.
- **Una nota sin enlaces no se simula.** No hay nada que la empuje salvo la repulsión mutua, así que `GraphLayout` le da un asiento fijo en anillos concéntricos exteriores (`Parked = true`) y `GraphCanvas` la saltea. Declutter y rendimiento a la vez: en un vault pobre en enlaces, esas son la mayoría de las notas.
- **Repulsión con Barnes-Hut, colisión con grilla uniforme.** Son dos problemas distintos: la repulsión es de largo alcance (un cúmulo lejano empuja como un solo cuerpo con la masa total → octree, O(n log n)); la colisión es estrictamente local (solo entre esferas que ya se tocan → grilla dimensionada al solapamiento máximo, 27 celdas por nodo). Meter la colisión en el octree habría sido más código y peor.
- **`GraphCollision.Tolerance` (0.05) no es cosmética.** Deshacer una FRACCIÓN del solapamiento por pasada (`stiffness` 0.5) hace que la distancia se acerque al mínimo asintóticamente y nunca lo alcance: sin tolerancia el pase reportaría "sigue solapado" para siempre y no se podría usar para decidir que un layout está limpio.
- **El desempate para nodos EXACTAMENTE coincidentes reparte sobre la esfera, no sobre un eje.** Mandar cada par coincidente en dirección X ensarta la pila en una fila, y una fila después relaja de a vecinos — cientos de pasadas para veinte nodos. La dirección sale de un FNV-1a de los dos ids: estable entre frames (si cambiara, los nodos coincidentes temblarían) y determinista entre procesos, cosa que `string.GetHashCode` NO garantiza porque .NET randomiza el hash de strings por proceso.
- **`GraphNode.Radius` es la fuente única del tamaño del nodo**, estampada por `GraphMetrics.Assign`. La comparten el dibujo, la colisión y el encuadre. Dos copias derivarían, y el síntoma sería discos que se solapan por más que la colisión empuje.
- **El tamaño es RELATIVO al vault, no absoluto.** `MinRadius + (MaxRadius-MinRadius)·√(grado/gradoMáximo)`. Una fórmula absoluta obliga a asumir un rango de grados, y los vaults no se ponen de acuerdo: uno disperso llega a 5, un wiki denso a 32. Calibrada para el primero, el segundo se vuelve un pegote — y alejar el zoom NO ayuda, porque todo se achica junto y lo que está mal es la PROPORCIÓN.
- **El slider "Tamaño de nodos" se aplica en el RADIO, no al dibujar.** El radio es también lo que la colisión mantiene separado y lo que el encuadre mide: escalar solo el pintado agrandaría los discos sin darles más lugar, y se encimarían. Por eso `GraphMetrics.Assign(nodes, scale)` recalcula desde el grado en vez de multiplicar sobre lo que ya había — si no, arrastrar el slider los haría crecer sin límite.
- **El encuadre automático tiene DOS techos, no uno.** Llenar la ventana no alcanza: un vault chico y muy enlazado se magnificaría hasta que sus hubs fueran manchas superpuestas. `MaxFitZoom` limita además por el tamaño en píxeles que puede alcanzar el nodo más grande (`MaxNodeScreenRadius`).
- **`θ = 0` en `GraphOctree` es EXACTO y es el ancla de los tests.** Con θ=0 el árbol no aproxima nada y tiene que dar lo mismo, nodo por nodo, que el bucle de fuerza bruta escrito literal dentro de `GraphOctreeTests`. Producción usa θ=0.7. Una aproximación sin forma de verificarla genera layouts que *parecen* plausibles: el peor tipo de bug.

### Grafo: ajustes por vault
- **Los ajustes se guardan POR VAULT, no globalmente.** Lo correcto depende de cómo sea el vault: un wiki denso de 40 notas quiere nodos chicos y piso de enlaces alto; un montón suelto de mil notas quiere lo contrario. Un único ajuste global estaría mal para todos los vaults menos uno.
- **El archivo vive FUERA del vault** (`%AppData%/MarkdownVault/graphs/<nombre>-<hash>.json`). Adentro aparecería en el explorador (`Directory.GetDirectories` no filtra nada) y, peor, el `FileSystemWatcher` del vault tiene `IncludeSubdirectories=true` sin filtro: arrastrar un slider refrescaría el árbol decenas de veces por segundo.
- **El nombre de archivo usa FNV-1a, NO `string.GetHashCode`.** .NET randomiza el hash de strings POR PROCESO: con eso, cada arranque mapearía el mismo vault a un archivo distinto y no se recuperaría ningún ajuste jamás. Hay un test que fija el nombre esperado como candado — si se cambia el algoritmo hace falta una migración, no un valor esperado nuevo.
- **Las carpetas apagadas se guardan por NOMBRE, no por índice.** Los índices se reparten por orden alfabético de carpeta, así que agregar o borrar una corre todas las de después y al recargar se escondería la carpeta equivocada.
- **La escritura es DIFERIDA (400 ms)** porque arrastrar un slider dispara decenas de cambios y solo importa el último. Consecuencia obligatoria: `MainWindow.Window_Closing` llama a `Graph.FlushSettings()` — al cerrar no hay 400 ms y se perdería lo último que se tocó.
- **`_restoring` en `GraphViewModel` no es opcional.** Cada setter reacciona relayouteando y encolando un guardado; sin la bandera, restaurar diez valores correría el layout diez veces y reescribiría en disco lo que se acababa de leer.
- **El orden en `BuildAsync` importa**: restaurar ANTES de `RebuildGroups` (el tamaño de nodo y el modo 3D deciden dónde caen las anclas), y las carpetas apagadas DESPUÉS (las filas de la leyenda todavía no existen).
- **La búsqueda NO se guarda**: es una pregunta momentánea, no un ajuste. El archivo activo tampoco — lo sigue la pestaña con foco.

### Grafo: vista 3D
- **La simulación es SIEMPRE tridimensional; el modo plano es el caso degenerado.** Con "Vista 3D" apagada, `GraphLayout` pone todos los destinos en Z=0 y la cámara queda en yaw/pitch 0: la proyección devuelve exactamente la cámara 2D anterior. Un camino de código, no dos.
- **El interruptor 3D RE-EJECUTA el layout, no solo inclina la cámara.** Aplastar una esfera de anclas contra el plano dejaría dos carpetas en el mismo punto. Por eso `GraphLayout.Assign` recibe `spatial` y `GraphGrouping.Anchors` reparte en círculo o en esfera según el caso (`GraphLayoutTests.Switching_between_flat_and_spatial_never_drops_two_folders_on_one_spot`).
- **Ese es el argumento técnico del 3D**: sobre un anillo el lugar disponible crece con el radio (2πr); sobre una esfera, con el radio AL CUADRADO (4πr²). La misma cantidad de carpetas necesita bastante menos radio en 3D.
- **Las huérfanas quedan PLANAS incluso en 3D**, a propósito. Una esfera de huérfanas envolvería el grafo y taparía las notas conectadas desde cualquier ángulo; un halo plano es algo que se mira a través.
- **Se dibuja en coordenadas de PANTALLA, sin `PushTransform` global.** Con perspectiva cada nodo tiene su propia escala y una sola transformación no puede expresar eso. Consecuencia: los grosores de línea y el tamaño del texto son constantes en píxeles, no divididos por el zoom.
- **La atenuación por distancia se normaliza contra el rango de profundidad REAL del grafo** (`_nearDepth`/`_farDepth`, que mide `ProjectVisible`), no contra la razón de perspectiva de la cámara. Atarla a la perspectiva fue un bug: con un grafo de 440 unidades de hondo visto desde 1700, el rango salía 0.89–1.00 — un 11% que nadie ve. Un layout plano no tiene rango, así que `Fog` devuelve 1 y el efecto se apaga solo, sin bandera de modo.
- **Lo lejano se MEZCLA hacia el color de fondo, no se vuelve transparente.** Un disco translúcido deja ver la maraña de aristas por detrás y queda embarrado; uno mezclado sigue sólido y simplemente parece lejano. Es como funciona la bruma en un horizonte real. Los brushes se cachean por (color, escalón de niebla) — uno por nodo por frame sería puro desperdicio.
- **Las aristas también se atenúan** con la profundidad promedio de sus extremos. A intensidad plena forman una malla brillante por delante de todo y aplanan la profundidad que los discos intentan transmitir.
- **Orden del pintor.** No hay z-buffer dibujando en 2D inmediato, así que los nodos se ordenan por profundidad y se dibujan de atrás hacia adelante. Las aristas van todas antes, y se descarta la que tenga un extremo detrás del lente (se dibujaría cruzando la pantalla).
- **El texto NO se escala con la distancia.** Una etiqueta lejana en tipografía diminuta sería ilegible y encima ocuparía lugar. La distancia decide quién GANA el espacio, no de qué tamaño se dibuja.
- **Las etiquetas llevan un halo del color del FONDO**, dibujado sobre la geometría del texto (`FormattedText.BuildGeometry`), no repitiendo el texto desplazado. El color sale del recurso `GraphBackground`, así que acompaña el tema. Sin halo, un nombre sobre un nodo claro es ilegible por más que el orden de dibujo sea correcto.
- **El halo son DOS pasadas, y el orden es todo**: `DrawGeometry(null, haloPen, geom)` primero y `DrawGeometry(labelBrush, null, geom)` encima. Un trazo en WPF va CENTRADO sobre el contorno — mitad afuera, mitad ADENTRO — y a 11px la mitad interior es más ancha que el palo de las letras. Pintar relleno y trazo en una sola llamada tapa el glifo con el halo y el texto desaparece. Bug real, costó una iteración. Por eso `HaloThickness = 3.0`: solo la mitad queda visible.
- **La geometría del texto se cachea construida en el ORIGEN y se posiciona con un `TranslateTransform`.** `BuildGeometry` es lo bastante caro como para que llamarlo por etiqueta por frame costara más que todo el resto del frame junto.
- **El reposo exige DOS condiciones: velocidades bajas Y cero solapamientos.** La colisión corrige POSICIONES sin tocar velocidades, así que un layout puede estar perfectamente quieto y con discos encimados. Ese fue un bug real: la simulación se congelaba a mitad de resolver. Por eso `GraphCollision.Resolve` devuelve una cuenta y por eso existe `Tolerance` — sin ella la cuenta nunca llegaría a cero.
- **`MaxFramesAwake = 600` es una válvula de seguridad**, no paranoia: un grafo lo bastante denso está sobre-restringido y sus solapamientos no llegan a cero nunca. Sin tope, ese grafo tendría un núcleo al 100% mientras la vista esté abierta.
- **Los resortes NO comprimen el cúmulo.** Con reposo 130 y nodos a 32, el resorte está COMPRIMIDO y empuja hacia afuera. Quien junta es la gravedad hacia el ancla de carpeta: para aflojar un cúmulo el control es **Fuerza central**, no **Enlaces**.
- **`Project` y `Unproject` deben ser inversas exactas, y hay un test de ida y vuelta que lo verifica.** Arrastrar un nodo depende de eso: una posición del mouse es un RAYO, no un punto, así que el canvas fija la profundidad actual del nodo (`_dragDepth`) y des-proyecta sobre ese plano. Una inversa sutilmente mal hecha se manifiesta como nodos que se escapan del cursor — parece un bug de física y es imposible de rastrear desde el síntoma.
- **`Unproject` solo es inversa DELANTE del lente.** Detrás no hay nada que invertir. `Pick` ya rechaza nodos con `Depth <= NearPlane`, así que nunca se puede arrastrar uno desde ahí.
- **`Pick` devuelve el más CERCANO a la cámara entre los candidatos**, no el primero: con perspectiva varios nodos comparten el mismo píxel y hay que hacer clic en el que se ve.
- **El pitch se limita a ±1.4 rad.** Cruzar el polo da vuelta el mundo en medio del arrastre y el usuario pierde toda referencia.
- **Entrar en 3D inclina la cámara de una (yaw 0.6 / pitch 0.35).** Dejarla cuadrada mostraría una imagen idéntica a la plana y el interruptor parecería no hacer nada.
- **`FitToContent` itera (4 pasadas).** Con perspectiva la respuesta se alimenta a sí misma: cambiar el zoom mueve la cámara, lo que cambia el tamaño proyectado del grafo.
- **El tope de profundidad del quadtree (32) no es defensivo por gusto**: dos notas fijadas en el mismo píxel subdividirían para siempre. En el tope la celda guarda la masa acumulada sin hijos y la consulta la trata como un solo grumo.
- **La simulación se congela al asentarse**, y CUALQUIER `PropertyChanged` del VM la despierta (`Wake()`), no solo el mouse. Sin eso, apagar una carpeta dejaría el grafo congelado en una imagen ya incorrecta. El renderizado sigue corriendo cada frame igual: el hover y la cámara tienen que repintar, y dibujar es la mitad barata.
- **`FitToContent` NO se puede llamar al reconstruir el grafo**: en ese momento las posiciones siguen siendo el círculo semilla de `GraphService`. Se difiere (`_needsFit`) al primer frame en que el layout se asienta, que es cuando recién se conoce la extensión real.
- **Las etiquetas se descartan por oclusión.** Se ordenan por importancia (nota activa → nodo bajo el cursor y sus vecinos → grado) y la que chocaría con una ya colocada se saltea ese frame. Dibujarlas todas convierte un vault mediano en una sopa ilegible.
- **El ámbar `#E0AF68` está deliberadamente FUERA de `GraphPalette`**: es el color de la nota activa, y un grupo usándolo la volvería imposible de encontrar.
- **Gotcha de `DockPanel` en `GraphView.xaml`**: `LastChildFill` está en `True` por defecto y **el último hijo ignora su `Dock`** — rellena el espacio sobrante. Lo que va anclado a la derecha (el valor de un slider, un interruptor) se declara PRIMERO; el elemento que debe estirarse va último. Poner el valor al final lo pega a la etiqueta.
- **Los converters se declaran localmente en `GraphView.Resources`, no vía `StaticResource` al diccionario de tema**: el tema se reemplaza en caliente al cambiar light/dark. Misma convención que `FindReplaceWindow.xaml`.

### Enlaces internos y anclas: el AST NO es el texto que ve el usuario
- **`MarkdownService.ParsePreviewAst` parsea una COPIA REESCRITA, no el buffer.** Antes de parsear (y de renderizar) pasa `PreprocessWikiLinks`, que expande cada `[[...]]` a un enlace Markdown completo: `[[audio parte 001#^8jlekw]]` (27 caracteres) → `[audio parte 001](<audio parte 001.md#^8jlekw>)` (47). **Todo `SourceSpan` posterior a un wikilink queda corrido.** Son índices dentro de la cadena reescrita.
- **NUNCA indexar el documento vivo de AvalonEdit con un offset sacado de ese AST.** Es la regla que costó un crash en producción: `TextEditor.Document.Insert(block.Span.End + 1, ...)` tiraba `ArgumentOutOfRangeException: '0 <= offset <= 45 … Actual value was 65'` sobre una nota de dos líneas. Y el modo de falla PEOR es el silencioso: en una nota larga ese offset sigue siendo válido, así que el marcador entra en el párrafo equivocado sin excepción, sin log y sin síntoma. `AnchorLocator` devolvía `Span.Start` y llevaba el scroll al lugar equivocado en cualquier nota con un wikilink antes del ancla — roto desde que se publicó, sin que nadie pudiera atribuirlo.
- **La unidad de intercambio entre el AST y el documento vivo es la LÍNEA.** `PreprocessWikiLinks` sustituye siempre DENTRO de una línea y nunca agrega ni saca saltos — y eso ahora es estructural, no casual: los regex de wikilink excluyen `\r\n` explícitamente (un `[[a\nb]]` ya no se convierte en enlace, igual que en Obsidian). Por eso `block.Line` sí es válido. `AnchorLocator.Find` devuelve LÍNEA (base 0, convención Markdig) y la vista la convierte con `Document.GetLineByNumber(line + 1).Offset`, que es verdadero por construcción.
- **Los ids sí sobreviven**, y por eso la resolución de ids se sigue haciendo sobre el parseo preprocesado: la garantía de decisión #6 (el id de un heading es exactamente el que renderiza el preview) queda intacta. Sólo los spans están envenenados.
- Cobertura de regresión en `AnchorLocatorTests` (sección "offsets envenenados"): todas las aserciones son contra el TEXTO CRUDO, nunca contra el reescrito.

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

### Posición de lectura (scroll) del editor y del preview
- **La posición del editor se guarda en LÍNEA del documento, NO en píxeles** (`OpenTab.ScrollAnchor`, `Models/EditorScrollAnchor.cs`). Esto no es preferencia de estilo: es la única unidad que sobrevive. Con `WordWrap="True"` un párrafo de Markdown es UNA línea lógica partida en decenas de líneas visuales, así que el mismo texto ocupa distinta altura según el ancho del panel y el tamaño de fuente — un píxel guardado deja de apuntar al mismo texto apenas el usuario mueve el splitter.
- **El mecanismo del bug, que es lo que hay que entender antes de tocar esto**: AvalonEdit mantiene un árbol de alturas donde cada línea que TODAVÍA NO SE RENDERIZÓ vale la altura por defecto de UNA sola línea. Al cambiar de pestaña el documento se reemplaza entero y ese árbol arranca casi todo por defecto: `ExtentHeight` queda MUY por debajo de la altura real y el ScrollViewer **recorta en silencio** cualquier offset mayor a `ExtentHeight - ViewportHeight`. Medido sobre un editor real: se guardaba la línea 136 y se aterrizaba en la 217. En una nota de pocas pantallas el recorte colapsa a ~0 y la pestaña aparece directamente arriba de todo. Con `WordWrap="False"` el mismo código restauraba exacto — esa es la prueba del mecanismo.
- **`EditorScrollRestorer` es el dueño ÚNICO del scroll vertical programático del editor.** Restaurar al cambiar de pestaña y revelar una línea (ancla de `[[Nota#Sección]]`, coincidencia de Buscar) comparten mecanismo porque comparten causa raíz. Pide el destino, deja que el scroll obligue a AvalonEdit a MEDIR las líneas que quedan a la vista, y vuelve a preguntar contra el árbol ya corregido. Converge en pocas pasadas.
- **La llegada se mide sobre la LÍNEA que se está mostrando, nunca comparando píxeles.** El destino en píxeles y el pintado se calculan los dos contra el árbol, pero el propio pintado ACTUALIZA ese árbol en el mismo pase: "el offset pedido es el offset actual" puede ser cierto y aun así estar mostrando otra línea. Bug real, costó una iteración (se guardaba la línea 29 y se aterrizaba en la 33).
- **El delta sub-línea se RECORTA al alto actual de su línea** (`EditorScrollAnchor.DesiredOffset`). Mientras esa línea no se midió vale la altura por defecto, y un delta guardado sobre el párrafo ya envuelto se pasaría de largo hacia las siguientes — ahí el bucle queda trabado, porque el destino es coherente consigo mismo y aun así muestra otra línea, así que no hay nada que corregir y no converge nunca.
- **El bucle tiene tres salidas duras y ninguna más**: llegó, se agotaron los intentos (techo de 12), o el usuario tocó algo (rueda / tecla / clic). `Cancel()` es el único punto de desuscripción y todos los caminos pasan por ahí. Un pedido nuevo cancela al anterior: nunca hay dos asentamientos peleando por el mismo viewport.
- **El cursor NO lleva la posición de lectura.** El setter de `TextEditor.Text` manda `CaretOffset` a 0 en cada swap, y quien lee con la rueda del mouse nunca movió el cursor. Se restaura igual, pero para editar: la posición la lleva el ancla, y sola.
- **La memoria de scroll del preview es por pestaña** (`OpenTab.PreviewScrollY`) y se lee con `ExecuteScriptAsync("window.scrollY")` **justo antes** de navegar afuera, contra `_lastPreviewTab` — la dueña de la página que todavía está en pantalla, que NO es `ActiveTab` (en mitad de un cambio de pestaña esa ya es la nueva).
- **Solo la ruta de NAVEGACIÓN COMPLETA guarda y restaura.** La ruta de parche en sitio (`__mvSetBody`) no recarga la página y conserva el scroll sola; reaplicar ahí un valor guardado movería la vista de alguien que solo estaba tipeando.
- **Orden en `NavigationCompleted`: EL ANCLA GANA.** Saltar a un `#ancla` y volver a donde se estaba leyendo cuelgan del MISMO evento, así que si no se ordenan explícitamente pelean y gana cualquiera. Un ancla es un pedido explícito del usuario en ese instante; la memoria de scroll es un valor por defecto. Por eso `ApplyPendingPreviewAnchorOrScrollAsync` consume el ancla primero y solo cae al scroll guardado cuando no hay ninguna pendiente.
- **El script de restauración del preview REINTENTA mientras quede corto.** Al terminar la navegación el documento existe pero todavía crece (imágenes sin medir, Mermaid sin renderizar) y el navegador recorta un `scrollTo` que se pase del alto actual — el mismo modo de fallo que el árbol de alturas del lado del editor. Se corta en seco apenas detecta que la posición no es la que él mismo dejó: eso significa que scrolleó el usuario.
- **Toda la política es PURA y se testea headless** (`Services/EditorScrollPolicy.cs` + `Models/EditorScrollAnchor.cs`, cubiertos por `EditorScrollPolicyTests`). Misma convención que `TextSearch` y los servicios del grafo: el control WPF solo traduce números del árbol de alturas a llamadas de scroll.

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

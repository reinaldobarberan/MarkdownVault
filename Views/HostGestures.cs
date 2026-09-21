using System.Windows.Input;

namespace MarkdownVault.Views;

/// <summary>
/// Fuente de verdad DECLARADA de los gestos de teclado que el host reserva por fuera
/// de <c>Window.InputBindings</c> (SDK 1.6.0, atajos de plugin). <c>Window.InputBindings</c>
/// se enumera en vivo desde <c>PluginShortcutBinder</c> — no hace falta duplicarlo acá — pero
/// hay DOS fuentes más que un binding de ventana nunca ve, y que un choque contra ellas no
/// produce ni un error ni un "atajo tomado": simplemente no pasa NADA al apretar la tecla,
/// sin diagnóstico (ver observación #474). Esta clase es el remedio: un lugar único, no un
/// literal duplicado en cada lado.
///
/// 1. <see cref="EditorConsumed"/> — <c>Views/EditorView.xaml.cs</c> intercepta estos cuatro
///    gestos en <c>OnPreviewKeyDown</c> (code-behind, <c>e.Handled = true</c>), que TUNELA de
///    raíz a hoja y corre ANTES de que WPF traduzca <c>Window.InputBindings</c>. Un
///    <c>KeyBinding</c> de plugin sobre uno de estos gestos quedaría sombreado sin aviso.
///    <c>EditorView.OnPreviewKeyDown</c> compara contra estas MISMAS instancias (no contra
///    literales <c>Key</c>/<c>ModifierKeys</c> duplicados) para que el checker y el handler
///    NUNCA puedan divergir — ver la refactorización de esa clase.
///
/// 2. <see cref="EditorControlDefaults"/> — los bindings propios que AvalonEdit (el fork
///    <c>Quicker.AvalonEdit</c> 6.3.1 que usa este proyecto) registra en
///    <c>TextArea.CommandBindings</c>/<c>TextArea.DefaultInputHandler</c> sobre el control con
///    foco, aunque el host jamás los declare. Esta lista salió de auditar ese fork en
///    concreto por reflexión (instanciar un <c>TextArea</c> y volcar sus
///    <c>CommandBindings</c> + <c>DefaultInputHandler.NestedInputHandlers</c>), no de la
///    documentación genérica de AvalonEdit — la tarea 2.4 la dejaba pendiente porque una
///    lista "de memoria" podía no coincidir con este fork puntual.
///
/// 3. <see cref="MenuOnlyClaims"/> — atajos que <c>MainWindow.xaml</c> anuncia como
///    <c>InputGestureText</c> en un <c>MenuItem</c> pero que NO tienen <c>&lt;KeyBinding&gt;</c>
///    real ni una rama de <c>OnPreviewKeyDown</c> (Ctrl+Shift+O, Ctrl+Shift+P): son etiquetas
///    falsas que hoy no hacen nada al apretarlas. Igual se reservan — si un plugin las tomara
///    y el usuario después viera CUALQUIERA de las dos funcionar por una implementación futura,
///    sería un choque silencioso invertido (el plugin dejaría de andar sin loggear nada nuevo,
///    porque nadie tocó este archivo). Ver design D1, pregunta abierta: si ese atajo se
///    promueve a un <c>&lt;KeyBinding&gt;</c> real, sacar la entrada de acá (quedaría
///    duplicada, no rota — el reservado se arma con Union, no con suma).
/// </summary>
public static class HostGestures
{
    // ─── EditorConsumed: MISMAS instancias que EditorView.OnPreviewKeyDown testea ────

    /// <summary>Ctrl+S. Guardar — corre ANTES del corte por HasOpenDocument (ver EditorView).</summary>
    public static readonly KeyGesture EditorSave = new(Key.S, ModifierKeys.Control);

    /// <summary>Ctrl+V CON una imagen en el portapapeles. Pega la imagen como adjunto.</summary>
    public static readonly KeyGesture EditorPasteImage = new(Key.V, ModifierKeys.Control);

    /// <summary>Ctrl+B. Envuelve la selección en negrita.</summary>
    public static readonly KeyGesture EditorBold = new(Key.B, ModifierKeys.Control);

    /// <summary>Ctrl+I. Envuelve la selección en cursiva.</summary>
    public static readonly KeyGesture EditorItalic = new(Key.I, ModifierKeys.Control);

    public static readonly IReadOnlyList<KeyGesture> EditorConsumed = new[]
    {
        EditorSave, EditorPasteImage, EditorBold, EditorItalic
    };

    // ─── EditorControlDefaults: auditados por reflexión sobre Quicker.AvalonEdit 6.3.1 ──
    // (instanciar TextArea() en un hilo STA y volcar TextArea.CommandBindings +
    // DefaultInputHandler.NestedInputHandlers[*].InputBindings). Cubre las TRES fuentes
    // AvalonEdit-nativas: los RoutedCommand de WPF con gesto por defecto (Copy/Cut/Paste/
    // Undo/Redo/SelectAll — el gesto vive en ApplicationCommands.*, no en un KeyBinding
    // propio de AvalonEdit), los comandos propios del fork (AvalonEditCommands.Duplicate/
    // DeleteLine/IndentSelection/ToggleOverstrike) y los InputBindings explícitos de
    // CaretNavigationCommandHandler/EditingCommandHandler (Tab, Home/End, flechas...).
    public static readonly IReadOnlyList<KeyGesture> EditorControlDefaults = new[]
    {
        // Portapapeles (ApplicationCommands.Copy/Cut/Paste — cada uno con dos gestos).
        new KeyGesture(Key.C,      ModifierKeys.Control),
        new KeyGesture(Key.Insert, ModifierKeys.Control),
        new KeyGesture(Key.X,      ModifierKeys.Control),
        new KeyGesture(Key.Delete, ModifierKeys.Shift),
        new KeyGesture(Key.V,      ModifierKeys.Control),
        new KeyGesture(Key.Insert, ModifierKeys.Shift),

        // Deshacer / rehacer / seleccionar todo.
        new KeyGesture(Key.Z, ModifierKeys.Control),
        new KeyGesture(Key.Y, ModifierKeys.Control),
        new KeyGesture(Key.A, ModifierKeys.Control),

        // Comandos propios del fork AvalonEdit (AvalonEditCommands, namespace TextEditor
        // en el ensamblado): Ctrl+I (IndentSelection) coincide con EditorItalic de arriba
        // a propósito — un plugin que pidiera Ctrl+I chocaría contra AMBAS fuentes; el
        // dedupe por VALOR lo hace el resolver (Union, no lista con repetidos que rompa).
        new KeyGesture(Key.D, ModifierKeys.Control),                                // Duplicate
        new KeyGesture(Key.D, ModifierKeys.Control | ModifierKeys.Shift),            // DeleteLine
        new KeyGesture(Key.I, ModifierKeys.Control),                                 // IndentSelection
        new KeyGesture(Key.Insert, ModifierKeys.None),                               // ToggleOverstrike

        // Edición básica sin modificador (EditingCommandHandler): borrar y estructura.
        new KeyGesture(Key.Delete, ModifierKeys.None),
        new KeyGesture(Key.Delete, ModifierKeys.Control),
        new KeyGesture(Key.Back,   ModifierKeys.None),
        new KeyGesture(Key.Back,   ModifierKeys.Shift),
        new KeyGesture(Key.Back,   ModifierKeys.Control),
        new KeyGesture(Key.Return, ModifierKeys.None),
        new KeyGesture(Key.Return, ModifierKeys.Shift),
        new KeyGesture(Key.Tab,    ModifierKeys.None),
        new KeyGesture(Key.Tab,    ModifierKeys.Shift),

        // Navegación de caret (CaretNavigationCommandHandler) — un plugin difícilmente
        // pediría una flecha sola, pero KeyGestureConverter SÍ la parsea (son "teclas
        // especiales" válidas sin modificador para WPF), así que quedan reservadas igual.
        new KeyGesture(Key.Left,  ModifierKeys.None),
        new KeyGesture(Key.Left,  ModifierKeys.Shift),
        new KeyGesture(Key.Left,  ModifierKeys.Control),
        new KeyGesture(Key.Left,  ModifierKeys.Control | ModifierKeys.Shift),
        new KeyGesture(Key.Right, ModifierKeys.None),
        new KeyGesture(Key.Right, ModifierKeys.Shift),
        new KeyGesture(Key.Right, ModifierKeys.Control),
        new KeyGesture(Key.Right, ModifierKeys.Control | ModifierKeys.Shift),
        new KeyGesture(Key.Up,    ModifierKeys.None),
        new KeyGesture(Key.Up,    ModifierKeys.Shift),
        new KeyGesture(Key.Down,  ModifierKeys.None),
        new KeyGesture(Key.Down,  ModifierKeys.Shift),
        new KeyGesture(Key.Next,    ModifierKeys.None),   // Av Pág / Page Down
        new KeyGesture(Key.Next,    ModifierKeys.Shift),
        new KeyGesture(Key.PageUp,  ModifierKeys.None),
        new KeyGesture(Key.PageUp,  ModifierKeys.Shift),
        new KeyGesture(Key.Home,    ModifierKeys.None),
        new KeyGesture(Key.Home,    ModifierKeys.Shift),
        new KeyGesture(Key.Home,    ModifierKeys.Control),
        new KeyGesture(Key.Home,    ModifierKeys.Control | ModifierKeys.Shift),
        new KeyGesture(Key.End,     ModifierKeys.None),
        new KeyGesture(Key.End,     ModifierKeys.Shift),
        new KeyGesture(Key.End,     ModifierKeys.Control),
        new KeyGesture(Key.End,     ModifierKeys.Control | ModifierKeys.Shift),
    };

    // ─── MenuOnlyClaims: etiquetas de MainWindow.xaml sin binding real detrás ────────

    /// <summary>MainWindow.xaml:245 — "Abrir vault…". Solo InputGestureText, no hace nada hoy.</summary>
    public static readonly KeyGesture MenuOpenVault = new(Key.O, ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>MainWindow.xaml:252 — "Exportar vista a PNG…". Solo InputGestureText, no hace nada hoy.</summary>
    public static readonly KeyGesture MenuExportPng = new(Key.P, ModifierKeys.Control | ModifierKeys.Shift);

    public static readonly IReadOnlyList<KeyGesture> MenuOnlyClaims = new[]
    {
        MenuOpenVault, MenuExportPng
    };
}

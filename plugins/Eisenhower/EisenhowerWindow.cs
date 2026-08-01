using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MarkdownVault.PluginSdk;

namespace MarkdownVault.Plugin.Eisenhower;

/// <summary>
/// Ventana única de gestión de tareas Eisenhower (pivote de UX, Change 2b): reemplaza el
/// modal de solo-captura (<c>CaptureModal</c>, retirado) por una grilla 2x2 completa donde
/// se agregan, marcan como hechas y eliminan tareas in-place. C#-only (sin XAML compilado) —
/// mismo patrón ALC-safe que el modal que reemplaza (nunca se retiene fuera de
/// <see cref="Window.ShowDialog"/>). Ventana delgada a propósito: TODA la lógica de negocio
/// (cuadrante, alta, toggle, baja, JSON) vive en <see cref="TaskStore"/> — esta clase solo
/// junta input, llama a TaskStore y persiste vía <see cref="IPluginStorage"/>.
/// </summary>
public sealed class EisenhowerWindow : Window
{
    private const string TasksFileName = "tasks.json";

    /// <summary>
    /// (urgent, important, acento claro, acento oscuro, fondo claro, fondo oscuro) por
    /// cuadrante — aproximación en hex de la paleta oklch de <see cref="EisenhowerCss"/>.
    /// Orden fijo: Hacer ahora, Planificar, Delegar, Eliminar (layout clásico 2x2).
    /// </summary>
    private static readonly (bool Urgent, bool Important, string AccentLight, string AccentDark, string BgLight, string BgDark)[] QuadrantStyles =
    {
        (true,  true,  "#E5604D", "#FF7A68", "#FDECEA", "#3A2420"), // Hacer ahora — coral
        (false, true,  "#4A90D9", "#6FA8E8", "#EAF2FB", "#1F3350"), // Planificar — azul
        (true,  false, "#D9A73B", "#E8BE5C", "#FDF6E3", "#3D3218"), // Delegar — ámbar
        (false, false, "#9098A0", "#A9B0B8", "#F2F3F4", "#33363A"), // Eliminar — gris
    };

    private readonly IPluginStorage _storage;
    private readonly IHostServices _host;
    private readonly bool _isDark;
    private readonly Brush _fg;
    private readonly Brush _mutedFg;

    private readonly Border _banner;
    private readonly TextBlock _bannerText;
    private readonly TextBox _titleBox;
    private readonly CheckBox _urgentBox;
    private readonly CheckBox _importantBox;
    private readonly Button _addButton;
    private readonly Grid _quadrantsGrid;
    private readonly Border _completedSection;
    private readonly TextBlock _completedHeader;
    private readonly TabControl _completedTabs;

    private IReadOnlyList<TaskItem> _tasks = Array.Empty<TaskItem>();
    private bool _readOnly;

    public EisenhowerWindow(IPluginStorage storage, IHostServices host)
    {
        _storage = storage;
        _host = host;
        _isDark = host.IsDarkTheme;
        var isDark = _isDark;
        _fg = new SolidColorBrush(ColorFromHex(isDark ? "#EEEEEE" : "#111111"));
        _mutedFg = new SolidColorBrush(ColorFromHex(isDark ? "#9AA0A6" : "#5F6368"));

        Title = "Tareas Eisenhower";
        Width = 900;
        Height = 640;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(ColorFromHex(isDark ? "#1E1E1E" : "#FFFFFF"));

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // banner
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // addBar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // legend
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // quadrants
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // completadas (historial)

        _bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        _banner = new Border
        {
            Background = new SolidColorBrush(ColorFromHex("#C0392B")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed,
            Child = _bannerText
        };
        Grid.SetRow(_banner, 0);
        root.Children.Add(_banner);

        var addBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleBox = new TextBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(6),
        };
        _titleBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddTask();
            }
        };
        Grid.SetColumn(_titleBox, 0);
        addBar.Children.Add(_titleBox);

        _urgentBox = new CheckBox
        {
            Content = "¿Urgente?",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = _fg
        };
        Grid.SetColumn(_urgentBox, 1);
        addBar.Children.Add(_urgentBox);

        _importantBox = new CheckBox
        {
            Content = "¿Importante?",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = _fg
        };
        Grid.SetColumn(_importantBox, 2);
        addBar.Children.Add(_importantBox);

        _addButton = new Button { Content = "Agregar", Padding = new Thickness(12, 6, 12, 6) };
        _addButton.Click += (_, _) => AddTask();
        Grid.SetColumn(_addButton, 3);
        addBar.Children.Add(_addButton);

        Grid.SetRow(addBar, 1);
        root.Children.Add(addBar);

        var legend = BuildLegend();
        Grid.SetRow(legend, 2);
        root.Children.Add(legend);

        _quadrantsGrid = new Grid();
        _quadrantsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _quadrantsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _quadrantsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _quadrantsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_quadrantsGrid, 3);
        root.Children.Add(_quadrantsGrid);

        var completedOuter = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        _completedHeader = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = _mutedFg,
            Margin = new Thickness(0, 0, 0, 6),
        };
        completedOuter.Children.Add(_completedHeader);

        _completedTabs = BuildCompletedTabControl();

        _completedSection = new Border
        {
            Background = new SolidColorBrush(ColorFromHex(isDark ? "#262626" : "#F5F5F5")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(4, 0, 4, 0),
            Child = _completedTabs,
        };
        completedOuter.Children.Add(_completedSection);

        Grid.SetRow(completedOuter, 4);
        root.Children.Add(completedOuter);

        Content = root;

        Loaded += (_, _) => LoadAndRender();
    }

    /// <summary>
    /// Mini-tabla de ayuda (texto secundario, no interactiva) que explica cómo las dos
    /// casillas urgente/importante del add bar determinan el cuadrante destino. Se arma a
    /// partir de <see cref="QuadrantStyles"/> para no duplicar el orden de cuadrantes.
    /// </summary>
    private FrameworkElement BuildLegend()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < QuadrantStyles.Length + 1; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddLegendCell(grid, 0, 0, "Cuadrante", bold: true);
        AddLegendCell(grid, 0, 1, "urgente", bold: true, center: true);
        AddLegendCell(grid, 0, 2, "importante", bold: true, center: true);

        for (var i = 0; i < QuadrantStyles.Length; i++)
        {
            var (urgent, important, _, _, _, _) = QuadrantStyles[i];
            var label = TaskStore.GetQuadrant(urgent, important);
            AddLegendCell(grid, i + 1, 0, label, bold: false);
            AddLegendCell(grid, i + 1, 1, urgent ? "✓" : "✗", bold: false, center: true);
            AddLegendCell(grid, i + 1, 2, important ? "✓" : "✗", bold: false, center: true);
        }

        return new Border
        {
            Padding = new Thickness(2, 0, 2, 10),
            Child = grid
        };
    }

    /// <summary>Celda individual de la mini-tabla de la leyenda: texto pequeño y muted.</summary>
    private void AddLegendCell(Grid grid, int row, int column, string text, bool bold, bool center = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = _mutedFg,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(column == 0 ? 0 : 16, 0, 0, 2),
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    /// <summary>Lee tasks.json (si existe) y renderiza el estado inicial.</summary>
    private void LoadAndRender()
    {
        var raw = _storage.Exists(TasksFileName)
            ? File.ReadAllText(Path.Combine(_storage.RootPath, TasksFileName))
            : null;

        ApplyLoadResult(TaskStore.Load(raw));
    }

    /// <summary>
    /// Aplica un <see cref="LoadResult"/> al estado de la ventana. En Corrupt/NewerVersion
    /// muestra el banner de error y deshabilita toda mutación (nunca se sobrescribe un
    /// archivo ilegible) — mismo criterio que <c>TaskStore.Capture</c> aplicaba en el modal.
    /// </summary>
    private void ApplyLoadResult(LoadResult result)
    {
        _tasks = result.Tasks;
        _readOnly = result.Status is LoadStatus.Corrupt or LoadStatus.NewerVersion;

        if (_readOnly)
        {
            _bannerText.Text = result.Status == LoadStatus.NewerVersion
                ? "tasks.json fue creado por una versión más nueva de este plugin. Edición deshabilitada para no perder datos."
                : "tasks.json no se pudo leer (formato inválido). Edición deshabilitada para no perder datos.";
            _banner.Visibility = Visibility.Visible;
        }
        else
        {
            _banner.Visibility = Visibility.Collapsed;
        }

        _titleBox.IsEnabled = !_readOnly;
        _urgentBox.IsEnabled = !_readOnly;
        _importantBox.IsEnabled = !_readOnly;
        _addButton.IsEnabled = !_readOnly;

        RenderQuadrants();
        RenderCompleted();
    }

    /// <summary>
    /// Reconstruye por completo los 4 cuadrantes a partir de las tareas ACTIVAS
    /// (<see cref="TaskStore.Active"/>) de <see cref="_tasks"/>. Las tareas completadas ya
    /// no ocupan celda de cuadrante — viven en la sección "Completadas" (ver
    /// <see cref="RenderCompleted"/>).
    /// </summary>
    private void RenderQuadrants()
    {
        _quadrantsGrid.Children.Clear();

        var active = TaskStore.Active(_tasks);

        for (var i = 0; i < QuadrantStyles.Length; i++)
        {
            var (urgent, important, accentLight, accentDark, bgLight, bgDark) = QuadrantStyles[i];
            var label = TaskStore.GetQuadrant(urgent, important);
            var accentBrush = new SolidColorBrush(ColorFromHex(_isDark ? accentDark : accentLight));
            var bgBrush = new SolidColorBrush(ColorFromHex(_isDark ? bgDark : bgLight));

            var panel = new StackPanel { Margin = new Thickness(4) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = accentBrush
            });

            foreach (var task in active.Where(t => t.Urgent == urgent && t.Important == important))
            {
                panel.Children.Add(BuildTaskRow(task));
            }

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };

            var border = new Border
            {
                Background = bgBrush,
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4),
                Padding = new Thickness(10),
                Child = scroll
            };

            Grid.SetRow(border, i < 2 ? 0 : 1);
            Grid.SetColumn(border, i % 2);
            _quadrantsGrid.Children.Add(border);
        }
    }

    /// <summary>
    /// Reconstruye la sección "Completadas" (historial bajo la grilla 2x2) a partir de
    /// <see cref="TaskStore.CompletedByMonth"/> — una pestaña por mes, la del mes más
    /// reciente primero y seleccionada. Sin tareas completadas: colapsa la sección entera
    /// (nada que mostrar, no vale la pena ocupar espacio con un mensaje).
    /// </summary>
    private void RenderCompleted()
    {
        _completedTabs.Items.Clear();

        var groups = TaskStore.CompletedByMonth(_tasks);

        if (groups.Count == 0)
        {
            _completedSection.Visibility = Visibility.Collapsed;
            _completedHeader.Visibility = Visibility.Collapsed;
            return;
        }

        _completedSection.Visibility = Visibility.Visible;
        _completedHeader.Visibility = Visibility.Visible;
        _completedHeader.Text = $"Completadas ({groups.Sum(g => g.Tasks.Count)})";

        foreach (var group in groups)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            foreach (var task in group.Tasks)
            {
                panel.Children.Add(BuildCompletedRow(task));
            }

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 160,
                Content = panel,
            };

            var tabItem = new TabItem
            {
                Header = group.Label,
                Content = scroll,
                Style = BuildTabItemStyle(),
            };
            _completedTabs.Items.Add(tabItem);
        }

        // Grupos ya vienen ordenados mes más reciente primero (TaskStore.CompletedByMonth) —
        // seleccionar el índice 0 selecciona ese mes.
        _completedTabs.SelectedIndex = 0;
    }

    /// <summary>
    /// Crea el <see cref="TabControl"/> vacío que aloja el historial "Completadas" (poblado por
    /// <see cref="RenderCompleted"/>, una pestaña por mes). El template por defecto de
    /// <see cref="TabControl"/>/<see cref="TabItem"/> en WPF trae chrome fijo del tema de
    /// Windows que casi siempre se ve claro/blanco y ignora <c>Background</c>/<c>Foreground</c>
    /// — por eso se define un <see cref="ControlTemplate"/> propio, minimal (un
    /// <see cref="DockPanel"/>: tira de pestañas arriba, contenido de la seleccionada abajo),
    /// consistente con los colores dark-aware del resto de la ventana. Cada
    /// <see cref="TabItem"/> agregado en <see cref="RenderCompleted"/> recibe además su propio
    /// estilo (ver <see cref="BuildTabItemStyle"/>).
    /// </summary>
    private static TabControl BuildCompletedTabControl()
    {
        var template = new ControlTemplate(typeof(TabControl));

        var dockPanel = new FrameworkElementFactory(typeof(DockPanel));

        var tabPanel = new FrameworkElementFactory(typeof(TabPanel));
        tabPanel.SetValue(DockPanel.DockProperty, Dock.Top);
        tabPanel.SetValue(Panel.IsItemsHostProperty, true);
        tabPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
        dockPanel.AppendChild(tabPanel);

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.Name = "PART_SelectedContentHost";
        contentPresenter.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        dockPanel.AppendChild(contentPresenter);

        template.VisualTree = dockPanel;

        return new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Template = template,
        };
    }

    /// <summary>
    /// Estilo por-<see cref="TabItem"/> del historial "Completadas": pill redondeada con el
    /// header (el <see cref="MonthGroup.Label"/> del mes) centrado; acentuada y en negrita
    /// cuando está seleccionada, muted cuando no — mismos <see cref="_fg"/>/<see cref="_mutedFg"/>
    /// dark-aware que el resto de la ventana. Necesita su propio <see cref="ControlTemplate"/>
    /// (Border + ContentPresenter) por la misma razón que <see cref="BuildCompletedTabControl"/>:
    /// el template por defecto de <see cref="TabItem"/> ignora Background/Foreground en la
    /// mayoría de los temas de Windows.
    /// </summary>
    private Style BuildTabItemStyle()
    {
        var template = new ControlTemplate(typeof(TabItem));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "TabItemBorder";
        border.SetValue(Border.PaddingProperty, new Thickness(12, 6, 12, 6));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 6, 0));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(TextElement.ForegroundProperty, _mutedFg);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        template.VisualTree = border;

        var selectedBg = new SolidColorBrush(ColorFromHex(_isDark ? "#3A3A3A" : "#E6E6E6"));

        var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, selectedBg, "TabItemBorder"));
        selectedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, _fg, "TabItemBorder"));
        selectedTrigger.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.SemiBold, "TabItemBorder"));
        template.Triggers.Add(selectedTrigger);

        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));

        return style;
    }

    /// <summary>
    /// Fila de una tarea completada dentro de la sección "Completadas": checkbox tildado
    /// que, al destildarse, llama a <see cref="TaskStore.ToggleDone"/> (la tarea vuelve a
    /// estar activa y reaparece en su cuadrante); título en estilo muted/tachado; y borrar
    /// (✕) vía <see cref="TaskStore.Remove"/>. A diferencia de <see cref="BuildTaskRow"/>,
    /// no tiene toggles U/I ni botón de enlace — el historial es de solo-lectura salvo
    /// restaurar/borrar.
    /// </summary>
    private FrameworkElement BuildCompletedRow(TaskItem task)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // done
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // title
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // delete

        var doneBox = new CheckBox
        {
            IsChecked = true,
            IsEnabled = !_readOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        doneBox.Click += (_, _) => ToggleTask(task.Id);
        Grid.SetColumn(doneBox, 0);
        row.Children.Add(doneBox);

        var titleText = new TextBlock
        {
            Text = task.Title,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _mutedFg,
            Opacity = 0.7,
            TextDecorations = TextDecorations.Strikethrough,
        };
        Grid.SetColumn(titleText, 1);
        row.Children.Add(titleText);

        var deleteButton = new Button
        {
            Content = "✕",
            IsEnabled = !_readOnly,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Click += (_, _) => RemoveTask(task.Id);
        Grid.SetColumn(deleteButton, 2);
        row.Children.Add(deleteButton);

        return row;
    }

    /// <summary>
    /// Fila de una tarea: checkbox "hecha", título (tachado si Done), toggles
    /// Urgente/Importante (siempre visibles — reclasifican in-place), enlace a documento
    /// (🔗, ver <see cref="BuildLinkButton"/>) y borrar (✕). Cambiar un toggle llama a
    /// <see cref="TaskStore.SetClassification"/>, persiste y reconstruye la grilla completa:
    /// la tarea "salta" a su nueva celda de cuadrante.
    /// </summary>
    private FrameworkElement BuildTaskRow(TaskItem task)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // done
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // title
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // urgent toggle
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // important toggle
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // link
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // delete

        var doneBox = new CheckBox
        {
            IsChecked = task.Done,
            IsEnabled = !_readOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        doneBox.Click += (_, _) => ToggleTask(task.Id);
        Grid.SetColumn(doneBox, 0);
        row.Children.Add(doneBox);

        var titleCell = BuildTitleCell(task);
        Grid.SetColumn(titleCell, 1);
        row.Children.Add(titleCell);

        var urgentBox = new CheckBox
        {
            Content = "U",
            ToolTip = "¿Urgente?",
            IsChecked = task.Urgent,
            IsEnabled = !_readOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = _fg
        };
        urgentBox.Click += (_, _) => Reclassify(task.Id, urgentBox.IsChecked == true, task.Important);
        Grid.SetColumn(urgentBox, 2);
        row.Children.Add(urgentBox);

        var importantBox = new CheckBox
        {
            Content = "I",
            ToolTip = "¿Importante?",
            IsChecked = task.Important,
            IsEnabled = !_readOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = _fg
        };
        importantBox.Click += (_, _) => Reclassify(task.Id, task.Urgent, importantBox.IsChecked == true);
        Grid.SetColumn(importantBox, 3);
        row.Children.Add(importantBox);

        var linkButton = BuildLinkButton(task);
        Grid.SetColumn(linkButton, 4);
        row.Children.Add(linkButton);

        var deleteButton = new Button
        {
            Content = "✕",
            IsEnabled = !_readOnly,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Click += (_, _) => RemoveTask(task.Id);
        Grid.SetColumn(deleteButton, 5);
        row.Children.Add(deleteButton);

        return row;
    }

    /// <summary>
    /// Botón de enlace (🔗) de una fila. Sin <see cref="TaskItem.LinkPath"/>: click abre un
    /// <see cref="OpenFileDialog"/> (ver <see cref="PickLink"/>) para vincular un documento del
    /// vault. Con enlace: click abre el documento vinculado vía
    /// <see cref="IHostServices.OpenVaultFile"/>, y un menú contextual (clic derecho) ofrece
    /// "Cambiar enlace…" / "Quitar enlace" — el botón nunca cambia de posición en la fila, solo
    /// de estilo (muted/sin tooltip vs. acentuado/tooltip con el nombre del archivo).
    /// </summary>
    private FrameworkElement BuildLinkButton(TaskItem task)
    {
        var hasLink = task.LinkPath is not null;

        var linkButton = new Button
        {
            Content = "🔗",
            IsEnabled = !_readOnly,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight = hasLink ? FontWeights.Bold : FontWeights.Normal,
            Foreground = hasLink ? _fg : _mutedFg,
            ToolTip = hasLink
                ? $"Abrir documento vinculado: {Path.GetFileName(task.LinkPath)}"
                : "Vincular un documento del vault",
        };

        linkButton.Click += (_, _) =>
        {
            if (task.LinkPath is null)
            {
                PickLink(task.Id);
            }
            else
            {
                _host.OpenVaultFile(task.LinkPath);
            }
        };

        if (hasLink)
        {
            var contextMenu = new ContextMenu();

            var changeItem = new MenuItem { Header = "Cambiar enlace…", IsEnabled = !_readOnly };
            changeItem.Click += (_, _) => PickLink(task.Id);
            contextMenu.Items.Add(changeItem);

            var removeItem = new MenuItem { Header = "Quitar enlace", IsEnabled = !_readOnly };
            removeItem.Click += (_, _) => RemoveLink(task.Id);
            contextMenu.Items.Add(removeItem);

            linkButton.ContextMenu = contextMenu;
        }

        return linkButton;
    }

    /// <summary>
    /// Celda de título editable in-place: un <see cref="TextBlock"/> (tachado si Done) que se
    /// reemplaza por un <see cref="TextBox"/> editable al hacer doble clic o al pulsar el botón
    /// "✎". Enter o perder foco confirma (<see cref="TaskStore.SetTitle"/> → persistir →
    /// reconstruir grilla vía <see cref="Persist"/>); Escape o dejar el campo en blanco cancela
    /// sin persistir. Ambos controles viven superpuestos en la misma celda de grilla —
    /// solo uno es visible a la vez.
    /// </summary>
    private FrameworkElement BuildTitleCell(TaskItem task)
    {
        var cell = new Grid();
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = task.Title,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _fg,
            Opacity = task.Done ? 0.6 : 1.0,
            TextDecorations = task.Done ? TextDecorations.Strikethrough : null,
            Cursor = _readOnly ? Cursors.Arrow : Cursors.IBeam,
        };

        var titleBox = new TextBox
        {
            Text = task.Title,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2),
            Visibility = Visibility.Collapsed,
        };

        void BeginEdit()
        {
            if (_readOnly)
            {
                return;
            }

            titleBox.Text = task.Title;
            titleText.Visibility = Visibility.Collapsed;
            titleBox.Visibility = Visibility.Visible;
            titleBox.Focus();
            titleBox.SelectAll();
        }

        void CommitEdit()
        {
            if (titleBox.Visibility != Visibility.Visible)
            {
                return; // ya confirmado o cancelado — evita doble commit (Enter dispara LostFocus)
            }

            var newTitle = titleBox.Text;
            titleBox.Visibility = Visibility.Collapsed;
            titleText.Visibility = Visibility.Visible;

            if (string.IsNullOrWhiteSpace(newTitle))
            {
                return; // en blanco: cancela/revierte — TaskStore ya rechaza esto, no hace falta llamar
            }

            SetTaskTitle(task.Id, newTitle);
        }

        void CancelEdit()
        {
            titleBox.Text = task.Title;
            titleBox.Visibility = Visibility.Collapsed;
            titleText.Visibility = Visibility.Visible;
        }

        titleText.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                BeginEdit();
            }
        };

        titleBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEdit();
                e.Handled = true;
            }
        };
        titleBox.LostFocus += (_, _) => CommitEdit();

        Grid.SetColumn(titleText, 0);
        Grid.SetColumn(titleBox, 0);
        cell.Children.Add(titleText);
        cell.Children.Add(titleBox);

        var editButton = new Button
        {
            Content = "✎",
            ToolTip = "Editar título",
            IsEnabled = !_readOnly,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = _mutedFg,
        };
        editButton.Click += (_, _) => BeginEdit();
        Grid.SetColumn(editButton, 1);
        cell.Children.Add(editButton);

        return cell;
    }

    private void AddTask()
    {
        if (_readOnly)
        {
            return;
        }

        var result = TaskStore.Add(_tasks, _titleBox.Text, _urgentBox.IsChecked == true, _importantBox.IsChecked == true);
        if (!result.Ok)
        {
            // Título en blanco: no-op con una pista sutil (borde rojo momentáneo) — sin
            // popups ni lógica adicional, TaskStore ya decidió que no hay nada que hacer.
            _titleBox.BorderBrush = Brushes.IndianRed;
            return;
        }

        _titleBox.ClearValue(TextBox.BorderBrushProperty);
        Persist(result.Tasks, result.Json!);
        _titleBox.Clear();
        _urgentBox.IsChecked = false;
        _importantBox.IsChecked = false;
    }

    private void ToggleTask(Guid id)
    {
        if (_readOnly)
        {
            return;
        }

        var result = TaskStore.ToggleDone(_tasks, id);
        Persist(result.Tasks, result.Json);
    }

    private void RemoveTask(Guid id)
    {
        if (_readOnly)
        {
            return;
        }

        var result = TaskStore.Remove(_tasks, id);
        Persist(result.Tasks, result.Json);
    }

    /// <summary>
    /// Reclasifica una tarea existente (toggle Urgente/Importante de su fila): delega el
    /// cálculo a <see cref="TaskStore.SetClassification"/>, persiste y reconstruye la grilla —
    /// la tarea aparece en su nueva celda de cuadrante. Toda la lógica vive en TaskStore, esta
    /// ventana solo junta el input y refresca.
    /// </summary>
    private void Reclassify(Guid id, bool urgent, bool important)
    {
        if (_readOnly)
        {
            return;
        }

        var result = TaskStore.SetClassification(_tasks, id, urgent, important);
        Persist(result.Tasks, result.Json);
    }

    /// <summary>
    /// Confirma la edición inline del título de una tarea: delega a
    /// <see cref="TaskStore.SetTitle"/>, persiste y reconstruye la grilla. Toda la lógica vive
    /// en TaskStore — esta ventana solo junta el input del <c>TextBox</c> de edición y refresca.
    /// </summary>
    private void SetTaskTitle(Guid id, string title)
    {
        if (_readOnly)
        {
            return;
        }

        var result = TaskStore.SetTitle(_tasks, id, title);
        Persist(result.Tasks, result.Json);
    }

    /// <summary>
    /// Handler del botón 🔗 cuando la tarea todavía no tiene enlace (y de "Cambiar enlace…"
    /// en el menú contextual, que reusa el mismo flujo). Abre un <see cref="OpenFileDialog"/>
    /// anclado en <see cref="IHostServices.VaultRoot"/>, filtrado a documentos de vault
    /// (.md/.markdown/.txt/.html). Si no hay vault abierto, o el archivo elegido queda FUERA
    /// del vault (relativización con ".." — nunca confiamos en que el diálogo respete
    /// InitialDirectory), aborta con <see cref="IHostServices.ShowStatus"/> y no toca
    /// <see cref="_tasks"/>. Éxito: <see cref="TaskStore.SetLink"/> → <see cref="Persist"/>.
    /// </summary>
    private void PickLink(Guid id)
    {
        if (_readOnly)
        {
            return;
        }

        var vaultRoot = _host.VaultRoot;
        if (string.IsNullOrEmpty(vaultRoot))
        {
            _host.ShowStatus("No hay un vault abierto: no se puede vincular un documento.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            InitialDirectory = vaultRoot,
            Filter = "Documentos (*.md;*.markdown;*.txt;*.html)|*.md;*.markdown;*.txt;*.html|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var relative = Path.GetRelativePath(vaultRoot, dialog.FileName);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            _host.ShowStatus("El archivo elegido está fuera del vault. Elegí un archivo dentro del vault.");
            return;
        }

        var result = TaskStore.SetLink(_tasks, id, relative);
        Persist(result.Tasks, result.Json);
    }

    /// <summary>
    /// Handler de "Quitar enlace" en el menú contextual del botón 🔗: delega a
    /// <see cref="TaskStore.SetLink"/> con <c>null</c> (desvincular), persiste y refresca.
    /// </summary>
    private void RemoveLink(Guid id)
    {
        if (_readOnly)
        {
            return;
        }

        var result = TaskStore.SetLink(_tasks, id, null);
        Persist(result.Tasks, result.Json);
    }

    /// <summary>
    /// Escribe tasks.json y refresca la grilla in-place. Mismo patrón anti-deadlock que
    /// <c>EisenhowerPlugin.CaptureTask</c> usaba: <c>Task.Run</c> escapa el
    /// SynchronizationContext de WPF antes de bloquear con <c>GetAwaiter().GetResult()</c>.
    /// </summary>
    private void Persist(IReadOnlyList<TaskItem> tasks, string json)
    {
        Task.Run(() => _storage.WriteTextAsync(TasksFileName, json)).GetAwaiter().GetResult();
        _tasks = tasks;
        RenderQuadrants();
        RenderCompleted();
    }

    private static Color ColorFromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

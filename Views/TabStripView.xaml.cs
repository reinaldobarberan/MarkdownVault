using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkdownVault.Helpers;
using MarkdownVault.Models;
using MarkdownVault.ViewModels;

namespace MarkdownVault.Views;

/// <summary>
/// Reusable file-tab strip bound to an <see cref="EditorGroupViewModel"/> (its ambient
/// DataContext). Extracted from <see cref="EditorView"/> so the same strip can appear both
/// inside each editor pane and above the preview in Solo visor. Click switches tabs,
/// middle-click closes them, and dragging one sideways reorders it within this strip — the
/// group's commands and its <see cref="EditorGroupViewModel.OpenTabs"/> collection do the
/// actual work.
/// </summary>
public partial class TabStripView : UserControl
{
    public TabStripView()
    {
        InitializeComponent();
    }

    private EditorGroupViewModel? Group => DataContext as EditorGroupViewModel;

    // ─── Drag-to-reorder state ─────────────────────────────────────────────────────────────
    // The tab under the last left-press, and where that press landed. A press alone is NOT a
    // drag: the gesture only becomes one once the pointer travels past the system drag
    // threshold, so an ordinary click-to-switch never nudges the tab order by a pixel of
    // hand tremor.
    private OpenTab? _pressedTab;
    private Point    _pressPoint;
    private double   _grabOffset;
    private bool     _isDragging;

    /// <summary>Left-click on a tab → switch to it, and arm a possible reorder drag.</summary>
    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Group is null) return;
        if (sender is FrameworkElement fe && fe.DataContext is OpenTab tab)
        {
            Group.SwitchToTabCommand.Execute(tab);

            // Arming happens after the switch so a drag always carries the tab the user can
            // see is selected. Presses on the close button never reach here — ButtonBase marks
            // the event handled — so the close affordance stays draggable-free.
            _pressedTab = tab;
            _pressPoint = e.GetPosition(TabsHost);
            _grabOffset = e.GetPosition(fe).X;   // grip kept for the whole gesture
            _isDragging = false;

            e.Handled = true;
        }
    }

    /// <summary>Middle-click on a tab → close it.</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Group is null) return;
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement fe && fe.DataContext is OpenTab tab)
        {
            Group.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Live reorder while the pointer is down: each move re-asks
    /// <see cref="TabReorderCalculator"/> which slot the cursor is over and, when that is not
    /// the tab's current slot, moves it there. Reordering the bound
    /// <see cref="EditorGroupViewModel.OpenTabs"/> collection IS the drag feedback — the tab
    /// travels under the cursor and its neighbours step aside — so no ghost adorner is needed.
    /// </summary>
    private void Strip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTab is null) return;

        // The button can come up outside our capture (alt-tab, a dialog stealing focus), in
        // which case no MouseLeftButtonUp ever arrives — treat any move without the button as
        // the end of the gesture rather than reordering on a stale press.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        var group = Group;
        if (group is null) { EndDrag(); return; }

        var pos = e.GetPosition(TabsHost);

        if (!_isDragging)
        {
            if (Math.Abs(pos.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance)
                return;

            _isDragging = true;
            CaptureMouse();   // keeps the moves coming when the pointer leaves the strip
        }

        int from = group.OpenTabs.IndexOf(_pressedTab);
        if (from < 0) { EndDrag(); return; }   // tab closed mid-drag

        var spans = MeasureTabs();
        if (spans.Count != group.OpenTabs.Count) return;   // containers not laid out yet

        int to = TabReorderCalculator.TargetIndex(spans, from, pos.X, _grabOffset);
        if (to != from) group.OpenTabs.Move(from, to);
    }

    private void Strip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

    /// <summary>Capture lost to anything else (a menu, another window) → drop the gesture.</summary>
    private void Strip_LostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_isDragging && IsMouseCaptured) ReleaseMouseCapture();
        _pressedTab = null;
        _isDragging = false;
    }

    /// <summary>
    /// Current horizontal placement of every rendered tab, in <c>TabsHost</c> coordinates.
    /// Returns an empty list when any container is missing — the strip lives inside a
    /// ScrollViewer, and asking for geometry that has not been realised yet would produce a
    /// drop slot computed from zero-width phantoms.
    /// </summary>
    private List<TabSpan> MeasureTabs()
    {
        var spans = new List<TabSpan>(TabsHost.Items.Count);

        for (int i = 0; i < TabsHost.Items.Count; i++)
        {
            if (TabsHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c)
                return new List<TabSpan>();

            var origin = c.TranslatePoint(new Point(0, 0), TabsHost);
            spans.Add(new TabSpan(origin.X, c.ActualWidth));
        }

        return spans;
    }
}

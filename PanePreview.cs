using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace FileExplorer;

/// PREVIEW-41: where this pane hosts its preview card. Docked, the card sits inside the pane under the tab row, so
/// opening, closing or resizing it never changes the tabs: only the path bar, list and footer get narrower. Picture
/// in picture, it is a corner card over the list. CORE-36: a double-click opens what its first click pressed, even
/// when the list's layout moved in between.
public partial class PaneView
{
    internal const double PreviewListMin = 250, PreviewCardMin = 200;
    const double SlotChrome = 16; // the divider strip on the left and the card's inset on the right
    Entry pressed;
    long pressedAt, listMovedAt;

    void InitializePanePreview()
    {
        SizeChanged += (_, _) => { if (PreviewSlot.Visibility == Visibility.Visible) FitPreviewSlot(); };
        PipLayer.SizeChanged += (_, _) => FitPips();
        List.SizeChanged += (_, _) => listMovedAt = Environment.TickCount64;
        List.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (InTextBox(e.OriginalSource)) return;
            var row = FindRow(e.OriginalSource as DependencyObject);
            long now = Environment.TickCount64;
            if (e.ClickCount == 2 && pressed != null && row != pressed && listMovedAt >= pressedAt
                && now - pressedAt <= GetDoubleClickTime() && Items.Contains(pressed))
            {
                // the rows moved under the pointer between the two clicks: the second one belongs to the first's item
                var item = pressed; pressed = null;
                List.SelectedItem = item; OpenSelected();
                e.Handled = true;
                return;
            }
            pressed = e.ClickCount == 1 ? row : null;
            pressedAt = now;
        };
    }

    internal static void Detach(FrameworkElement card)
    {
        if (card.Parent is Panel panel) panel.Children.Remove(card);
        else if (card.Parent is Window window) window.Content = null;
    }

    /// Puts the card into this pane: docked in the slot under the tabs, or as picture in picture over the list.
    internal void HostPreview(PreviewCard card, bool docked)
    {
        Detach(card);
        card.Width = card.Height = double.NaN;
        card.LayoutTransform = null;
        if (docked)
        {
            Grid.SetColumn(card, 1);
            card.Margin = new Thickness(0, 4, 8, 8);
            card.HorizontalAlignment = HorizontalAlignment.Stretch; card.VerticalAlignment = VerticalAlignment.Stretch;
            PreviewSlot.Children.Add(card);
        }
        else
        {
            card.Margin = new Thickness(12);
            card.HorizontalAlignment = HorizontalAlignment.Right; card.VerticalAlignment = VerticalAlignment.Bottom;
            PipLayer.Children.Add(card); FitPip(card);
        }
        UpdatePreviewSlot();
    }

    /// The slot follows its card: shown with it, gone (and the list at full width again) without it.
    internal void UpdatePreviewSlot()
    {
        bool show = PreviewSlot.Children.OfType<PreviewCard>().Any(card => card.Visibility == Visibility.Visible);
        bool appearing = show && PreviewSlot.Visibility != Visibility.Visible;
        PreviewSlot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { PreviewSlot.BeginAnimation(WidthProperty, null); return; }
        FitPreviewSlot();
        if (appearing && Motion.On) // a short slide; the card itself fades in (MainWindow.SetPreviewVisible)
            PreviewSlot.BeginAnimation(WidthProperty, new DoubleAnimation(0, PreviewSlot.Width, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    double PreviewSlotMax => ActualWidth > 0 ? Math.Max(PreviewCardMin + SlotChrome, ActualWidth - PreviewListMin) : double.PositiveInfinity;

    void FitPreviewSlot() => PreviewSlot.Width = Math.Clamp(Settings.PreviewWidth + SlotChrome, PreviewCardMin + SlotChrome, PreviewSlotMax);

    /// The docked card's width as set (not mid-slide), the setting's meaning: the divider and inset are not part of it.
    internal double DockedPreviewWidth => PreviewSlot.Visibility == Visibility.Visible && PreviewSlot.ReadLocalValue(WidthProperty) is double width ? width - SlotChrome : 0;

    void PreviewDivider_DragDelta(object s, DragDeltaEventArgs e)
    {
        PreviewSlot.BeginAnimation(WidthProperty, null);
        PreviewSlot.Width = Math.Clamp(PreviewSlot.ActualWidth - e.HorizontalChange, PreviewCardMin + SlotChrome, PreviewSlotMax);
    }

    void PreviewDivider_DragCompleted(object s, DragCompletedEventArgs e)
    {
        Settings.PreviewWidth = PreviewSlot.Width - SlotChrome; Settings.Save();
    }

    void FitPips() { foreach (var card in PipLayer.Children.OfType<PreviewCard>()) FitPip(card); }

    /// Compact and inside this pane's list, however narrow the pane is.
    void FitPip(PreviewCard card)
    {
        double width = Math.Max(0, PipLayer.ActualWidth - 24), height = Math.Max(0, PipLayer.ActualHeight - 24);
        if (width <= 0 || height <= 0) return; // measured on the next size change
        card.Width = Math.Min(width, Math.Min(400, Math.Max(280, width * .45)));
        card.Height = Math.Min(height, Math.Min(320, Math.Max(220, height * .55)));
    }
}

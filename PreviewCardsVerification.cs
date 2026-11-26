#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    /// PREVIEW-41 and CORE-36 (S78): the preview card inside the pane, the cards of two panes, and the double-click guard.
    internal async Task VerifyPreviewCards41(string root, string folder, double previewWidth, Func<Task> settle, Action<bool, string> check)
    {
        // One pane: docked inside the pane, under its tab row. Enough tabs that the old column made them wrap.
        double oldWidth = Width, oldHeight = Height;
        SetSinglePane(true); Width = 1400; Height = 900; await settle();
        int tabsBefore = Left.TabCount; for (int i = 0; i < 8; i++) Left.OpenInBackgroundTab(root);
        Left.List.UnselectAll(); PreviewFollow(); await PreviewSession.DrainAsync(); await settle();
        double tabsHeight = Left.TabsArea.ActualHeight, tabsWidth = Left.TabsArea.ActualWidth, pathWidth = Left.PathBar.ActualWidth, listWidth = Left.List.ActualWidth;
        int rows = Left.TabStrip.Rows;
        Left.List.SelectedItem = Left.Items.Single(e => e.Path == folder); PreviewFollow(); await sidePreview.Completion; await settle();
        await Task.Delay(300); await settle(); // past the opening slide
        check(PreviewPane.Parent == Left.PreviewSlot && Left.TabsArea.ActualHeight == tabsHeight && Left.TabsArea.ActualWidth == tabsWidth && Left.TabStrip.Rows == rows,
            $"The docked preview leaves the tab row's width, rows and height unchanged ({Left.TabCount} tabs, {rows} row(s), {tabsHeight:0.#} high)");
        check(Left.PathBar.ActualWidth < pathWidth - previewWidth + 1 && Left.List.ActualWidth < listWidth - previewWidth + 1 && PreviewPane.TranslatePoint(new Point(), Left).Y >= Left.TabsArea.ActualHeight,
            "The path bar and list are pushed left; the card starts below the tabs");
        check(PreviewPane.CornerRadius.TopLeft == 10 && PreviewPane.BorderThickness.Left == 1 && PreviewPane.Margin == new Thickness(0, 4, 8, 8) && Left.PreviewSlot.ColumnDefinitions[0].Width.Value == 8,
            "The docked preview is a rounded edged card with an even inset and a resize strip");
        var divider = Left.PreviewDivider; double slot = Left.PreviewSlot.Width;
        divider.RaiseEvent(new DragDeltaEventArgs(-40, 0)); divider.RaiseEvent(new DragCompletedEventArgs(-40, 0, false)); await settle();
        check(Math.Abs(Left.PreviewSlot.Width - slot - 40) < 0.5 && Math.Abs(Settings.PreviewWidth - previewWidth - 40) < 0.5 && Left.TabsArea.ActualHeight == tabsHeight,
            "Dragging the card's edge resizes it, keeps the width and leaves the tabs alone");
        divider.RaiseEvent(new DragDeltaEventArgs(-5000, 0)); await settle();
        check(Left.ActualWidth - Left.PreviewSlot.ActualWidth >= PaneView.PreviewListMin - 0.5, "The list keeps its minimum width however far the card is dragged");
        divider.RaiseEvent(new DragDeltaEventArgs(Left.DockedPreviewWidth - previewWidth, 0)); divider.RaiseEvent(new DragCompletedEventArgs(0, 0, false)); await settle();
        check(Math.Abs(Settings.PreviewWidth - previewWidth) < 0.5, "The saved width returns to where it was");

        // CORE-36: the second press of a double-click opens what the first pressed, even when the rows moved in between.
        var other = Left.Items.First(e => e.Path != folder);
        var first = (ListViewItem)Left.List.ItemContainerGenerator.ContainerFromItem(Left.Items.Single(e => e.Path == folder));
        var second = (ListViewItem)Left.List.ItemContainerGenerator.ContainerFromItem(other);
        MouseButtonEventArgs Press(int count)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount)).SetValue(args, count);
            return args;
        }
        first.RaiseEvent(Press(1)); Left.PreviewSlot.Width += 30; Left.UpdateLayout();
        var again = Press(2); second.RaiseEvent(again);
        // opening a folder lists it asynchronously: wait for that navigation to finish
        for (int i = 0; i < 100 && !(Left.Listed && Left.Dir.Equals(folder, StringComparison.OrdinalIgnoreCase)); i++) { await settle(); await Task.Delay(50); }
        check(again.Handled && Left.Dir.Equals(folder, StringComparison.OrdinalIgnoreCase),
            $"A double-click whose rows moved between its presses opens the item its first press was on (handled {again.Handled}, opened {Left.Dir == folder})");
        await S1Verification.Navigate(Left, root);
        while (Left.TabCount > tabsBefore) Left.CloseTab(Left.ActiveTab + 1 < Left.TabCount ? Left.ActiveTab + 1 : Left.ActiveTab);

        // Two panes: a picture in picture card in each, each showing its own pane's selection.
        SetSinglePane(false); await S1Verification.Navigate(Right, root); await settle();
        Left.List.SelectedItem = Left.Items.Single(e => e.Path == folder); Right.List.SelectedItem = Right.Items.Single(e => e.Path == other.Path); PreviewFollow();
        await sidePreview.Completion; await secondary.Session.Completion; await settle();
        check(secondary != null && PreviewPane.Parent == Left.PipLayer && secondary.Parent == Right.PipLayer && PreviewPane.IsVisible && secondary.IsVisible && Left.PreviewSlot.Visibility == Visibility.Collapsed,
            "Two panes show a picture in picture card in each pane at the same time");
        check(sidePreview.Entry?.Path == folder && secondary.Session.Entry?.Path == other.Path && secondary.PreviewTitle.Text == other.Name,
            "Each card shows its own pane's selection, not only the focused pane's");
        check(!PreviewPipButton.IsVisible && !secondary.PreviewPipButton.IsVisible && secondary.PreviewPinButton.IsVisible && secondary.PreviewWindowButton.IsVisible && secondary.PreviewCloseButton.IsVisible,
            "Both cards keep pin, window and close; picture in picture is not offered where it is the only place");
        check(PreviewPane.ActualWidth <= Left.PipLayer.ActualWidth - 23 && secondary.ActualWidth <= Right.PipLayer.ActualWidth - 23 && secondary.ActualHeight <= Right.PipLayer.ActualHeight - 23,
            "Each card fits inside its own pane's list");
        secondary.PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Right.List.UnselectAll(); PreviewFollow(); await settle();
        check(secondary.Pinned && secondary.Session.Entry?.Path == other.Path && secondary.IsVisible && sidePreview.Entry?.Path == folder, "The right card's pin is its own");
        secondary.PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await settle();
        check(!secondary.Pinned && !secondary.IsVisible, "Unpinned, the right card follows its pane's empty selection");
        var right = secondary;
        Right.List.SelectedItem = Right.Items.Single(e => e.Path == other.Path); PreviewFollow(); await right.Session.Completion; await settle();
        right.PreviewWindowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await settle();
        check(right.Window != null && right.Window.Content == right && primary.Window == null && PreviewPane.Parent == Left.PipLayer, "The right card floats in its own window, the left one stays in its pane");
        right.Window.Close(); await settle();
        check(right.Window == null && right.Parent == Right.PipLayer && right.Mode == PreviewMode.Sidebar, "Closing that window returns the card to its pane");
        SetSinglePane(true); await settle();
        check(secondary == null && PreviewPane.Parent == Left.PreviewSlot && Right.PipLayer.Children.Count == 0, "Back to one pane: the second card is gone and the first docks again");
        Width = oldWidth; Height = oldHeight; await settle();
    }
}
#endif

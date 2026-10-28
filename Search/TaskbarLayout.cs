// Selected layout measurement adapted from EverythingToolbar, MIT,
// Copyright (c) 2023 Stephan Rumswinkel. Pinned source: vendor/EverythingSDK/NOTICES.txt.
using System.Windows;
using System.Windows.Automation;

namespace FileExplorer.Search;

internal static class TaskbarLayout
{
    internal static IReadOnlyList<Rect> Measure(IntPtr handle)
    {
        var bar = AutomationElement.FromHandle(handle);
        var blocked = new List<Rect>();
        var byTray = new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon");
        foreach (AutomationElement item in bar.FindAll(TreeScope.Descendants, byTray))
            if (item.Current.BoundingRectangle is { Width: > 0, Height: > 0 } rect) blocked.Add(rect);
        var frame = bar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "TaskbarFrame"));
        if (frame == null) return blocked;
        double maxChildWidth = bar.Current.BoundingRectangle.Width * 0.6;
        foreach (AutomationElement child in frame.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
        {
            var rect = child.Current.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            if (child.Current.AutomationId is "WidgetsButton" or "SystemTrayIcon" || rect.Width <= maxChildWidth)
                blocked.Add(rect);
        }
        return blocked;
    }

    // All inputs and the result are physical screen pixels. Never cover shell controls.
    internal static Rect? FindSlot(Rect bar, IReadOnlyList<Rect> blocked, double scale)
    {
        if (scale <= 0 || bar.Width <= bar.Height || bar.Height < 20 * scale || blocked.Count == 0) return null;
        var occupied = blocked.Where(r => r.IntersectsWith(bar)).OrderBy(r => r.Left).ToArray();
        if (occupied.Length == 0) return null;
        double padding = 8 * scale, cursor = bar.Left + padding;
        var gaps = new List<(double Start, double End)>();
        foreach (var rect in occupied)
        {
            if (rect.Left - padding > cursor) gaps.Add((cursor, Math.Min(rect.Left - padding, bar.Right - padding)));
            cursor = Math.Max(cursor, rect.Right + padding);
        }
        gaps.Add((cursor, bar.Right - padding));
        var gap = gaps.OrderByDescending(g => g.End - g.Start).First();
        double width = Math.Min(240 * scale, gap.End - gap.Start);
        if (width < 120 * scale) return null;
        double height = Math.Min(48 * scale, bar.Height * 0.75);
        return new Rect(gap.End - width, bar.Top + (bar.Height - height) / 2, width, height);
    }
}

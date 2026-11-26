using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FileExplorer.Preview;

namespace FileExplorer;

/// PREVIEW-41: the preview cards and where they are. One pane: one card, docked inside the pane under its tab row
/// (Sidebar), or picture in picture over its list, or in its own window. Two panes: each pane has its own card showing
/// its own selection, both at once, as picture in picture (a docked card would take half of an already halved pane);
/// either can still float in a window. Moving a card keeps its session, content, native windows, playback and leases.
public partial class MainWindow
{
    internal enum PreviewMode { Sidebar, PictureInPicture, Windowed }
    PreviewCard primary, secondary; // the left (or only) pane's, and the right pane's in two panes
    bool changingPreviewHost;
    IEnumerable<PreviewCard> Cards => primary == null ? Array.Empty<PreviewCard>() : secondary == null ? new[] { primary } : new[] { primary, secondary };
    PreviewCard ActiveCard => Cards.FirstOrDefault(card => card.Pane == active) ?? primary;

    // The left pane's card keeps the names the rest of the code and the verification use.
    PreviewCard PreviewPane => primary;
    DockPanel PreviewHeader => primary?.PreviewHeader;
    TextBlock PreviewTitle => primary?.PreviewTitle;
    Image PreviewAppIcon => primary?.PreviewAppIcon;
    ContentControl PreviewBody => primary?.PreviewBody;
    Button PreviewCloseButton => primary?.PreviewCloseButton;
    Button PreviewWindowButton => primary?.PreviewWindowButton;
    Button PreviewPipButton => primary?.PreviewPipButton;
    Button PreviewPinButton => primary?.PreviewPinButton;
    Button PreviewMarkButton => primary?.PreviewMarkButton;
    Button PreviewMarkNextButton => primary?.PreviewMarkNextButton;
    Button PreviewMarkPrevButton => primary?.PreviewMarkPrevButton;
    bool previewPinned { get => primary?.Pinned == true; set { if (primary != null) primary.Pinned = value; } }
    string previewShown { get => primary?.Shown; set { if (primary != null) primary.Shown = value; } }
    PreviewMode previewMode => primary?.Mode ?? PreviewMode.Sidebar;
    Window previewWindow => primary?.Window;

    /// One pane or two, a card added or retired: every card goes where its mode puts it now.
    void RefreshPreviewHosts()
    {
        if (primary == null) return;
        if (Settings.SinglePane && secondary is { } retired)
        {
            secondary = null;
            if (retired.Window is { } window) { retired.Window = null; window.Content = null; window.Close(); }
            PaneView.Detach(retired); retired.Session.Dispose();
            Right.UpdatePreviewSlot();
        }
        else if (!Settings.SinglePane && secondary == null) secondary = new PreviewCard(this, Right, UpdatePreviewHeading);
        foreach (var card in Cards) { if (card.Window == null) PlaceCard(card); UpdatePreviewModeButtons(card); }
        PreviewFollow();
    }

    /// Docked inside the pane in one pane; picture in picture over the pane's list otherwise.
    void PlaceCard(PreviewCard card) => card.Pane.HostPreview(card, card.Mode == PreviewMode.Sidebar && Settings.SinglePane);

    internal void TogglePin(PreviewCard card)
    {
        if (card.Session.Entry == null) return;
        card.Pinned = !card.Pinned; UpdatePreviewModeButtons(card);
        if (!card.Pinned) PreviewFollow();
    }

    void UpdatePreviewHeading(PreviewCard card, string text)
    {
        card.PreviewTitle.Text = text; card.PreviewHeader.DataContext = text.Length > 0 ? card.Session.Entry : null;
        var icon = PreviewControls.RendererIcon(card.Session.RendererId);
        if (icon != null) card.PreviewAppIcon.Source = icon;
        else card.PreviewAppIcon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Entry.SmallIcon)) { Source = card.PreviewHeader.DataContext });
        card.PreviewAppIcon.ToolTip = card.Session.RendererDescription;
        AutomationProperties.SetName(card.PreviewAppIcon, card.Session.RendererDescription ?? "Preview icon");
        if (card.Window != null) { card.Window.Title = text; card.Window.Icon = icon ?? card.Session.Entry?.SmallIcon; }
        UpdatePreviewModeButtons(card);
    }

    internal void ToggleCardWindow(PreviewCard card) => MoveHost(card, card.Mode == PreviewMode.Windowed ? PreviewMode.Sidebar : PreviewMode.Windowed);
    internal void ToggleCardPip(PreviewCard card) => MoveHost(card, card.Mode == PreviewMode.PictureInPicture ? PreviewMode.Sidebar : PreviewMode.PictureInPicture);
    internal void ClosePreviewFrom(PreviewCard card) { TogglePreview(); card.Pane.FocusList(); }

    void MovePreviewHost(PreviewMode destination) { if (primary != null) MoveHost(primary, destination); }
    void ResetPreviewHosts() { foreach (var card in Cards) MoveHost(card, PreviewMode.Sidebar); }

    void MoveHost(PreviewCard card, PreviewMode destination)
    {
        if (changingPreviewHost || card.Mode == destination) return;
        if (destination != PreviewMode.Sidebar && card.Session.Entry == null) return;
        changingPreviewHost = true;
        try
        {
            // Move the whole existing card. The session, content, HWND, native document,
            // playback and archive lease keep their identities.
            if (card.Window is { } previous) { card.Window = null; previous.Content = null; previous.Close(); }
            else PaneView.Detach(card);
            card.Mode = destination;
            card.Pane.UpdatePreviewSlot();
            if (destination == PreviewMode.Windowed)
            {
                card.Width = card.Height = double.NaN; card.Margin = new Thickness(0);
                card.HorizontalAlignment = HorizontalAlignment.Stretch; card.VerticalAlignment = VerticalAlignment.Stretch;
                var window = card.Window = new Window
                {
                    Owner = this, Content = card, Title = card.PreviewTitle.Text, Width = 840, Height = 680, MinWidth = 360, MinHeight = 260,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ShowActivated = false,
                    Background = Hex("#161B24"), Foreground = Foreground, FontFamily = FontFamily, FontSize = FontSize
                };
                window.Icon = PreviewControls.RendererIcon(card.Session.RendererId) ?? card.Session.Entry.SmallIcon;
                AuxiliaryWindows.Prepare(window, card, scaleContent: false);
#if DEBUG
                HiddenTrialPresentation.Prepare(window);
#endif
                window.SourceInitialized += (_, _) => ApplyAcrylic(window);
                window.Closed += (_, _) =>
                {
                    if (card.Window != window) return;
                    // Native window close restores; the header's explicit ×
                    // remains the separate close-preview action.
                    card.Window = null; window.Content = null;
                    if (!closingPreviews) MoveHost(card, PreviewMode.Sidebar);
                };
                ApplyPreviewAppearance(); window.Show();
            }
            else
            {
                PlaceCard(card);
                if (destination == PreviewMode.Sidebar) card.Shown = card.Session.Entry?.Path;
            }
            UpdatePreviewModeButtons(card); SetPreviewVisible(card, card.Session.Entry != null);
        }
        finally { changingPreviewHost = false; }
    }

    void SetPreviewVisible(PreviewCard card, bool visible)
    {
        bool appearing = visible && card.Visibility != Visibility.Visible;
        card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (appearing) Motion.Enter(card, ms: 120); // a fade only: hosted viewers draw in their own windows
        if (card.Window is { } window)
        {
            if (!visible) window.Hide();
            else if (!window.IsVisible) window.Show();
        }
        card.Pane.UpdatePreviewSlot();
    }

    void UpdatePreviewModeButtons() { foreach (var card in Cards) UpdatePreviewModeButtons(card); }
    void UpdatePreviewModeButtons(PreviewCard card)
    {
        string pin = card.Pinned ? "Unpin preview and follow selection" : "Pin this preview";
        card.PreviewPinButton.ToolTip = pin; AutomationProperties.SetName(card.PreviewPinButton, pin);
        AutomationProperties.SetItemStatus(card.PreviewPinButton, card.Pinned ? "Pinned" : "Following selection");
        card.PreviewPinButton.Tag = card.Pinned ? "on" : null; card.PreviewPinButton.IsEnabled = card.Session.Entry != null;
        UpdateMarkButtons(card); // OPUS-08
        string back = Settings.SinglePane ? "Return preview to the pane" : "Return preview to picture in picture";
        string floating = card.Mode == PreviewMode.Windowed ? back : "Open preview in a floating window";
        string pip = card.Mode == PreviewMode.PictureInPicture ? "Return preview to the pane" : "Picture in picture inside Pathweft";
        card.PreviewWindowButton.ToolTip = floating; AutomationProperties.SetName(card.PreviewWindowButton, floating);
        card.PreviewPipButton.ToolTip = pip; AutomationProperties.SetName(card.PreviewPipButton, pip);
        // two panes: picture in picture is the only in-app place, so there is nothing to switch to
        card.PreviewPipButton.Visibility = Settings.SinglePane ? Visibility.Visible : Visibility.Collapsed;
        card.PreviewWindowButton.Content = card.Mode == PreviewMode.Windowed ? "\uE73F" : "\uE8A7";
        // docked it is part of the pane's surface; over the list or in a window it needs a solid one
        if (card.Window == null && card.Mode == PreviewMode.Sidebar && Settings.SinglePane) card.SetResourceReference(Border.BackgroundProperty, "Palette.0DFFFFFF");
        else card.Background = Hex("#161B24");
    }
}

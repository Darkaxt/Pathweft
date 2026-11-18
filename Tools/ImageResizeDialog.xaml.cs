using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

// TOOLS-03 Resize images: Image Resizer's choices in Pathweft's look. The size (a preset or custom width, height and
// unit), the fit, quality per format, metadata, and where the results go: beside the originals or in a folder, named
// by a pattern, or replacing the originals (never the default, never remembered). Everything else is remembered.
public partial class ImageResizeDialog : Window
{
    internal enum Output { Beside, Folder, Replace }
    internal sealed record Choice(ResizeOptions Options, Output Output, string Folder);
    internal Choice Result { get; private set; }
    readonly string folder;
    bool loading, modal;
    const int Custom = -1;
#if DEBUG
    /// Verification answers the dialog instead of a person: it sets the controls and calls Accept.
    internal static Action<ImageResizeDialog> VerificationAnswer;
#endif

    /// `folder`: where the pictures are, for Browse and a relative output folder. `count`: how many will be resized.
    internal static Choice Ask(Window owner, string folder, int count)
    {
        var dialog = new ImageResizeDialog(owner, folder, count);
#if DEBUG
        if (VerificationAnswer is { } answer) { answer(dialog); dialog.Close(); return dialog.Result; }
        HiddenTrialPresentation.Prepare(dialog);
#endif
        dialog.modal = true;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    internal ImageResizeDialog(Window owner, string folder, int count)
    {
        InitializeComponent();
        AuxiliaryWindows.Prepare(this, Root);
        Owner = owner;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        this.folder = folder;
        Caption.Text = count == 1 ? "1 picture" : $"{count:N0} pictures";
        loading = true;
        for (int i = 0; i < ImageResizer.Presets.Count; i++)
        {
            var p = ImageResizer.Presets[i];
            PresetBox.Items.Add(new ComboBoxItem { Content = $"{p.Name} — {p.Width:0.##} × {p.Height:0.##} pixels", Tag = i });
        }
        PresetBox.Items.Add(new ComboBoxItem { Content = "Custom", Tag = Custom });
        Add(UnitBox, ("pixels", ResizeUnit.Pixel), ("percent", ResizeUnit.Percent), ("centimetres", ResizeUnit.Centimeter), ("inches", ResizeUnit.Inch));
        Add(FitBox, ("Fit — keep the proportions, inside the size", ResizeFit.Fit), ("Fill — keep the proportions, cut to the size", ResizeFit.Fill), ("Stretch — exactly the size", ResizeFit.Stretch));
        Add(PngBox, ("Encoder's choice", PngInterlaceOption.Default), ("On", PngInterlaceOption.On), ("Off", PngInterlaceOption.Off));
        Add(TiffBox, ("Encoder's choice", TiffCompressOption.Default), ("None", TiffCompressOption.None), ("CCITT Group 3", TiffCompressOption.Ccitt3),
            ("CCITT Group 4", TiffCompressOption.Ccitt4), ("LZW", TiffCompressOption.Lzw), ("RLE", TiffCompressOption.Rle), ("ZIP", TiffCompressOption.Zip));
        Add(DestinationBox, ("Beside the originals", Output.Beside), ("In a folder", Output.Folder), ("Replace the originals", Output.Replace));

        var saved = Parse(Settings.ImageResize);
        var o = saved.Options;
        Pick(PresetBox, saved.Preset);
        Pick(UnitBox, o.Size.Unit); Pick(FitBox, o.Size.Fit);
        WidthBox.Text = Number(saved.CustomWidth); HeightBox.Text = Number(saved.CustomHeight);
        ShrinkBox.IsChecked = o.ShrinkOnly; OrientationBox.IsChecked = o.IgnoreOrientation;
        QualitySlider.Value = o.JpegQuality; QualityText.Text = o.JpegQuality.ToString(CultureInfo.CurrentCulture);
        Pick(PngBox, o.PngInterlace); Pick(TiffBox, o.TiffCompress);
        MetadataBox.IsChecked = o.RemoveMetadata; DateBox.IsChecked = o.KeepDateModified;
        NameBox.Text = o.FileNameFormat;
        FolderBox.Text = saved.Folder;
        Pick(DestinationBox, saved.Output); // never Replace: that is chosen each time
        loading = false;
        Refresh();
        Loaded += (_, _) =>
        {
#if DEBUG
            if (!HiddenTrialPresentation.SuppressFocus(this))
#endif
            PresetBox.Focus();
        };
    }

    static void Add<T>(ComboBox box, params (string Text, T Value)[] items)
    {
        foreach (var (text, value) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        box.SelectedIndex = 0;
    }
    static bool Pick(ComboBox box, object tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, tag)) { box.SelectedItem = item; return true; }
        return false;
    }
    static T Tag<T>(ComboBox box, T fallback) => box.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;
    static string Number(double v) => v > 0 && double.IsFinite(v) ? v.ToString("0.##", CultureInfo.CurrentCulture) : "";

    /// Empty or 0: auto (follows the other side). Null: not a number.
    static double? Read(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return 0;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && v >= 0 && double.IsFinite(v) ? v : null;
    }

    internal Output Destination => Tag(DestinationBox, Output.Beside);
    int Preset => Tag(PresetBox, 0);

    void Setting_Changed(object s, SelectionChangedEventArgs e) { if (!loading) Refresh(); }
    void Setting_Click(object s, RoutedEventArgs e) => Refresh();
    void Text_Changed(object s, TextChangedEventArgs e) { if (!loading) Refresh(); }
    void Quality_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityText != null) QualityText.Text = ((int)QualitySlider.Value).ToString(CultureInfo.CurrentCulture);
    }

    void Refresh()
    {
        bool custom = Preset == Custom;
        CustomRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        // a percent Fit or Fill scales both sides by the width's percentage (the engine reads only the width then)
        HeightField.IsEnabled = !(Tag(UnitBox, ResizeUnit.Pixel) == ResizeUnit.Percent && Tag(FitBox, ResizeFit.Fit) != ResizeFit.Stretch);
        var output = Destination;
        FolderRow.Visibility = output == Output.Folder ? Visibility.Visible : Visibility.Collapsed;
        NameRow.Visibility = NameHint.Visibility = output == Output.Replace ? Visibility.Collapsed : Visibility.Visible;
        ReplaceHint.Visibility = output == Output.Replace ? Visibility.Visible : Visibility.Collapsed;
        ResizeButton.Content = output == Output.Replace ? "Resize and replace" : "Resize";
        var size = Size();
        string box = size == null ? "" : size.Unit == ResizeUnit.Percent && size.Fit != ResizeFit.Stretch ? $"{size.Width:0.##}%"
            : $"{(size.Width > 0 ? size.Width.ToString("0.##", CultureInfo.CurrentCulture) : "auto")} × {(size.Height > 0 ? size.Height.ToString("0.##", CultureInfo.CurrentCulture) : "auto")} {UnitName(size.Unit)}";
        Summary.Text = string.Join(" · ", new[]
        {
            size == null ? "" : $"{size.Fit} {box}",
            ShrinkBox.IsChecked == true ? "only smaller" : "",
            MetadataBox.IsChecked == true ? "metadata removed" : "",
            output == Output.Replace ? "originals to the Recycle Bin" : output == Output.Folder ? "to " + FolderBox.Text.Trim() : "beside the originals",
        }.Where(t => t.Length > 0));
    }
    static string UnitName(ResizeUnit u) => u switch { ResizeUnit.Pixel => "pixels", ResizeUnit.Percent => "%", ResizeUnit.Centimeter => "cm", _ => "in" };

    /// The chosen size, or null while the custom width or height isn't a number.
    ResizeSize Size()
    {
        var fit = Tag(FitBox, ResizeFit.Fit);
        if (Preset != Custom) return ImageResizer.Presets[Math.Clamp(Preset, 0, ImageResizer.Presets.Count - 1)] with { Fit = fit };
        if (Read(WidthBox.Text) is not { } w || Read(HeightBox.Text) is not { } h) return null;
        return new("Custom", fit, w, h, Tag(UnitBox, ResizeUnit.Pixel));
    }

    ResizeOptions Current(ResizeSize size) => ImageResizer.Defaults with
    {
        Size = size,
        ShrinkOnly = ShrinkBox.IsChecked == true,
        IgnoreOrientation = OrientationBox.IsChecked == true,
        RemoveMetadata = MetadataBox.IsChecked == true,
        JpegQuality = Math.Clamp((int)QualitySlider.Value, 1, 100),
        PngInterlace = Tag(PngBox, PngInterlaceOption.Default),
        TiffCompress = Tag(TiffBox, TiffCompressOption.Default),
        FileNameFormat = NameBox.Text.Trim() is { Length: > 0 } n ? n : ImageResizer.Defaults.FileNameFormat,
        KeepDateModified = DateBox.IsChecked == true,
    };

    void Browse_Click(object s, RoutedEventArgs e)
    {
        var pick = new Microsoft.Win32.OpenFolderDialog { Title = "Save the resized pictures in" };
        try { pick.InitialDirectory = Directory.Exists(Target()) ? Target() : folder; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { pick.InitialDirectory = folder; }
        if (pick.ShowDialog(this) == true) FolderBox.Text = pick.FolderName;
    }
    string Target()
    {
        string text = FolderBox.Text.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(folder, text));
    }

    void Resize_Click(object s, RoutedEventArgs e) => Accept();

    /// Resize: the dialog stays open, saying why, while the choices can't be used.
    internal void Accept()
    {
        void Fail(string why) { Problem.Text = why; Problem.Visibility = Visibility.Visible; }
        Problem.Visibility = Visibility.Collapsed;
        var size = Size();
        if (size == null) { Fail("Type the width and height as numbers. Leave one empty to keep the proportions."); return; }
        bool percentBoth = size.Unit == ResizeUnit.Percent && size.Fit != ResizeFit.Stretch;
        if (size.Width == 0 && (percentBoth || size.Height == 0)) { Fail(percentBoth ? "Type a percentage." : "Type a width, a height or both."); return; }
        if (size.Fit != ResizeFit.Fit && !percentBoth && (size.Width == 0 || size.Height == 0)) { Fail($"{size.Fit} needs both a width and a height."); return; }
        var output = Destination;
        string target = null;
        if (output == Output.Folder)
        {
            if (FolderBox.Text.Trim().Trim('"').Length == 0) { Fail("Choose the folder for the resized pictures."); return; }
            try { target = Target(); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { Fail("That isn't a valid folder path."); return; }
            if (File.Exists(target)) { Fail("That is a file, not a folder."); return; }
        }
        var options = Current(size);
        if (output != Output.Replace && options.FileNameFormat.IndexOfAny(new[] { '\\', '/' }) >= 0) { Fail("The file name can't contain \\ or /."); return; }
        Settings.ImageResize = Serialize(options, Preset, output == Output.Replace ? Output.Beside : output, output == Output.Folder ? FolderBox.Text.Trim() : Saved.Folder,
            Preset == Custom ? size.Width : Saved.CustomWidth, Preset == Custom ? size.Height : Saved.CustomHeight);
        Settings.Save();
        Result = new(options, output, target);
        if (modal) DialogResult = true;
    }

    (ResizeOptions Options, int Preset, Output Output, string Folder, double CustomWidth, double CustomHeight) Saved => Parse(Settings.ImageResize);

    // ---- remembered as "key:value" pairs joined by '|' (no '|' can be in a path or a file name) ----
    internal static string Serialize(ResizeOptions o, int preset, Output output, string folder, double customWidth, double customHeight)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join("|",
            "preset:" + preset, "fit:" + o.Size.Fit, "unit:" + o.Size.Unit, "w:" + customWidth.ToString(inv), "h:" + customHeight.ToString(inv),
            "shrink:" + (o.ShrinkOnly ? 1 : 0), "orient:" + (o.IgnoreOrientation ? 1 : 0), "strip:" + (o.RemoveMetadata ? 1 : 0),
            "quality:" + o.JpegQuality, "png:" + o.PngInterlace, "tiff:" + o.TiffCompress, "date:" + (o.KeepDateModified ? 1 : 0),
            "name:" + o.FileNameFormat, "out:" + (output == Output.Folder ? "folder" : "beside"), "folder:" + folder);
    }

    internal static (ResizeOptions Options, int Preset, Output Output, string Folder, double CustomWidth, double CustomHeight) Parse(string text)
    {
        var d = ImageResizer.Defaults;
        var map = (text ?? "").Split('|').Select(p => p.Split(':', 2)).Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
        string Get(string k) => map.TryGetValue(k, out var v) ? v : null;
        int Int(string k, int fallback) => int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        double Dbl(string k, double fallback) => double.TryParse(Get(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 && double.IsFinite(v) ? v : fallback;
        T En<T>(string k, T fallback) where T : struct, Enum => Enum.TryParse<T>(Get(k), out var v) && Enum.IsDefined(v) ? v : fallback;
        int preset = Int("preset", 0);
        if (preset != Custom && (preset < 0 || preset >= ImageResizer.Presets.Count)) preset = 0;
        double w = Dbl("w", 1024), h = Dbl("h", 768);
        var fit = En("fit", d.Size.Fit);
        var unit = En("unit", ResizeUnit.Pixel);
        var size = preset == Custom ? new ResizeSize("Custom", fit, w, h, unit) : ImageResizer.Presets[preset] with { Fit = fit };
        var o = d with
        {
            Size = size,
            ShrinkOnly = Int("shrink", d.ShrinkOnly ? 1 : 0) == 1,
            IgnoreOrientation = Int("orient", d.IgnoreOrientation ? 1 : 0) == 1,
            RemoveMetadata = Int("strip", d.RemoveMetadata ? 1 : 0) == 1,
            JpegQuality = Math.Clamp(Int("quality", d.JpegQuality), 1, 100),
            PngInterlace = En("png", d.PngInterlace),
            TiffCompress = En("tiff", d.TiffCompress),
            KeepDateModified = Int("date", d.KeepDateModified ? 1 : 0) == 1,
            FileNameFormat = Get("name") is { Length: > 0 } n ? n : d.FileNameFormat,
        };
        return (o, preset, Get("out") == "folder" ? Output.Folder : Output.Beside, Get("folder") ?? "", w, h);
    }
}

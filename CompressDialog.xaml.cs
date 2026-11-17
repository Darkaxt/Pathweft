using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

// ARCHIVE-12 Compress…: 7-Zip's Add to archive in Pathweft's look. The format, level, method, dictionary and word
// size, solid blocks, threads, volumes, password, encryption method, encrypted names and RAR's recovery record are
// chosen here; what a format can't take is greyed out. The choices are remembered without the password.
public partial class CompressDialog : Window
{
    internal sealed record Choice(string Path, ArchiveOptions Options);
    internal Choice Result { get; private set; }
    readonly IReadOnlyList<string> sources; // null inside an archive: nothing there to delete afterwards
    readonly string folder, stem;
    string extension, suggested;
    bool loading, modal;
#if DEBUG
    /// Verification answers the dialog instead of a person: it sets the controls and calls Accept.
    internal static Action<CompressDialog> VerificationAnswer;
#endif

    /// `stemPath`: the suggested archive without its extension. `sources`: the files on disk, for Delete after.
    internal static Choice Ask(Window owner, string stemPath, IReadOnlyList<string> sources)
    {
        var dialog = new CompressDialog(owner, stemPath, sources);
#if DEBUG
        if (VerificationAnswer is { } answer) { answer(dialog); dialog.Close(); return dialog.Result; }
#endif
        dialog.modal = true;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    internal CompressDialog(Window owner, string stemPath, IReadOnlyList<string> sources)
    {
        InitializeComponent();
        AuxiliaryWindows.Prepare(this, Root);
        Owner = owner;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        this.sources = sources;
        folder = Path.GetDirectoryName(stemPath); stem = stemPath;
        loading = true;
        var saved = ArchiveOptions.Parse(Settings.Compress);
        string rarReason = RarNative.AvailabilityReason();
        if (saved.Rar && rarReason.Length > 0) saved = (saved with { Format = "7z" }).Normalized();
        foreach (var format in ArchiveOptions.Formats)
        {
            var item = new ComboBoxItem { Content = format == "7z" ? "7z" : format.ToUpperInvariant(), Tag = format };
            if (format == "rar" && rarReason.Length > 0) { item.IsEnabled = false; item.ToolTip = rarReason; ToolTipService.SetShowOnDisabled(item, true); }
            FormatBox.Items.Add(item);
        }
        for (int i = 0; i < ArchiveOptions.LevelNames.Length; i++) LevelBox.Items.Add(new ComboBoxItem { Content = ArchiveOptions.LevelNames[i], Tag = i });
        ThreadsBox.Items.Add(new ComboBoxItem { Content = $"Automatic (all {Environment.ProcessorCount})", Tag = 0 });
        for (int i = 1; i <= Environment.ProcessorCount; i++) ThreadsBox.Items.Add(new ComboBoxItem { Content = i.ToString(), Tag = i });
        SplitBox.Items.Add(new ComboBoxItem { Content = "No split", Tag = 0L });
        string[] media = { "", "", " — CD", " — CD", " — FAT32", " — DVD", " — DVD DL" };
        for (int i = 0; i < ArchiveOptions.VolumePresets.Length; i++)
            SplitBox.Items.Add(new ComboBoxItem { Content = ArchiveOptions.Size(ArchiveOptions.VolumePresets[i]) + media[i], Tag = ArchiveOptions.VolumePresets[i] });
        SplitBox.Items.Add(new ComboBoxItem { Content = "Custom size", Tag = -1L });
        RecoveryBox.Items.Add(new ComboBoxItem { Content = "None", Tag = 0 });
        foreach (int percent in ArchiveOptions.RecoveryPercents) RecoveryBox.Items.Add(new ComboBoxItem { Content = percent + "%", Tag = percent });
        extension = saved.Extension;
        PathBox.Text = suggested = Archive.UniqueNew(stemPath + extension);
        Pick(FormatBox, saved.Format); Pick(LevelBox, saved.Level); Pick(ThreadsBox, saved.Threads);
        if (!Pick(SplitBox, saved.VolumeSize)) { Pick(SplitBox, -1L); VolumeBox.Text = saved.VolumeSize % (1 << 20) == 0 ? saved.VolumeSize / (1 << 20) + "M" : saved.VolumeSize % 1024 == 0 ? saved.VolumeSize / 1024 + "K" : saved.VolumeSize.ToString(); }
        Pick(RecoveryBox, saved.Recovery);
        NamesBox.IsChecked = saved.EncryptNames;
        DeleteBox.IsEnabled = sources != null;
        if (sources == null) { DeleteBox.ToolTip = "Inside an archive the selection stays where it is."; ToolTipService.SetShowOnDisabled(DeleteBox, true); }
        FillMethods(saved.Method);
        FillSizes(saved.Dictionary, saved.Word);
        FillSolid(saved.Solid);
        FillCiphers(saved.ZipCrypto);
        loading = false;
        Refresh();
        Loaded += (_, _) =>
        {
#if DEBUG
            if (!HiddenTrialPresentation.SuppressFocus(this))
#endif
            { PathBox.Focus(); PathBox.Select(0, PathBox.Text.Length - extension.Length); }
        };
        Closed += (_, _) => { PasswordBox.Clear(); RepeatBox.Clear(); PlainBox.Clear(); };
    }

    static bool Pick(ComboBox box, object tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, tag) && item.IsEnabled) { box.SelectedItem = item; return true; }
        return false;
    }
    static T Tag<T>(ComboBox box, T fallback) => box.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;
    string Format => Tag(FormatBox, "7z");
    string Method => Tag(MethodBox, "");

    void FillMethods(string keep)
    {
        MethodBox.Items.Clear();
        if (Format == "rar") MethodBox.Items.Add(new ComboBoxItem { Content = "RAR 5", Tag = "" });
        else if (Format == "tar") MethodBox.Items.Add(new ComboBoxItem { Content = "None (TAR only stores)", Tag = "" });
        foreach (var method in ArchiveOptions.Methods(Format)) MethodBox.Items.Add(new ComboBoxItem { Content = method, Tag = method });
        if (!Pick(MethodBox, keep)) MethodBox.SelectedIndex = 0;
    }
    void FillSizes(long dictionary, int word)
    {
        DictionaryBox.Items.Clear(); WordBox.Items.Clear();
        bool ppmd = Method == "PPMd";
        DictionaryLabel.Text = ppmd ? "Model memory" : "Dictionary size";
        WordLabel.Text = ppmd ? "Model order" : "Word size";
        string fixedWindow = Method switch { "Deflate" => "32 KB", "Deflate64" => "64 KB", _ => "Automatic" };
        DictionaryBox.Items.Add(new ComboBoxItem { Content = fixedWindow, Tag = 0L });
        foreach (long size in ArchiveOptions.Dictionaries(Format, Method)) DictionaryBox.Items.Add(new ComboBoxItem { Content = ArchiveOptions.Size(size), Tag = size });
        WordBox.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = 0 });
        foreach (int value in ArchiveOptions.Words(Format, Method)) WordBox.Items.Add(new ComboBoxItem { Content = value.ToString(), Tag = value });
        if (!Pick(DictionaryBox, dictionary)) DictionaryBox.SelectedIndex = 0;
        if (!Pick(WordBox, word)) WordBox.SelectedIndex = 0;
    }
    void FillSolid(string keep)
    {
        SolidBox.Items.Clear();
        if (Format == "7z")
        {
            SolidBox.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = "" });
            foreach (var block in ArchiveOptions.SolidBlocks) SolidBox.Items.Add(new ComboBoxItem { Content = ArchiveOptions.SolidName(block), Tag = block });
        }
        else if (Format == "rar")
        {
            SolidBox.Items.Add(new ComboBoxItem { Content = "Non-solid", Tag = "" });
            SolidBox.Items.Add(new ComboBoxItem { Content = "Solid", Tag = "on" });
        }
        else SolidBox.Items.Add(new ComboBoxItem { Content = "Non-solid", Tag = "" });
        if (!Pick(SolidBox, keep)) SolidBox.SelectedIndex = 0;
    }
    void FillCiphers(bool zipCrypto)
    {
        CipherBox.Items.Clear();
        CipherBox.Items.Add(new ComboBoxItem { Content = "AES-256", Tag = false });
        if (Format == "zip") CipherBox.Items.Add(new ComboBoxItem { Content = "ZipCrypto (older programs)", Tag = true });
        if (!Pick(CipherBox, zipCrypto)) CipherBox.SelectedIndex = 0;
    }

    void Format_Changed(object s, SelectionChangedEventArgs e)
    {
        if (loading) return;
        loading = true;
        FillMethods(Method); FillSizes(Tag(DictionaryBox, 0L), Tag(WordBox, 0)); FillSolid(Tag(SolidBox, "")); FillCiphers(Tag(CipherBox, false));
        loading = false;
        // the extension follows the format, as 7-Zip's does: name.7z becomes name.zip
        string text = PathBox.Text, next = "." + Format;
        if (text == suggested) PathBox.Text = suggested = Archive.UniqueNew(stem + next); // still the suggestion: a free name in the new format
        else if (text.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) PathBox.Text = text[..^extension.Length] + next;
        extension = next;
        Refresh();
    }
    void Method_Changed(object s, SelectionChangedEventArgs e)
    {
        if (loading) return;
        loading = true; FillSizes(Tag(DictionaryBox, 0L), Tag(WordBox, 0)); loading = false;
        Refresh();
    }
    void Setting_Changed(object s, SelectionChangedEventArgs e) { if (!loading) Refresh(); }
    void Setting_Click(object s, RoutedEventArgs e) => Refresh();
    void Volume_Changed(object s, TextChangedEventArgs e) { if (!loading) Refresh(); }
    void Password_Changed(object s, RoutedEventArgs e) { if (!loading) Refresh(); }
    void Show_Click(object s, RoutedEventArgs e)
    {
        bool show = ShowBox.IsChecked == true;
        loading = true;
        if (show) PlainBox.Text = PasswordBox.Password;
        else { PasswordBox.Password = PlainBox.Text; RepeatBox.Password = PlainBox.Text; PlainBox.Clear(); }
        loading = false;
        PlainBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        RepeatRow.Visibility = show ? Visibility.Collapsed : Visibility.Visible; // shown, it needs no second typing (7-Zip's Show Password)
        Refresh();
    }
    string Password => ShowBox.IsChecked == true ? PlainBox.Text : PasswordBox.Password;

    /// Greys out what the format and level don't use, and says in one line what will be written.
    void Refresh()
    {
        string format = Format;
        bool tar = format == "tar", rar = format == "rar", store = Tag(LevelBox, 3) == 0, compressing = !tar && !store;
        LevelBox.IsEnabled = !tar;
        MethodBox.IsEnabled = compressing && !rar;
        DictionaryBox.IsEnabled = compressing && DictionaryBox.Items.Count > 1;
        WordBox.IsEnabled = compressing && WordBox.Items.Count > 1;
        SolidBox.IsEnabled = compressing && SolidBox.Items.Count > 1;
        ThreadsBox.IsEnabled = !tar && !rar;
        if (rar) { ThreadsBox.ToolTip = "The embedded RAR writer chooses its own threads and dictionary."; ToolTipService.SetShowOnDisabled(ThreadsBox, true); }
        else ThreadsBox.ToolTip = null;
        VolumeField.Visibility = Tag(SplitBox, 0L) == -1 ? Visibility.Visible : Visibility.Collapsed;
        PasswordField.IsEnabled = RepeatField.IsEnabled = ShowBox.IsEnabled = !tar;
        if (tar) { PasswordField.ToolTip = "TAR has no encryption."; ToolTipService.SetShowOnDisabled(PasswordField, true); } else PasswordField.ToolTip = null;
        bool password = !tar && Password.Length > 0;
        CipherBox.IsEnabled = password && format == "zip";
        NamesBox.IsEnabled = password && format is "7z" or "rar";
        RecoveryBox.IsEnabled = rar;
        var options = Current();
        Summary.Text = string.Join(" · ", new[]
        {
            tar ? "TAR" : $"{(format == "7z" ? "7z" : format.ToUpperInvariant())} {ArchiveOptions.LevelNames[options.Level]}{(options.Level > 0 && options.Method.Length > 0 ? " " + options.Method : "")}",
            options.Encrypted ? (options.EncryptNames ? "encrypted, names hidden" : "encrypted") : "",
            options.VolumeSize > 0 ? "volumes of " + ArchiveOptions.Size(options.VolumeSize) : "",
        }.Where(t => t.Length > 0));
    }

    ArchiveOptions Current()
    {
        long split = Tag(SplitBox, 0L);
        return new ArchiveOptions
        {
            Format = Format,
            Level = Tag(LevelBox, 3),
            Method = Method,
            Dictionary = Tag(DictionaryBox, 0L),
            Word = Tag(WordBox, 0),
            Solid = Tag(SolidBox, ""),
            Threads = Tag(ThreadsBox, 2),
            VolumeSize = split == -1 ? ArchiveOptions.ParseSize(VolumeBox.Text) ?? 0 : split,
            Password = Format == "tar" || Password.Length == 0 ? null : Password,
            ZipCrypto = Tag(CipherBox, false),
            EncryptNames = NamesBox.IsChecked == true && NamesBox.IsEnabled,
            Recovery = Tag(RecoveryBox, 0),
            DeleteAfter = DeleteBox.IsChecked == true && sources != null,
        }.Normalized();
    }

    void Browse_Click(object s, RoutedEventArgs e)
    {
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Compress to", AddExtension = true, OverwritePrompt = false, DefaultExt = extension,
            Filter = $"{(Format == "7z" ? "7z" : Format.ToUpperInvariant())} archive (*{extension})|*{extension}",
        };
        try { save.InitialDirectory = Path.GetDirectoryName(Target(PathBox.Text)); save.FileName = Path.GetFileName(PathBox.Text.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { save.InitialDirectory = folder; }
        if (save.ShowDialog(this) == true) PathBox.Text = save.FileName;
    }
    string Target(string text)
    {
        text = text.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(folder, text));
    }

    void Compress_Click(object s, RoutedEventArgs e) => Accept();

    /// Compress: the dialog stays open, saying why, while something can't be written as asked.
    internal void Accept()
    {
        string Fail(string why) { Problem.Text = why; Problem.Visibility = Visibility.Visible; return null; }
        Problem.Visibility = Visibility.Collapsed;
        var options = Current();
        if (PathBox.Text.Trim().Trim('"').Length == 0) { Fail("Give the archive a name."); return; }
        string path;
        try { path = options.PathFor(Target(PathBox.Text)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { Fail("That isn't a valid file name or path."); return; }
        if (Path.GetFileNameWithoutExtension(path).Length == 0) { Fail("Give the archive a name."); return; }
        if (Archive.NameTaken(path)) { Fail($"{Path.GetFileName(path)} already exists there. Choose another name."); return; }
        if (Tag(SplitBox, 0L) == -1 && options.VolumeSize == 0) { Fail("Type a volume size of at least 64 KB, such as 700M or 4G."); return; }
        if (ShowBox.IsChecked != true && PasswordBox.Password != RepeatBox.Password) { Fail("The passwords don't match."); return; }
        if (options.DeleteAfter && sources.Any(source => Directory.Exists(source) && path.StartsWith(source.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
        { Fail("The archive can't go inside a folder that is deleted after compression."); return; }
        Settings.Compress = options.Serialize();
        Settings.Save();
        Result = new(path, options);
        if (modal) DialogResult = true;
    }
}

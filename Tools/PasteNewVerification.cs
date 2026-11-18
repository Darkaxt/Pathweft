#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer;

// TOOLS-06 / TOOLS-07 checks through the real commands, on fixtures under an owned root. The user's clipboard is saved
// exactly first (S4NativeClipboard) and put back at the end; nothing runs when it can't be saved.
public partial class MainWindow
{
    internal async Task VerifyPasteNew20(string root, Action<bool, string> check)
    {
        root = Path.GetFullPath(root);
        string scratch = Path.Combine(root, "pastenew20-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await VerifyPasteAsFile20(Path.Combine(scratch, "Paste"), check);
            await VerifyNewPlus20(Path.Combine(scratch, "New"), check);
            NewPlus.Checks(scratch, (ok, label) => check(ok, "TOOLS-07: " + label));
        }
        finally
        {
            SetActive(Left); Left.Navigate(root, record: false);
            await Until20(() => Left.Dir.Equals(root, StringComparison.OrdinalIgnoreCase));
            try { Directory.Delete(scratch, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static async Task<bool> Until20(Func<bool> condition, int milliseconds = 8000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { if (clock.ElapsedMilliseconds > milliseconds) return false; await Task.Delay(25); }
        return true;
    }

    static string ClipboardSignature(S4Verification.ClipboardPart[] parts)
        => string.Join("|", parts.Select(p => p.Format + "=" + p.Kind + ":" + p.Value).OrderBy(s => s, StringComparer.Ordinal));

    /// Puts exactly these formats on the clipboard (owned by this window); the sequence number after the write.
    uint PutClipboardForVerification(params (uint Format, byte[] Bytes)[] formats)
    {
        IntPtr owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        for (int attempt = 0; !OpenClipboard(owner); attempt++)
        {
            if (attempt == 50) throw new IOException("The clipboard stayed busy.");
            Thread.Sleep(20);
        }
        try
        {
            if (!EmptyClipboard()) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            foreach (var (format, bytes) in formats)
            {
                IntPtr memory = GlobalAlloc(0x42, (nuint)bytes.Length);
                if (memory == IntPtr.Zero) throw new OutOfMemoryException();
                IntPtr pointer = GlobalLock(memory);
                try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { GlobalUnlock(memory); }
                if (SetClipboardData(format, memory) == IntPtr.Zero) { GlobalFree(memory); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            }
        }
        finally { CloseClipboard(); }
        return GetClipboardSequenceNumber();
    }

    static byte[] Pixels20(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    static BitmapSource DecodeFile20(string path)
    {
        using var stream = File.OpenRead(path);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    async Task VerifyPasteAsFile20(string work, Action<bool, string> check)
    {
        Directory.CreateDirectory(work);
        S4Verification.ClipboardPart[] original;
        try { original = S4NativeClipboard.Read(); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            check(false, "TOOLS-06: the original clipboard can be saved exactly before Paste as file is checked (" + ex.Message + "); nothing was changed");
            return;
        }
        SetActive(Left); Left.Navigate(work, record: false);
        check(await Until20(() => Left.Dir.Equals(work, StringComparison.OrdinalIgnoreCase)), "TOOLS-06: the scratch folder is the active pane's folder");
        uint owned = 0;
        try
        {
            string P(string name) => Path.Combine(work, name);
            bool Selected(string name) => Left.List.SelectedItem is Entry e && e.Name == name;

            // text: CF_UNICODETEXT, UTF-8 file, repeat gets a new name, the clipboard is left exactly as it was
            const string text = "Pasted by Pathweft ü € 😀\r\nsecond line";
            owned = PutClipboardForVerification((CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")));
            string before = ClipboardSignature(S4NativeClipboard.Read());
            check(CanPasteAsFile() && CanPasteAsFile("txt") && !CanPasteAsFile("html") && !CanPasteAsFile("png"), "TOOLS-06: only Text file is offered for text on the clipboard");
            PasteAsFile("txt");
            check(File.Exists(P("Pasted text.txt")) && File.ReadAllBytes(P("Pasted text.txt")).SequenceEqual(new UTF8Encoding(false).GetBytes(text)), "TOOLS-06: clipboard text is saved as Pasted text.txt in UTF-8 without loss");
            check(await Until20(() => Selected("Pasted text.txt")), "TOOLS-06: the new file is selected");
            check(Left.List.SelectedItem is Entry { Renaming: false }, "TOOLS-06: Paste as file starts no rename");
            PasteAsFile("txt");
            check(File.Exists(P("Pasted text (2).txt")) && File.ReadAllText(P("Pasted text (2).txt")) == text, "TOOLS-06: pasting again makes Pasted text (2).txt");
            check(GetClipboardSequenceNumber() == owned && ClipboardSignature(S4NativeClipboard.Read()) == before, "TOOLS-06: Paste as file only reads the clipboard (sequence and every format unchanged)");

            // HTML: the whole CF_HTML document, then a fragment-only one wrapped in a UTF-8 page
            const string pre = "<html><body>\r\n<!--StartFragment-->", fragment = "<p>Zürich <b>bold</b> 😀</p>", post = "<!--EndFragment-->\r\n</body></html>";
            static byte[] CfHtml(string pre, string fragment, string post, bool document)
            {
                const string header = "Version:0.9\r\nStartHTML:{0}\r\nEndHTML:{1}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
                int length = string.Format(header, "0000000000", "0000000000", 0, 0).Length;
                int sh = length, sf = sh + Encoding.UTF8.GetByteCount(pre), ef = sf + Encoding.UTF8.GetByteCount(fragment), eh = ef + Encoding.UTF8.GetByteCount(post);
                string start = document ? sh.ToString("D10") : "-000000001", end = document ? eh.ToString("D10") : "-000000001";
                return Encoding.UTF8.GetBytes(string.Format(header, start, end, sf, ef) + pre + fragment + post + "\0");
            }
            uint html = RegisterClipboardFormat("HTML Format");
            owned = PutClipboardForVerification((html, CfHtml(pre, fragment, post, true)), (CF_UNICODETEXT, Encoding.Unicode.GetBytes("Zürich bold\0")));
            before = ClipboardSignature(S4NativeClipboard.Read());
            check(CanPasteAsFile("html") && CanPasteAsFile("txt"), "TOOLS-06: HTML Format on the clipboard enables HTML file");
            PasteAsFile("html");
            var document = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(pre + fragment + post)).ToArray();
            check(File.Exists(P("Pasted HTML.html")) && File.ReadAllBytes(P("Pasted HTML.html")).SequenceEqual(document), "TOOLS-06: CF_HTML StartHTML..EndHTML is saved as Pasted HTML.html, UTF-8 marked");
            owned = PutClipboardForVerification((html, CfHtml(pre, fragment, post, false)));
            PasteAsFile("html");
            var wrapped = File.Exists(P("Pasted HTML (2).html")) ? File.ReadAllText(P("Pasted HTML (2).html")) : "";
            check(wrapped.Contains(fragment) && wrapped.Contains("charset=\"utf-8\"") && !wrapped.Contains("StartFragment"), "TOOLS-06: a fragment-only CF_HTML (StartHTML -1) becomes a UTF-8 page around StartFragment..EndFragment");
            check(GetClipboardSequenceNumber() == owned, "TOOLS-06: HTML paste leaves the clipboard unchanged");

            // images: PNG as written, then CF_DIBV5 with alpha, then a 24-bit bottom-up CF_DIB
            var expected = new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 128, 10, 20, 30, 255, 40, 50, 60, 0, 70, 80, 90, 200 }; // BGRA, top row first
            var source = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, expected, 12);
            var png = EncodePng(source);
            owned = PutClipboardForVerification((RegisterClipboardFormat("PNG"), png));
            check(CanPasteAsFile("png") && !CanPasteAsFile("txt"), "TOOLS-06: a PNG on the clipboard enables Image file");
            PasteAsFile("png");
            check(File.Exists(P("Pasted image.png")) && File.ReadAllBytes(P("Pasted image.png")).SequenceEqual(png), "TOOLS-06: clipboard PNG is saved byte for byte as Pasted image.png");

            var v5 = new byte[124 + expected.Length];
            BitConverter.GetBytes(124).CopyTo(v5, 0); BitConverter.GetBytes(3).CopyTo(v5, 4); BitConverter.GetBytes(2).CopyTo(v5, 8);
            BitConverter.GetBytes((ushort)1).CopyTo(v5, 12); BitConverter.GetBytes((ushort)32).CopyTo(v5, 14); BitConverter.GetBytes(3).CopyTo(v5, 16);
            BitConverter.GetBytes(expected.Length).CopyTo(v5, 20);
            BitConverter.GetBytes(0x00FF0000u).CopyTo(v5, 40); BitConverter.GetBytes(0x0000FF00u).CopyTo(v5, 44); BitConverter.GetBytes(0x000000FFu).CopyTo(v5, 48); BitConverter.GetBytes(0xFF000000u).CopyTo(v5, 52);
            BitConverter.GetBytes(0x73524742u).CopyTo(v5, 56); // 'sRGB'
            Array.Copy(expected, 12, v5, 124, 12); Array.Copy(expected, 0, v5, 136, 12); // bottom-up
            owned = PutClipboardForVerification((CF_DIBV5, v5));
            PasteAsFile("png");
            check(File.Exists(P("Pasted image (2).png")) && Pixels20(DecodeFile20(P("Pasted image (2).png"))).SequenceEqual(expected), "TOOLS-06: CF_DIBV5 with alpha is saved as PNG with its alpha and row order");

            var dib = new byte[40 + 24];
            BitConverter.GetBytes(40).CopyTo(dib, 0); BitConverter.GetBytes(3).CopyTo(dib, 4); BitConverter.GetBytes(2).CopyTo(dib, 8);
            BitConverter.GetBytes((ushort)1).CopyTo(dib, 12); BitConverter.GetBytes((ushort)24).CopyTo(dib, 14);
            var opaque = (byte[])expected.Clone();
            for (int row = 0; row < 2; row++)
                for (int x = 0; x < 3; x++)
                {
                    int from = (row * 3 + x) * 4, to = 40 + (1 - row) * 12 + x * 3;
                    dib[to] = expected[from]; dib[to + 1] = expected[from + 1]; dib[to + 2] = expected[from + 2];
                    opaque[from + 3] = 255;
                }
            owned = PutClipboardForVerification((CF_DIB, dib));
            PasteAsFile("png");
            check(File.Exists(P("Pasted image (3).png")) && Pixels20(DecodeFile20(P("Pasted image (3).png"))).SequenceEqual(opaque), "TOOLS-06: a 24-bit CF_DIB is saved as an opaque PNG");
            check(GetClipboardSequenceNumber() == owned && IsClipboardFormatAvailable(CF_DIB), "TOOLS-06: image paste leaves the clipboard unchanged");

            // Undo takes the last one back to the Recycle Bin
            Undo(); await undoCompletion;
            check(!File.Exists(P("Pasted image (3).png")) && File.Exists(P("Pasted image (2).png")), "TOOLS-06: Undo removes the file Paste as file made, and only it");
        }
        finally
        {
            // text-only before rolling back: the rollback re-reads what is there, and a bitmap can't be held as bytes
            if (owned != 0)
            {
                owned = PutClipboardForVerification((CF_UNICODETEXT, Encoding.Unicode.GetBytes("Pathweft verification\0")));
                int restored;
                for (int attempt = 0; (restored = S4NativeClipboard.Restore(original, owned)) == 3 && attempt < 50; attempt++) await Task.Delay(20);
                if (restored == 2) check(true, "TOOLS-06: a newer clipboard owner was left alone at restore");
                else check(restored == 0 && ClipboardSignature(S4NativeClipboard.Read()) == ClipboardSignature(original), "TOOLS-06: the original clipboard is restored exactly afterwards");
            }
        }
    }

    async Task VerifyNewPlus20(string work, Action<bool, string> check)
    {
        string templates = Path.Combine(Path.GetDirectoryName(work), "Templates");
        Directory.CreateDirectory(work); Directory.CreateDirectory(templates);
        string year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string note = Path.Combine(templates, "01. Note $YYYY.txt"), project = Path.Combine(templates, "02. Project $YYYY");
        File.WriteAllText(note, "note body");
        Directory.CreateDirectory(Path.Combine(project, "docs"));
        File.WriteAllText(Path.Combine(project, "docs", "readme.md"), "readme");
        File.WriteAllText(Path.Combine(project, "$PARENT_FOLDER_NAME.txt"), "p");

        var saved = (Settings.NewPlusFolder, Settings.NewPlusHideExtension, Settings.NewPlusHideDigits, Settings.NewPlusVariables);
        Settings.NewPlusFolder = templates; Settings.NewPlusHideExtension = true; Settings.NewPlusHideDigits = true; Settings.NewPlusVariables = true;
        try
        {
            check(string.Join("|", Templates().Select(t => t.Title)) == $"Project {year}|Note {year}", "TOOLS-07: templates are discovered folders first, with digits, extensions and variables as set");
            SetActive(Left); Left.Navigate(work, record: false);
            check(await Until20(() => Left.Dir.Equals(work, StringComparison.OrdinalIgnoreCase)), "TOOLS-07: the scratch folder is the active pane's folder");
            var menu = TemplateMenuItems().ToList();
            check(menu.Count == 3 && menu.Take(2).All(m => m.IsEnabled) && (string)menu[1].Header == $"Note {year}", "TOOLS-07: New from template lists each template and Open templates folder");

            string P(string name) => Path.Combine(work, name);
            async Task<bool> RenamingThenCancel(string name)
            {
                if (!await Until20(() => Left.List.SelectedItem is Entry { Renaming: true } e && e.Name == name)) return false;
                var entry = (Entry)Left.List.SelectedItem;
                foreach (var box in Descendants20<TextBox>(Left.List).Where(b => b.Tag == entry))
                    box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box) ?? PresentationSource.FromVisual(this), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                return await Until20(() => !entry.Renaming);
            }
            void Answer(Func<NewFileDialog.FileType, bool> pick, Action<NewFileDialog> also = null)
                => NewFileDialog.VerificationAnswer = d => { if (d.Choose(pick)) { also?.Invoke(d); d.Accept(); } };

            bool listed = false;
            NewFileDialog.VerificationAnswer = d =>
            {
                listed = d.Entries.Count(e => e.Template != null) == 2 && d.Entries.Any(e => e.Template == null && e.Extension == ".txt");
                if (d.Choose(e => e.Template?.Path == note)) d.Accept();
            };
            NewFile(); await templateCreation;
            check(listed, "TOOLS-07: New file lists the templates beside Explorer's own types");
            check(File.Exists(P($"Note {year}.txt")) && File.ReadAllText(P($"Note {year}.txt")) == "note body", "TOOLS-07: a file template is copied with digits dropped and $YYYY resolved");
            check(await RenamingThenCancel($"Note {year}.txt"), "TOOLS-07: the created file is selected and renamed in place");

            Answer(e => e.Template?.Path == note);
            NewFile(); await templateCreation;
            check(File.Exists(P($"Note {year} (1).txt")), "TOOLS-07: a name conflict gets a numbered name");
            check(await RenamingThenCancel($"Note {year} (1).txt"), "TOOLS-07: the numbered copy is selected and renamed in place");

            bool folderList = false;
            NewFileDialog.VerificationAnswer = d =>
            {
                folderList = d.Entries.Count() == 2 && d.Entries.First().Template?.Path == project && d.Entries.Last().Extension == "";
                if (d.Choose(e => e.Template?.Path == project)) d.Accept();
            };
            MkDir(); await templateCreation;
            check(folderList, "TOOLS-07: New folder offers the folder templates and a plain folder");
            string made = P($"Project {year}");
            check(File.Exists(Path.Combine(made, "docs", "readme.md")) && File.ReadAllText(Path.Combine(made, "docs", "readme.md")) == "readme" && File.Exists(Path.Combine(made, $"Project {year}.txt")), "TOOLS-07: a folder template copies its structure off the UI thread and resolves nested variables");
            check(await RenamingThenCancel($"Project {year}"), "TOOLS-07: the created folder is selected and renamed in place");

            Answer(e => e.Template == null, d => d.NameField.Text = "Plain");
            MkDir();
            check(Directory.Exists(P("Plain")), "TOOLS-07: a plain folder is still made from the same window");

            Undo(); await undoCompletion;
            check(!Directory.Exists(P("Plain")), "TOOLS-07: Undo removes the plain folder");
            Undo(); await undoCompletion;
            check(!Directory.Exists(made), "TOOLS-07: Undo removes the created folder structure");
            Undo(); await undoCompletion;
            check(!File.Exists(P($"Note {year} (1).txt")) && File.Exists(P($"Note {year}.txt")), "TOOLS-07: Undo removes the created file, and only it");

            int before = Directory.GetFileSystemEntries(work).Length;
            await CreateFromTemplate(new NewTemplate(Path.Combine(templates, "gone.txt"), "gone", false));
            check(Status.Text.StartsWith("Couldn't create from gone.txt") && Directory.GetFileSystemEntries(work).Length == before, "TOOLS-07: a failed creation leaves nothing behind and says why");
            await CreateFromTemplate(new NewTemplate(Path.Combine(templates, "gone"), "gone", true));
            check(Status.Text.StartsWith("Couldn't create from gone") && Directory.GetFileSystemEntries(work).Length == before, "TOOLS-07: a failed folder template removes its partial folder");
        }
        finally
        {
            NewFileDialog.VerificationAnswer = null;
            (Settings.NewPlusFolder, Settings.NewPlusHideExtension, Settings.NewPlusHideDigits, Settings.NewPlusVariables) = saved;
        }
    }

    static IEnumerable<T> Descendants20<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants20<T>(child)) yield return deeper;
        }
    }
}
#endif

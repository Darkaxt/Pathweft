#if DEBUG
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;

public partial class MainWindow
{
    // TOOLS-04 (OCR in image previews) and TOOLS-05 (.reg structured preview) through the real pane, Quick View, PiP and Windowed hosts.
    internal async Task VerifyPreviewTools20(string root, Action<bool,string> check)
    {
        RegistryFile.Checks((ok,label)=>check(ok,"TOOLS-05: "+label));
        await TextExtractor.Checks(Path.Combine(root,"ocr-engine"),(ok,label)=>check(ok,"TOOLS-04: "+label));

        const string fixtureKey=@"Software\PathweftS20Fixture";
        string types=Path.Combine(root,"types.reg"),large=Path.Combine(root,"large.reg"),huge=Path.Combine(root,"huge.reg"),other=Path.Combine(root,"other.txt"),picture=Path.Combine(root,"words.png");
        string[] lines=
        {
            "Windows Registry Editor Version 5.00","",
            @"[HKEY_CURRENT_USER\Software\PathweftS20Fixture]",
            "@=\"default text\"",
            "\"Str\"=\"plain \\\"quoted\\\"\"",
            "\"Dw\"=dword:0000002a",
            "\"Qw\"=hex(b):02,00,00,00,00,00,00,00",
            @"""Bin""=hex:de,ad,\",
            "  be,ef",
            "\"Exp\"=hex(2):25,00,41,00,25,00,00,00",
            "\"Multi\"=hex(7):61,00,00,00,62,00,00,00,00,00",
            "\"None\"=hex(0):01",
            "\"Gone\"=-",
            "\"BadDw\"=dword:zz",
            @"[HKEY_CURRENT_USER\Software\PathweftS20Fixture\Child]",
            "\"Inner\"=\"child value\"",
            @"[-HKEY_CURRENT_USER\Software\PathweftS20Fixture\Removed]",
            @"[HKEY_CURRENT_USER\Software\PathweftS20Fixture\Broken",""
        };
        // regedit exports UTF-16 LE with a BOM
        File.WriteAllText(types,string.Join("\r\n",lines),Encoding.Unicode);
        var big=new StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Software\\PathweftS20Large]\r\n");
        for(int i=0;i<250_000;i++)big.Append("\"v").Append(i).Append("\"=dword:00000001\r\n");
        File.WriteAllText(large,big.ToString());
        using(var file=File.Create(huge)){file.Write("Windows Registry Editor Version 5.00\r\n"u8);file.SetLength(10L*1024*1024+1);}
        File.WriteAllText(other,"Another selected item");
        WriteWords(picture);
        var hashes=new[]{types,large}.ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));
        check(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(fixtureKey)==null,"TOOLS-05: The fixture's registry key does not exist before previewing");

        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();
        async Task Layout(){await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);if(previewWindow!=null)await previewWindow.Dispatcher.InvokeAsync(previewWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);}
        void Pick(string path){previewPinned=false;previewShown=null;Settings.Preview=true;Left.List.SelectedItem=Left.Items.Single(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));ApplyPreview();PreviewFollow();}
        async Task Select(string path,string module,string state="ready")
        {
            if(previewMode!=PreviewMode.Sidebar)MovePreviewHost(PreviewMode.Sidebar);
            Pick(path);await sidePreview.Completion;await Layout();
            check(sidePreview.State==state&&sidePreview.ModuleId==(state=="error"?sidePreview.ModuleId:module),$"Pane preview of {Path.GetFileName(path)} is {state} in {module} ({sidePreview.State}; {sidePreview.ModuleId})");
        }
        void ExpandAll(ItemsControl items){foreach(var row in items.Items.OfType<TreeViewItem>()){row.IsExpanded=true;ExpandAll(row);}}
        IEnumerable<TreeViewItem> Rows(ItemsControl items)=>items.Items.OfType<TreeViewItem>().SelectMany(row=>Rows(row).Prepend(row));
        var themes=new[]{("native-dark",true,"dark"),("native-light",true,"light"),("original",false,"dark")};
        async Task Themes(string label,Action state)
        {
            foreach(var theme in themes)foreach(double scale in new[]{1.0,1.5})
            {
                Settings.NativeTheme=theme.Item2;Settings.NativeMode=theme.Item3;Settings.TextScale=scale;ApplyAppearance();await Layout();
                state();await S1Verification.Snapshot(this,root,$"{label}-{theme.Item1}-{scale.ToString(CultureInfo.InvariantCulture)}");
            }
            Settings.NativeTheme=true;Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();await Layout();
        }

        // ---- TOOLS-05 ----
        await Select(types,"registry");
        var registry=(RegistryViewer)sidePreview.ViewerForVerification;
        var tree=registry.TreeForVerification;
        check(PreviewBody.Content is DockPanel&&tree!=null&&tree.IsVisible,"TOOLS-05: A selected .reg file opens as a key/value tree in the existing pane preview");
        ExpandAll(tree);
        var rows=Rows(tree).ToList();
        string[] names=rows.Select(row=>AutomationProperties.GetName(row)).ToArray();
        string Status(string name)=>rows.FirstOrDefault(row=>AutomationProperties.GetName(row)==name) is {} found?AutomationProperties.GetItemStatus(found):null;
        check(names.Take(3).SequenceEqual(new[]{"HKEY_CURRENT_USER","Software","PathweftS20Fixture"}),"TOOLS-05: Nested key paths show as expandable hive/key rows");
        foreach(string expected in new[]{"(Default), REG_SZ, default text","Str, REG_SZ, plain \"quoted\"","Dw, REG_DWORD, 0x0000002a (42)","Qw, REG_QWORD, 0x00000002 (2)",
            "Bin, REG_BINARY, de ad be ef","Exp, REG_EXPAND_SZ, %A%","Multi, REG_MULTI_SZ, a b","None, REG_NONE, 01","Inner, REG_SZ, child value"})
            check(names.Contains(expected),"TOOLS-05: Value row shows name, type and data: "+expected);
        check(Status("Gone, deleted")=="Deleted"&&Status("Removed")=="Deleted","TOOLS-05: Deleted values and keys are marked as deleted");
        check(Status("BadDw, ERROR, (Invalid DWORD (32-bit) value)")=="Invalid value"&&Status("Broken")=="Invalid key","TOOLS-05: Invalid values and keys are marked in the tree");
        var problems=registry.ProblemsForVerification;
        string[] problemText=problems?.Items.OfType<TextBlock>().Select(item=>item.Text).ToArray()??Array.Empty<string>();
        check(problemText.Any(text=>text.StartsWith("Line 14: ",StringComparison.Ordinal))&&problemText.Any(text=>text.StartsWith("Line 18: ",StringComparison.Ordinal)),"TOOLS-05: Parse problems are listed with their line numbers: "+string.Join(" | ",problemText));
        check(PreviewChildren<TextBlock>(PreviewBody).Any(text=>AutomationProperties.GetName(text)=="Registry file summary"&&text.Text.Contains("2 problems",StringComparison.Ordinal)),"TOOLS-05: The summary counts keys, values and problems");
        registry.ToggleForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
        string decoded=Encoding.Unicode.GetString(File.ReadAllBytes(types)[2..]);
        check(registry.ShowingSource&&registry.SourceForVerification is {IsReadOnly:true,IsVisible:true} box&&box.Text==decoded,"TOOLS-05: The source toggle shows the file's exact text read-only");
        registry.ToggleForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
        check(!registry.ShowingSource&&tree.IsVisible,"TOOLS-05: The toggle returns to the same key tree");
        if(problems!=null)problems.SelectedIndex=problemText.ToList().FindIndex(text=>text.StartsWith("Line 18: ",StringComparison.Ordinal));await Layout();
        check(registry.ShowingSource&&registry.SourceForVerification.SelectedText==lines[17],"TOOLS-05: Choosing a problem opens the source text at its line ("+registry.SourceForVerification?.SelectedText+")");
        registry.ToggleForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
        await Themes("tools05-tree",()=>check(tree.IsVisible&&sidePreview.ViewerForVerification==registry,"TOOLS-05: The tree keeps rendering through theme and scale changes"));

        // host transitions keep the same viewer and content
        {
            var content=PreviewBody.Content;long generation=sidePreview.Generation;var entry=sidePreview.Entry;
            PreviewPipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            check(previewMode==PreviewMode.PictureInPicture&&ReferenceEquals(registry,sidePreview.ViewerForVerification)&&ReferenceEquals(content,PreviewBody.Content)&&tree.IsVisible,"TOOLS-05: PiP keeps the same registry tree");
            await S1Verification.Snapshot(this,root,"tools05-pip");
            PreviewWindowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            check(previewMode==PreviewMode.Windowed&&previewWindow?.Content==PreviewPane&&ReferenceEquals(registry,sidePreview.ViewerForVerification)&&sidePreview.Generation==generation&&tree.IsVisible,"TOOLS-05: Windowed keeps the same registry tree and request");
            await S1Verification.Snapshot(previewWindow,root,"tools05-windowed");
            previewWindow.Close();await Layout();
            check(previewMode==PreviewMode.Sidebar&&sidePreview.Entry==entry&&ReferenceEquals(registry,sidePreview.ViewerForVerification),"TOOLS-05: Closing the floating window returns the same tree to the sidebar");
        }

        // selection replacement, including while a large file is still parsing
        await Select(other,"text");
        check(registry.TreeForVerification==null&&!PreviewChildren<TreeView>(PreviewBody).Any(),"TOOLS-05: Selecting another file replaces and releases the registry tree");
        Pick(large);var abandoned=sidePreview.Completion;
        Pick(other);await abandoned;await sidePreview.Completion;await Layout();
        check(sidePreview.ModuleId=="text"&&sidePreview.State=="ready"&&!PreviewChildren<TreeView>(PreviewBody).Any(),"TOOLS-05: Changing selection during a large parse cancels it without publishing a stale tree");
        await Select(types,"registry");
        check(!ReferenceEquals(registry,sidePreview.ViewerForVerification)&&((RegistryViewer)sidePreview.ViewerForVerification).TreeForVerification.Items.Count==1,"TOOLS-05: Returning to the .reg file builds a fresh tree");
        var started=System.Diagnostics.Stopwatch.StartNew();
        await Select(large,"registry");
        var largeViewer=(RegistryViewer)sidePreview.ViewerForVerification;
        check(largeViewer.ParseForVerification is {Truncated:true}&&largeViewer.ProblemsForVerification?.Items.OfType<TextBlock>().Any(text=>text.Text.StartsWith("Line 200",StringComparison.Ordinal)&&text.Text.Contains("Only the first",StringComparison.Ordinal))==true,
            $"TOOLS-05: A large .reg file is truncated visibly instead of hanging ({started.ElapsedMilliseconds} ms)");
        await Select(huge,"registry","error");
        check(PreviewBody.Content is TextBlock {Text:var hugeText}&&hugeText.Contains("too large",StringComparison.Ordinal),"TOOLS-05: A .reg file over 10 MB reports the size limit");
        await Select(types,"registry");
        ToggleQuickView(Left,this);await quickPreview.Completion;
        check(quickPreview.State=="ready"&&quickPreview.ModuleId=="registry"&&quickPreview.ViewerForVerification is RegistryViewer{TreeForVerification:not null},"TOOLS-05: Quick View uses the same registry tree");
        quick.Close();await PreviewSession.DrainAsync();
        foreach(var (path,hash) in hashes)check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"TOOLS-05: Previewing leaves the .reg file unchanged: "+Path.GetFileName(path));
        check(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(fixtureKey)==null&&Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\PathweftS20Large")==null,"TOOLS-05: Previewing never writes the registry");

        // ---- TOOLS-04 ----
        var mapped=TextExtractor.RegionToPixels(new Rect(10,20,100,50),1.5,1000,1000);
        check(mapped==new Int32Rect(15,30,150,75),"TOOLS-04: A DIP region maps to image pixels at 150%");
        check(TextExtractor.RegionToPixels(new Rect(10,20,100,50),2,1000,1000)==new Int32Rect(20,40,200,100)&&TextExtractor.RegionToPixels(new Rect(900,900,300,300),1,1000,1000)==new Int32Rect(900,900,100,100),"TOOLS-04: Region mapping scales at 200% and clamps to the image");
        string copied=null;OcrOverlay.ClipboardForVerification=value=>copied=value;
        try
        {
            await Select(picture,"image");
            var overlay=PreviewChildren<OcrOverlay>(PreviewBody).Single();
            var languages=TextExtractor.Languages();
            check(overlay.ToggleForVerification.IsVisible&&AutomationProperties.GetName(overlay.ToggleForVerification).Length>0,"TOOLS-04: The image preview shows a small named OCR button");
            check(overlay.LanguageForVerification.Visibility==(languages.Count>1?Visibility.Visible:Visibility.Collapsed),$"TOOLS-04: A language choice appears only with more than one OCR language ({languages.Count})");
            overlay.ToggleForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            string tag=overlay.LanguageForVerification.SelectedItem is ComboBoxItem{Tag:string chosen}?chosen:"";
            string missing=TextExtractor.Unavailable(tag);
            var frame=overlay.FrameForVerification;
            if(missing!=null)
            {
                check(overlay.StatusText.Contains(missing,StringComparison.Ordinal),"TOOLS-04: This host has no usable OCR language and the preview says so explicitly: "+missing);
                await overlay.Recognize(null);
                check(overlay.ResultText.Length==0&&!overlay.CopyForVerification.IsEnabled&&copied==null,"TOOLS-04: Without OCR nothing is recognized, copied or installed");
            }
            else
            {
                double scale=overlay.Scale;
                check(Math.Abs(scale-overlay.BitmapForVerification.PixelWidth/frame.ActualWidth)<1e-9,"TOOLS-04: The overlay maps by the displayed image scale ("+scale.ToString("0.###",CultureInfo.InvariantCulture)+")");
                await overlay.Recognize(new Rect(frame.ActualWidth/2,0,frame.ActualWidth/2,frame.ActualHeight));
                check(overlay.ResultText.Contains("Lantern",StringComparison.OrdinalIgnoreCase)&&!overlay.ResultText.Contains("Harbor",StringComparison.OrdinalIgnoreCase),"TOOLS-04: OCR of the right half returns its rendered word: "+overlay.ResultText);
                check(copied==null,"TOOLS-04: Recognition never writes the clipboard by itself");
                overlay.CopyForVerification.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(copied==overlay.ResultText,"TOOLS-04: Copy text copies exactly the recognized text");
                await overlay.Recognize(null);
                check(overlay.ResultText.Contains("Harbor",StringComparison.OrdinalIgnoreCase)&&overlay.ResultText.Contains("Lantern",StringComparison.OrdinalIgnoreCase),"TOOLS-04: OCR of the whole image returns both words: "+overlay.ResultText);
                await Themes("tools04-result",()=>check(overlay.ResultText.Length>0,"TOOLS-04: The OCR result panel renders through theme and scale changes"));
                PreviewPipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
                double small=overlay.Scale;
                check(previewMode==PreviewMode.PictureInPicture&&Math.Abs(small-overlay.BitmapForVerification.PixelWidth/frame.ActualWidth)<1e-9,$"TOOLS-04: The PiP view remaps by its own displayed scale ({scale.ToString("0.###",CultureInfo.InvariantCulture)} -> {small.ToString("0.###",CultureInfo.InvariantCulture)})");
                await overlay.Recognize(new Rect(0,0,frame.ActualWidth/2,frame.ActualHeight));
                check(overlay.ResultText.Contains("Harbor",StringComparison.OrdinalIgnoreCase)&&!overlay.ResultText.Contains("Lantern",StringComparison.OrdinalIgnoreCase),"TOOLS-04: Region OCR stays correct at the PiP scale: "+overlay.ResultText);
                await S1Verification.Snapshot(this,root,"tools04-pip");
                PreviewWindowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
                check(previewMode==PreviewMode.Windowed&&PreviewChildren<OcrOverlay>(PreviewBody).Single()==overlay,"TOOLS-04: Windowed keeps the same OCR overlay");
                previewWindow.Close();await Layout();
                // a recognition still running when the selection changes is abandoned quietly
                var pending=overlay.Recognize(null);
                await Select(other,"text");await pending;
                check(!PreviewChildren<OcrOverlay>(PreviewBody).Any(),"TOOLS-04: Changing selection during recognition replaces the image preview");
            }
            if(previewMode!=PreviewMode.Sidebar)MovePreviewHost(PreviewMode.Sidebar);
            await Select(picture,"image");
            ToggleQuickView(Left,this);await quickPreview.Completion;
            check(quickPreview.ModuleId=="image"&&PreviewChildren<OcrOverlay>(quickBody).Count()==1,"TOOLS-04: Quick View image previews offer the same OCR action");
            quick.Close();await PreviewSession.DrainAsync();
        }
        finally{OcrOverlay.ClipboardForVerification=null;}
        check(HiddenTrialPresentation.ForegroundViolations==0,"TOOLS-04/05: Preview tool checks never activate a desktop window");
    }

    // Two words far apart so a half-image region contains exactly one
    static void WriteWords(string path)
    {
        var visual=new DrawingVisual();
        using(var context=visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,900,200));
            var typeface=new Typeface("Segoe UI");
            context.DrawText(new FormattedText("Harbor",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,64,Brushes.Black,1),new Point(40,60));
            context.DrawText(new FormattedText("Lantern",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,64,Brushes.Black,1),new Point(520,60));
        }
        var bitmap=new RenderTargetBitmap(900,200,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);png.Save(file);
    }
}
#endif

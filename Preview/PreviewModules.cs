// Selected extension/priority handling adapted from QuickLook viewer Plugin.cs files.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;

namespace FileExplorer.Preview;

internal sealed record PreviewModule(string Id, string Name, string Description, int Priority, string[] Extensions)
{
    internal bool Enabled => !Settings.DisabledPreviewModules.Contains(Id);
    internal bool Matches(Entry entry) => Id=="clsid"&&ShellClassInspection.IsInput(entry.Path)||Id=="binary"&&!entry.IsDir&&!ShellClassInspection.IsInput(entry.Path)||Id == "archive" && (entry.IsDir || Archive.IsArchiveName(entry.Path)) ||
        !entry.IsDir && (Id == "text" || (Id == "f3d" ? F3dViewer.Supports(entry.Path) : Extensions.Contains(Path.GetExtension(entry.Path), StringComparer.OrdinalIgnoreCase)));
}

internal static class PreviewModules
{
    // Higher priority first, stable ID order on ties. Markdown outranks generic text.
    // Fixed inventory: no reflection, downloaded plugins or donor product services.
    internal static readonly PreviewModule[] All =
    {
        new("csv", "Delimited tables", "Read-only CSV, TSV and PSV tables with headers and row navigation.", 95, new[] { ".csv", ".tsv", ".psv" }),
        new("database", "Databases", "Read-only SQLite and LiteDB tables, collections and row navigation.", 95, new[]{".sqlite",".sqlite3",".db",".db3",".sdb",".litedb",".lite"}),
        new("mail", "Mail messages", "EML and MSG headers, isolated message body and attachment names.",95,new[]{".eml",".msg"}),
        new("certificate", "Certificates", "Public certificate, signature and provisioning details; no installation or trust changes.",95,new[]{".cer",".crt",".pem",".p7s",".pkcs7",".p12",".pfx",".mobileprovision",".certSigningRequest"}),
        new("app", "Application packages", "Android, Windows, iOS and NuGet package identity and metadata without installation.",95,new[]{".apk",".aab",".appx",".appxbundle",".msix",".msixbundle",".msi",".msp",".ipa",".nupkg",".snupkg"}),
        new("design", "Design thumbnails", "Embedded previews from supported design documents; no editor launch.",95,new[]{".cdr",".fig",".kra",".pdn",".pip",".pix",".sketch",".xd",".xmind"}),
        new("photocraft", "Layered images", "Read-only native PhotoCraft and Photoshop layer composition; no editor launch.",95,new[]{".pcraft",".psd",".psb"}),
        new("vectorcraft", "Vector artwork", "Native VectorCraft artboards, SVG and PDF-compatible AI; no editor launch or external linked-file loading.",95,new[]{".vectorcraft",".drawcraft",".svg",".svgz",".ai"}),
        new("figcraft", "Interface designs", "Native FigCraft pages/frames and best-effort Figma import; existing embedded design previews keep priority.",90,new[]{".figcraft",".fig"}),
        new("pdfcraft", "PDF (Craft)", "Optional source-built PDF rendering; Windows PDF remains the default. Located native text is available through the core API without OCR.",80,new[]{".pdf"}),
        new("mediainfo", "Media information", "Technical container, codec, track and color information. Choose this view from the preview menu.",80,new[]{".mp4",".m4v",".mov",".wmv",".avi",".mkv",".webm",".mpg",".mpeg",".3gp",".ts",".mp3",".wav",".wma",".m4a",".aac",".flac",".ogg",".opus"}),
        new("binary", "Hex and text", "Read-only hexadecimal and ASCII bytes with offset navigation.",-20,new[]{".bin",".hex"}),
        new("elf", "ELF, Mach-O and UImage", "Executable architecture and binary metadata without loading or executing code.",95,new[]{".elf",".so",".o",".out",".axf",".prx",".puff",".ko",".mod",".dylib",".uimage"}),
        new("dump", "Crash dumps", "Windows minidump process, exception, thread and module information; no symbol downloads.",95,new[]{".dmp",".dump",".mdmp",".hdmp",".minidump"}),
        new("prefetch", "Prefetch records", "Windows Prefetch executable, run count, times and referenced files.",95,new[]{".pf"}),
        new("chm", "Compiled help", "Isolated static CHM topics, internal navigation and local images.",95,new[]{".chm"}),
        new("clsid", "Shell class details", "Read-only ::{GUID} registration inspection without activating the inspected class.",110,Array.Empty<string>()),
        new("office", "Office documents", "Read-only document pages, workbook sheets and presentation slides, with embedded navigation.",95,new[]{".doc",".docx",".docm",".odt",".xls",".xlsx",".xlsm",".xlsb",".ods",".ppt",".pptx",".odp",".vsd",".vsdx"}),
        new("f3d", "3D models (F3D)", "Interactive model preview: drag to rotate, right-drag to pan, wheel to zoom. Uses the bundled F3D readers.", 80, Array.Empty<string>()),
        new("markdown", "Markdown", "Read-only formatted Markdown; scripts and remote resources stay inactive.", 100,
            new[] { ".md", ".markdown", ".mdx", ".mmd", ".mkd", ".mdwn", ".mdown", ".mdc", ".qmd", ".rmd", ".rmarkdown", ".apib", ".mdtxt", ".mdtext" }),
        new("image", "Images", "WIC images, decoded to fit the preview without retaining the source file.", 90,
            new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".heic" }),
        new("pdf", "PDF", "Windows PDF rendering with page navigation; no document scripts.", 90, new[] { ".pdf" }),
        new("html", "HTML", "Static HTML with local images and styling. Scripts, online content and downloads stay disabled.", 90, new[] { ".html", ".htm" }),
        new("executable", "Executable details", "Version resources, architecture and file details without running the application.", 90, new[] { ".exe" }),
        new("archive", "Folders and archives", "Expandable folder and archive trees without extracting or running entries.", 90, Array.Empty<string>()),
        new("media", "Video and audio", "Bundled MPV playback with hardware acceleration and Dolby Vision to SDR conversion.", 90,
            new[] { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".3gp", ".ts", ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".opus" }),
        new("font", "Fonts", "TrueType and OpenType specimens; the font is not installed.", 90, new[] { ".ttf", ".otf" }),
        new("text", "Text and code", "Read-only source text with encoding detection and a visible 256-KiB limit.", -5, Array.Empty<string>())
    };
    internal static IEnumerable<PreviewModule> Candidates(Entry entry) => All.Where(m => m.Enabled && (ShellClassInspection.IsInput(entry.Path)?m.Id=="clsid":m.Matches(entry)))
        .OrderByDescending(m => m.Id=="binary"&&m.Extensions.Contains(Path.GetExtension(entry.Path),StringComparer.OrdinalIgnoreCase)?95:m.Priority).ThenBy(m => m.Id, StringComparer.Ordinal);
}

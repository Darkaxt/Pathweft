// Selected extension/priority handling adapted from QuickLook viewer Plugin.cs files.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;

namespace FileExplorer.Preview;

internal sealed record PreviewModule(string Id, string Name, string Description, int Priority, string[] Extensions)
{
    internal bool Enabled => !Settings.DisabledPreviewModules.Contains(Id);
    internal bool Matches(Entry entry) => Id == "archive" && entry.IsDir ||
        !entry.IsDir && (Id == "text" || Extensions.Contains(Path.GetExtension(entry.Path), StringComparer.OrdinalIgnoreCase));
}

internal static class PreviewModules
{
    // Higher priority first, stable ID order on ties. Markdown outranks generic text.
    // Fixed inventory: no reflection, downloaded plugins or donor product services.
    internal static readonly PreviewModule[] All =
    {
        new("f3d", "3D models (F3D prototype)", "Opt-in GLB preview with orbit, zoom and reset; embedded model resources only.", 100, new[] { ".glb" }),
        new("markdown", "Markdown", "Read-only formatted Markdown; scripts and remote resources stay inactive.", 100,
            new[] { ".md", ".markdown", ".mdx", ".mmd", ".mkd", ".mdwn", ".mdown", ".mdc", ".qmd", ".rmd", ".rmarkdown", ".apib", ".mdtxt", ".mdtext" }),
        new("image", "Images", "WIC images, decoded to fit the preview without retaining the source file.", 90,
            new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".heic" }),
        new("pdf", "PDF", "Windows PDF rendering with page navigation; no document scripts.", 90, new[] { ".pdf" }),
        new("archive", "Archive and folder listing", "Lists ZIP-family archives and folders without extracting or running entries.", 90,
            new[] { ".zip", ".jar", ".nupkg", ".apk", ".vsix", ".whl" }),
        new("media", "Video and audio", "Built-in playback using installed Windows codecs.", 90,
            new[] { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".3gp", ".ts", ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".opus" }),
        new("font", "Fonts", "TrueType and OpenType specimens; the font is not installed.", 90, new[] { ".ttf", ".otf" }),
        new("text", "Text and code", "Read-only source text with encoding detection and a visible 256-KiB limit.", -5, Array.Empty<string>())
    };
    internal static IEnumerable<PreviewModule> Candidates(Entry entry) => All.Where(m => m.Enabled && m.Matches(entry))
        .OrderByDescending(m => m.Priority).ThenBy(m => m.Id, StringComparer.Ordinal);
}

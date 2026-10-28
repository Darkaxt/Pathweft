// Markdown extension selection ported from QuickLook.Plugin.MarkdownViewer.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
// Native WPF rendering replaces the donor's script-driven browser/resource host.
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace FileExplorer.Preview;

internal static class MarkdownContent
{
    internal static FlowDocumentScrollViewer Render(string text)
    {
        var parsed = Markdown.Parse(text, new MarkdownPipelineBuilder().DisableHtml().Build());
        var document = new FlowDocument { PagePadding = new(18), FontFamily = new("Segoe UI"), FontSize = 14,
            Background = Brushes.Transparent, Foreground = MainWindow.Hex("#E8EEF6") };
        foreach (var block in parsed) document.Blocks.Add(Block(block));
        return new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent };
    }
    static WpfBlock Block(MdBlock block)
    {
        if (block is CodeBlock code)
            return new Paragraph(new Run(code.Lines.ToString())) { FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12, Background = MainWindow.Hex("#252830"), Padding = new(12), Margin = new(0, 8, 0, 8) };
        if (block is ListBlock list)
        {
            var result = new System.Windows.Documents.List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new(0, 4, 0, 8), Padding = new(18, 0, 0, 0) };
            foreach (ListItemBlock item in list)
            {
                var target = new ListItem(); foreach (var child in item) target.Blocks.Add(Block(child)); result.ListItems.Add(target);
            }
            return result;
        }
        if (block is QuoteBlock quote)
        {
            var section = new Section { BorderBrush = MainWindow.Hex("#4C8DFF"), BorderThickness = new(3, 0, 0, 0), Padding = new(12, 2, 0, 2), Margin = new(0, 8, 0, 8) };
            foreach (var child in quote) section.Blocks.Add(Block(child)); return section;
        }
        if (block is ThematicBreakBlock) return new Paragraph(new Run("────────")) { Foreground = MainWindow.Hex("#8A97AA") };
        var paragraph = new Paragraph { Margin = new(0, 0, 0, 10) };
        if (block is HeadingBlock heading) { paragraph.FontSize = Math.Max(15, 30 - heading.Level * 3); paragraph.FontWeight = FontWeights.SemiBold; }
        if (block is LeafBlock leaf && leaf.Inline != null)
            foreach (var inline in leaf.Inline) Add(paragraph.Inlines, inline);
        return paragraph;
    }
    static void Add(InlineCollection target, MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
            case CodeInline code: target.Add(new Run(code.Content) { FontFamily = new("Cascadia Mono, Consolas"), Background = MainWindow.Hex("#252830") }); break;
            case LineBreakInline: target.Add(new LineBreak()); break;
            case EmphasisInline emphasis:
                Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                foreach (var child in emphasis) Add(span.Inlines, child); target.Add(span); break;
            case LinkInline link:
                // Text only: no URI navigation, script execution or image/network fetch.
                var label = new Span { Foreground = MainWindow.Hex("#8DB6FF") };
                foreach (var child in link) Add(label.Inlines, child);
                if (link.IsImage) label.Inlines.Add(new Run(" [image]"));
                label.ToolTip = link.Url; target.Add(label); break;
            case AutolinkInline link: target.Add(new Run(link.Url)); break;
            case ContainerInline container: foreach (var child in container) Add(target, child); break;
            case HtmlInline html: target.Add(new Run(html.Tag)); break;
        }
    }
}

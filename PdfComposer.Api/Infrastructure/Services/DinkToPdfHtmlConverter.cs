using DinkToPdf;
using DinkToPdf.Contracts;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using PdfComposer.Api.Infrastructure.Utilities;

namespace PdfComposer.Api.Infrastructure.Services;

public sealed class DinkToPdfHtmlConverter : IHtmlToPdfConverter
{
    private readonly Lazy<IConverter> _converter = new(() => new SynchronizedConverter(new PdfTools()));

    public async Task<Stream> ConvertAsync(string html, PdfRenderOptions options, CancellationToken cancellationToken = default)
    {
        var document = new HtmlToPdfDocument
        {
            GlobalSettings = new GlobalSettings
            {
                ColorMode = ColorMode.Color,
                Orientation = ParseOrientation(options.Orientation),
                PaperSize = ParsePaperKind(options.PaperSize),
                Margins = new MarginSettings { Top = 15, Bottom = 18, Left = 10, Right = 10 }
            },
            Objects =
            {
                new ObjectSettings
                {
                    HtmlContent = html,
                    WebSettings = new WebSettings
                    {
                        DefaultEncoding = "utf-8",
                        LoadImages = true,
                        EnableIntelligentShrinking = true
                    },
                    HeaderSettings = BuildHeaderSettings(options),
                    FooterSettings = BuildFooterSettings(options)
                }
            }
        };

        var bytes = await Task.Run(() => _converter.Value.Convert(document), cancellationToken);
        return await TempStreamFactory.CreateFromBytesAsync(bytes, ".pdf", cancellationToken);
    }

    private static Orientation ParseOrientation(string? orientation)
        => string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase)
            ? Orientation.Landscape
            : Orientation.Portrait;

    private static PaperKind ParsePaperKind(string? paperSize)
        => string.Equals(paperSize, "letter", StringComparison.OrdinalIgnoreCase)
            ? PaperKind.Letter
            : PaperKind.A4;

    private static HeaderSettings? BuildHeaderSettings(PdfRenderOptions options)
    {
        if (!options.IncludeHeader || string.IsNullOrWhiteSpace(options.HeaderText))
        {
            return null;
        }

        return new HeaderSettings
        {
            FontSize = 9,
            Right = options.HeaderText,
            Line = true,
            Spacing = 3
        };
    }

    private static FooterSettings? BuildFooterSettings(PdfRenderOptions options)
    {
        if (!options.IncludeFooter && string.IsNullOrWhiteSpace(options.FooterText))
        {
            return new FooterSettings
            {
                FontSize = 9,
                Center = "Page [page] of [toPage]"
            };
        }

        var footer = string.IsNullOrWhiteSpace(options.FooterText)
            ? "Page [page] of [toPage]"
            : $"{options.FooterText} | Page [page] of [toPage]";

        return new FooterSettings
        {
            FontSize = 9,
            Center = footer,
            Line = true,
            Spacing = 3
        };
    }
}

using DinkToPdf;
using DinkToPdf.Contracts;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using PdfComposer.Api.Infrastructure.Utilities;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Extgstate;
using iText.IO.Font.Constants;
using System.IO;

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

    private static void PostProcess(string sourcePath, string outputPath, PdfPostProcessOptions options)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(reader, writer);

        var font = BuildFont(options.CustomFontPath);
        var totalPages = pdfDoc.GetNumberOfPages();

        using var document = new Document(pdfDoc); // create once, reuse for all pages

        for (var i = 1; i <= totalPages; i++)
        {
            var page = pdfDoc.GetPage(i);
            var pageSize = page.GetPageSize();
            var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);

            if (!string.IsNullOrWhiteSpace(options.WatermarkText))
            {
                WriteWatermark(options.WatermarkText!, font, canvas, document, pageSize, i);
            }

            if (!string.IsNullOrWhiteSpace(options.HeaderText))
            {
                document.ShowTextAligned(
                    new Paragraph(options.HeaderText).SetFont(font).SetFontSize(9),
                    pageSize.GetWidth() / 2,
                    pageSize.GetTop() - 16,
                    i,
                    TextAlignment.CENTER,
                    VerticalAlignment.MIDDLE,
                    0);
            }

            if (!string.IsNullOrWhiteSpace(options.FooterText))
            {
                document.ShowTextAligned(
                    new Paragraph(options.FooterText).SetFont(font).SetFontSize(9),
                    pageSize.GetWidth() / 2,
                    pageSize.GetBottom() + 14,
                    i,
                    TextAlignment.CENTER,
                    VerticalAlignment.MIDDLE,
                    0);
            }

            if (options.EnablePageNumbers)
            {
                document.ShowTextAligned(
                    new Paragraph($"Page {i} of {totalPages}").SetFont(font).SetFontSize(9),
                    pageSize.GetRight() - 24,
                    pageSize.GetBottom() + 14,
                    i,
                    TextAlignment.RIGHT,
                    VerticalAlignment.MIDDLE,
                    0);
            }
        }
    }

    private static PdfFont BuildFont(string? customFontPath)
    {
        if (!string.IsNullOrWhiteSpace(customFontPath) && File.Exists(customFontPath))
        {
            return PdfFontFactory.CreateFont(customFontPath, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
        }

        return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
    }

    private static void WriteWatermark(
        string text,
        PdfFont font,
        PdfCanvas canvas,
        Document doc,
        iText.Kernel.Geom.Rectangle pageSize,
        int pageNumber)
    {
        var gs1 = new PdfExtGState().SetFillOpacity(0.20f);
        canvas.SaveState();
        canvas.SetExtGState(gs1);

        doc.ShowTextAligned(
            new Paragraph(text)
                .SetFont(font)
                .SetFontSize(46)
                .SetFontColor(ColorConstants.LIGHT_GRAY),
            pageSize.GetWidth() / 2,
            pageSize.GetHeight() / 2,
            pageNumber,
            TextAlignment.CENTER,
            VerticalAlignment.MIDDLE,
            (float)(Math.PI / 4));

        canvas.RestoreState();
    }
}

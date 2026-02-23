using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Kernel.Pdf.Xobject;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using IOPath = System.IO.Path;

namespace PdfComposer.Api.Infrastructure.Services;

public sealed class ITextPdfMergeService : IPdfMergeService
{
    public async Task<Stream> MergeAsync(IReadOnlyList<Stream> pdfStreams, PdfPostProcessOptions options, CancellationToken cancellationToken = default)
    {
        if (pdfStreams.Count == 0)
        {
            throw new InvalidOperationException("At least one PDF stream is required for merge.");
        }

        var mergedTempPath = IOPath.Combine(IOPath.GetTempPath(), $"pdf-merge-{Guid.NewGuid():N}.pdf");

        await Task.Run(() => MergeCore(pdfStreams, mergedTempPath), cancellationToken);

        var outputPath = mergedTempPath;
        if (RequiresPostProcessing(options))
        {
            var postProcessedPath = IOPath.Combine(IOPath.GetTempPath(), $"pdf-post-{Guid.NewGuid():N}.pdf");
            await Task.Run(() => PostProcess(mergedTempPath, postProcessedPath, options), cancellationToken);
            File.Delete(mergedTempPath);
            outputPath = postProcessedPath;
        }

        var outStream = new FileStream(
            outputPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
            });

        return outStream;
    }

    private static void MergeCore(IReadOnlyList<Stream> pdfStreams, string outputPath)
    {
        using var writer = new PdfWriter(outputPath);
        using var destination = new PdfDocument(writer);
        Rectangle? targetPageSize = null;

        foreach (var stream in pdfStreams)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var reader = new PdfReader(stream);
            using var source = new PdfDocument(reader);

            for (var pageNo = 1; pageNo <= source.GetNumberOfPages(); pageNo++)
            {
                var sourcePage = source.GetPage(pageNo);
                var sourceSize = sourcePage.GetPageSize();
                targetPageSize ??= new Rectangle(sourceSize.GetWidth(), sourceSize.GetHeight());

                var destinationPage = destination.AddNewPage(new PageSize(targetPageSize));
                var xObject = sourcePage.CopyAsFormXObject(destination);
                DrawScaledAndCentered(destinationPage, xObject, sourceSize, targetPageSize);
            }
        }
    }

    private static void DrawScaledAndCentered(PdfPage destinationPage, PdfFormXObject sourceXObject, Rectangle sourceSize, Rectangle targetSize)
    {
        var scaleX = targetSize.GetWidth() / sourceSize.GetWidth();
        var scaleY = targetSize.GetHeight() / sourceSize.GetHeight();
        var scale = Math.Min(scaleX, scaleY);

        var drawWidth = sourceSize.GetWidth() * scale;
        var drawHeight = sourceSize.GetHeight() * scale;
        var offsetX = (targetSize.GetWidth() - drawWidth) / 2f;
        var offsetY = (targetSize.GetHeight() - drawHeight) / 2f;

        var canvas = new PdfCanvas(destinationPage);
        canvas.AddXObjectWithTransformationMatrix(sourceXObject, scale, 0, 0, scale, offsetX, offsetY);
    }

    private static bool RequiresPostProcessing(PdfPostProcessOptions options)
    {
        return options.EnablePageNumbers
               || !string.IsNullOrWhiteSpace(options.WatermarkText)
               || !string.IsNullOrWhiteSpace(options.HeaderText)
               || !string.IsNullOrWhiteSpace(options.FooterText);
    }

    private static void PostProcess(string sourcePath, string outputPath, PdfPostProcessOptions options)
    {
        using var reader = new PdfReader(sourcePath);
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(reader, writer);

        var font = BuildFont(options.CustomFontPath);
        var totalPages = pdfDoc.GetNumberOfPages();

        using var document = new Document(pdfDoc); // create once

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

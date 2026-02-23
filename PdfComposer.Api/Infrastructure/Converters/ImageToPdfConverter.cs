using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Layout;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using IOPath = System.IO.Path;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class ImageToPdfConverter : IFileToPdfConverter
{
    public bool CanConvert(SupportedFileType fileType)
        => fileType is SupportedFileType.Png or SupportedFileType.Jpg or SupportedFileType.Jpeg;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        var inputExtension = IOPath.GetExtension(fileName);
        var imagePath = IOPath.Combine(IOPath.GetTempPath(), $"img-{Guid.NewGuid():N}{inputExtension}");
        var outputPath = IOPath.Combine(IOPath.GetTempPath(), $"img-{Guid.NewGuid():N}.pdf");

        await using (var imageFile = new FileStream(
                         imagePath,
                         new FileStreamOptions
                         {
                             Mode = FileMode.CreateNew,
                             Access = FileAccess.Write,
                             Share = FileShare.None,
                             BufferSize = 81920,
                             Options = FileOptions.Asynchronous
                         }))
        {
            if (input.CanSeek)
            {
                input.Position = 0;
            }

            await input.CopyToAsync(imageFile, cancellationToken);
        }

        using var image = await SixLabors.ImageSharp.Image.LoadAsync(imagePath, cancellationToken);
        var width = image.Width;
        var height = image.Height;

        using (var writer = new PdfWriter(outputPath))
        using (var pdf = new PdfDocument(writer))
        {
            // Use A4 as a standard page size. Rotate for landscape images.
            var standardPage = iText.Kernel.Geom.PageSize.A4;
            var pageSize = (width > height) ? standardPage.Rotate() : standardPage;
            pdf.SetDefaultPageSize(pageSize);

            // Document with zero margins to use full page
            using var document = new Document(pdf);
            document.SetMargins(0, 0, 0, 0);

            var pdfImage = new iText.Layout.Element.Image(ImageDataFactory.Create(imagePath));

            // Scale the image to fit within the page (preserve aspect ratio)
            pdfImage.ScaleToFit(pageSize.GetWidth(), pageSize.GetHeight());

            // Center horizontally and vertically using fixed position
            var scaledWidth = pdfImage.GetImageScaledWidth();
            var scaledHeight = pdfImage.GetImageScaledHeight();
            var x = (pageSize.GetWidth() - scaledWidth) / 2;
            var y = (pageSize.GetHeight() - scaledHeight) / 2;

            document.Add(pdfImage.SetFixedPosition(1, x, y));

            document.Close();
        }

        File.Delete(imagePath);

        return new FileStream(
            outputPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
            });
    }
}

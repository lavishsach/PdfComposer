namespace PdfComposer.Api.Application.Models;

public sealed class PdfRenderOptions
{
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "Portrait";
    public bool IncludeHeader { get; set; }
    public string? HeaderText { get; set; }
    public bool IncludeFooter { get; set; }
    public string? FooterText { get; set; }
    public string? CustomFontCss { get; set; }
}

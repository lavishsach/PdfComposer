namespace PdfComposer.Api.Application.Models;

public sealed class PdfPostProcessOptions
{
    public bool EnablePageNumbers { get; set; } = true;
    public string? WatermarkText { get; set; }
    public string? CustomFontPath { get; set; }
    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }
}

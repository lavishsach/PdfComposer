using System.Text.Json;

namespace PdfComposer.Api.Application.Interfaces;

public interface IHtmlTemplateEngine
{
    Task<string> RenderAsync(string templateHtml, JsonElement data, CancellationToken cancellationToken = default);
}

using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Infrastructure.Utilities;
using RazorLight;
using System.Text.Json;

namespace PdfComposer.Api.Infrastructure.Services;

public sealed class RazorHtmlTemplateEngine : IHtmlTemplateEngine
{
    private readonly RazorLightEngine _engine;

    public RazorHtmlTemplateEngine()
    {
        _engine = new RazorLightEngineBuilder()
            .UseEmbeddedResourcesProject(typeof(RazorHtmlTemplateEngine))
            .UseMemoryCachingProvider()
            .Build();
    }

    public async Task<string> RenderAsync(string templateHtml, JsonElement data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var model = JsonElementMapper.ToObject(data);
        var templateKey = $"tpl-{Guid.NewGuid():N}";
        return await _engine.CompileRenderStringAsync(templateKey, templateHtml, model);
    }
}

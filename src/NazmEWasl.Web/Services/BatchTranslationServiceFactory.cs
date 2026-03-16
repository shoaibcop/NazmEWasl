using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class BatchTranslationServiceFactory
{
    private readonly IServiceProvider _sp;

    public BatchTranslationServiceFactory(IServiceProvider sp) => _sp = sp;

    public IBatchTranslationService Create(string provider) =>
        provider.Trim().ToLowerInvariant() switch
        {
            "openai" => _sp.GetRequiredService<OpenAiBatchTranslationService>(),
            _        => _sp.GetRequiredService<ClaudeBatchTranslationService>()  // claude + gemini fallback
        };
}

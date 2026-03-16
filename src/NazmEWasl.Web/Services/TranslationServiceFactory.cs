using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class TranslationServiceFactory : ITranslationServiceFactory
{
    private readonly IServiceProvider _sp;

    public TranslationServiceFactory(IServiceProvider sp)
    {
        _sp = sp;
    }

    public ITranslationService Create(string? provider) =>
        (provider ?? "Claude").Trim().ToLowerInvariant() switch
        {
            "gemini" => _sp.GetRequiredService<GeminiTranslationService>(),
            "openai" => _sp.GetRequiredService<OpenAITranslationService>(),
            _        => _sp.GetRequiredService<TranslationService>()
        };
}

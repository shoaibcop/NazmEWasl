namespace NazmEWasl.Web.Services.Interfaces;

public interface ITranslationServiceFactory
{
    /// <summary>Resolve the correct ITranslationService by provider name ("Claude" or "Gemini").</summary>
    ITranslationService Create(string? provider);
}

namespace NazmEWasl.Web.Models.ViewModels;

public class SettingsViewModel
{
    // Claude
    public string AnthropicApiKey   { get; set; } = "";
    public string AnthropicModel    { get; set; } = "claude-sonnet-4-6";
    public string AnthropicMaxTokens { get; set; } = "8000";

    // Gemini
    public string GeminiApiKey    { get; set; } = "";
    public string GeminiModel     { get; set; } = "gemini-2.0-flash";
    public string GeminiMaxTokens { get; set; } = "8000";

    // OpenAI
    public string OpenAiApiKey    { get; set; } = "";
    public string OpenAiModel     { get; set; } = "gpt-4o";
    public string OpenAiMaxTokens { get; set; } = "8000";

    // Card Appearance
    public string PersianFontSize   { get; set; } = "56";
    public string RomanUrduFontSize { get; set; } = "34";
    public string FontFamily        { get; set; } = "Amiri";
    public string OverlayOpacity    { get; set; } = "0.72";

    // Video
    public string Fps                { get; set; } = "24";
    public string Width              { get; set; } = "1080";
    public string Height             { get; set; } = "1080";
    public string EndCardDurationSec { get; set; } = "5";
}

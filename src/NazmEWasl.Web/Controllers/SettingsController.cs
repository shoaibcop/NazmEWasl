using Microsoft.AspNetCore.Mvc;
using NazmEWasl.Web.Models.ViewModels;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Controllers;

public class SettingsController : Controller
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings) => _settings = settings;

    // GET /settings
    public IActionResult Index()
    {
        var vm = new SettingsViewModel
        {
            AnthropicApiKey    = _settings.Get("Anthropic:ApiKey"),
            AnthropicModel     = _settings.Get("Anthropic:Model",    "claude-sonnet-4-6"),
            AnthropicMaxTokens = _settings.Get("Anthropic:MaxTokens","8000"),

            GeminiApiKey    = _settings.Get("Gemini:ApiKey"),
            GeminiModel     = _settings.Get("Gemini:Model",    "gemini-2.0-flash"),
            GeminiMaxTokens = _settings.Get("Gemini:MaxTokens","8000"),

            OpenAiApiKey    = _settings.Get("OpenAI:ApiKey"),
            OpenAiModel     = _settings.Get("OpenAI:Model",    "gpt-4o"),
            OpenAiMaxTokens = _settings.Get("OpenAI:MaxTokens","8000"),

            PersianFontSize   = _settings.Get("Card.PersianFontSize",   "56"),
            RomanUrduFontSize = _settings.Get("Card.RomanUrduFontSize", "34"),
            EnglishFontSize   = _settings.Get("Card.EnglishFontSize",   "26"),
            HindiFontSize     = _settings.Get("Card.HindiFontSize",     "26"),
            FontFamily        = _settings.Get("Card.FontFamily",        "Amiri"),
            OverlayOpacity    = _settings.Get("Card.OverlayOpacity",    "0.72"),

            Fps                = _settings.Get("Video.Fps",               "24"),
            Width              = _settings.Get("Video.Width",             "1080"),
            Height             = _settings.Get("Video.Height",            "1080"),
            EndCardDurationSec = _settings.Get("Video.EndCardDurationSec","5"),
            VideoCodec         = _settings.Get("Video.Codec",            "libx264"),
            VideoCrf           = _settings.Get("Video.Crf",              "23"),
            VideoPreset        = _settings.Get("Video.Preset",           "fast"),
            VideoFormat        = _settings.Get("Video.Format",           "mp4"),
        };
        return View(vm);
    }

    // POST /settings
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel vm)
    {
        _settings.Set("Anthropic:ApiKey",    vm.AnthropicApiKey);
        _settings.Set("Anthropic:Model",     vm.AnthropicModel);
        _settings.Set("Anthropic:MaxTokens", vm.AnthropicMaxTokens);

        _settings.Set("Gemini:ApiKey",    vm.GeminiApiKey);
        _settings.Set("Gemini:Model",     vm.GeminiModel);
        _settings.Set("Gemini:MaxTokens", vm.GeminiMaxTokens);

        _settings.Set("OpenAI:ApiKey",    vm.OpenAiApiKey);
        _settings.Set("OpenAI:Model",     vm.OpenAiModel);
        _settings.Set("OpenAI:MaxTokens", vm.OpenAiMaxTokens);

        _settings.Set("Card.PersianFontSize",   vm.PersianFontSize);
        _settings.Set("Card.RomanUrduFontSize", vm.RomanUrduFontSize);
        _settings.Set("Card.EnglishFontSize",   vm.EnglishFontSize);
        _settings.Set("Card.HindiFontSize",     vm.HindiFontSize);
        _settings.Set("Card.FontFamily",        vm.FontFamily);
        _settings.Set("Card.OverlayOpacity",    vm.OverlayOpacity);

        _settings.Set("Video.Fps",               vm.Fps);
        _settings.Set("Video.Width",             vm.Width);
        _settings.Set("Video.Height",            vm.Height);
        _settings.Set("Video.EndCardDurationSec",vm.EndCardDurationSec);
        _settings.Set("Video.Codec",             vm.VideoCodec);
        _settings.Set("Video.Crf",               vm.VideoCrf);
        _settings.Set("Video.Preset",            vm.VideoPreset);
        _settings.Set("Video.Format",            vm.VideoFormat);

        await _settings.SaveAsync();

        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }
}

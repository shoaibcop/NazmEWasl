using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Models.ViewModels;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Controllers;

public class TranslationController : Controller
{
    private readonly AppDbContext _db;
    private readonly ITranslationServiceFactory _translationFactory;

    public TranslationController(AppDbContext db, ITranslationServiceFactory translationFactory)
    {
        _db = db;
        _translationFactory = translationFactory;
    }

    // GET /translation/{songId}/review
    public async Task<IActionResult> Review(int songId)
    {
        var song = await _db.Songs
            .Include(s => s.Verses)
            .FirstOrDefaultAsync(s => s.Id == songId);

        if (song == null) return NotFound();

        return View(new TranslationReviewViewModel
        {
            Song   = song,
            Verses = song.Verses.OrderBy(v => v.VerseNumber).ToList()
        });
    }

    // POST /translation/{songId}/verse/{n}/save
    [HttpPost]
    public async Task<IActionResult> SaveVerse(
        int songId, int n,
        string? romanUrdu,
        string? englishText,
        string? hindiText)
    {
        var verse = await _db.Verses
            .Include(v => v.Song)
            .FirstOrDefaultAsync(v => v.Song.Id == songId && v.VerseNumber == n);

        if (verse == null) return NotFound();

        if (romanUrdu   != null) verse.RomanUrdu   = romanUrdu;
        if (englishText != null) verse.EnglishText = englishText;
        if (hindiText   != null) verse.HindiText   = hindiText;
        verse.LastEditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Verse {n} saved.";
        return RedirectToAction(nameof(Review), new { songId });
    }

    // POST /translation/{songId}/verse/{n}/retranslate
    [HttpPost]
    public async Task<IActionResult> RetranslateVerse(int songId, int n, string? language = null)
    {
        var song = await _db.Songs
            .Include(s => s.Verses)
            .FirstOrDefaultAsync(s => s.Id == songId);

        if (song == null) return NotFound();

        var verse = song.Verses.FirstOrDefault(v => v.VerseNumber == n);
        if (verse == null) return NotFound();

        // Re-translate single language if specified, otherwise all selected languages
        IReadOnlyList<string> targetLanguages = !string.IsNullOrWhiteSpace(language)
            ? [language]
            : song.ParsedLanguages;

        var fullSongText = string.Join("\n",
            song.Verses.OrderBy(v => v.VerseNumber).Select(v => v.PersianText));

        try
        {
            var translation = _translationFactory.Create(song.LastTranslationProvider);
            var result = await translation.RetranslateVerseAsync(
                n, verse.PersianText, song.Title, fullSongText, targetLanguages);

            if (result.RomanUrdu   != null) verse.RomanUrdu   = result.RomanUrdu;
            if (result.EnglishText != null) verse.EnglishText = result.EnglishText;
            if (result.HindiText   != null) verse.HindiText   = result.HindiText;
            if (result.Keywords    != null && result.Keywords.Count > 0)
                verse.KeywordsJson = JsonSerializer.Serialize(result.Keywords);
            verse.LastEditedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Verse {n} re-translated.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Re-translation failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Review), new { songId });
    }

    // POST /translation/{songId}/approve
    [HttpPost]
    public async Task<IActionResult> Approve(int songId)
    {
        var song = await _db.Songs
            .Include(s => s.Verses)
            .FirstOrDefaultAsync(s => s.Id == songId);

        if (song == null) return NotFound();

        foreach (var verse in song.Verses)
            verse.IsApproved = true;

        song.Status = SongStatus.Approved;
        song.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "All verses approved. You can now generate assets.";
        return RedirectToAction("Detail", "Songs", new { id = songId });
    }
}

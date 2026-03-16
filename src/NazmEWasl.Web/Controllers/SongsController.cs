using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Models.ViewModels;
using NazmEWasl.Web.Services;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Controllers;

public class SongsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStorageService _storage;
    private readonly IPipelineService _pipeline;
    private readonly ITranslationServiceFactory _translationFactory;
    private readonly ILrcLibService _lrcLib;
    private readonly IBackgroundPipelineQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BatchTranslationServiceFactory _batchFactory;

    public SongsController(
        AppDbContext db,
        IStorageService storage,
        IPipelineService pipeline,
        ITranslationServiceFactory translationFactory,
        ILrcLibService lrcLib,
        IBackgroundPipelineQueue queue,
        IServiceScopeFactory scopeFactory,
        BatchTranslationServiceFactory batchFactory)
    {
        _db = db;
        _storage = storage;
        _pipeline = pipeline;
        _translationFactory = translationFactory;
        _lrcLib = lrcLib;
        _queue = queue;
        _scopeFactory = scopeFactory;
        _batchFactory = batchFactory;
    }

    // GET /songs
    public async Task<IActionResult> Index()
    {
        var songs = await _db.Songs
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return View(songs);
    }

    // GET /songs/create
    public IActionResult Create() => View(new SongCreateViewModel());

    // POST /songs/create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SongCreateViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var lrcResult = await _lrcLib.GetLyricsAsync(model.Title, model.Artist);
        if (lrcResult == null)
        {
            ModelState.AddModelError(string.Empty,
                $"Could not find \"{model.Title}\" by {model.Artist} on lrclib.net. " +
                "Please check that the title and artist exactly match what is listed on lrclib.net.");
            return View(model);
        }

        var parsedVerses = _lrcLib.ParseLyrics(lrcResult);
        if (parsedVerses.Count == 0)
        {
            ModelState.AddModelError(string.Empty,
                "lrclib returned an empty lyrics result for this song. Please try a different title or artist spelling.");
            return View(model);
        }

        var songId = Guid.NewGuid().ToString("N");
        _storage.InitialiseSongFolders(songId);

        var audioExt = Path.GetExtension(model.AudioFile.FileName).ToLowerInvariant();
        var audioFilename = audioExt == ".wav" ? "audio.wav" : "audio.mp3";
        await using (var s = model.AudioFile.OpenReadStream())
            await _storage.SaveInputFileAsync(songId, audioFilename, s);

        var bgExt = Path.GetExtension(model.BackgroundFile.FileName).ToLowerInvariant();
        var bgFilename = bgExt == ".png" ? "background.png" : "background.jpg";
        await using (var s = model.BackgroundFile.OpenReadStream())
            await _storage.SaveInputFileAsync(songId, bgFilename, s);

        var song = new Song
        {
            SongId    = songId,
            Title     = model.Title,
            Artist    = model.Artist,
            Year      = model.Year,
            Notes     = model.Notes,
            Status    = SongStatus.Uploaded,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var v in parsedVerses)
        {
            song.Verses.Add(new Verse
            {
                VerseNumber = v.VerseNumber,
                PersianText = v.Text,
                StartMs     = v.StartMs,
                EndMs       = v.EndMs
            });
        }

        _db.Songs.Add(song);
        await _db.SaveChangesAsync();

        if (!lrcResult.HasSyncedLyrics)
            TempData["Warning"] = "Lyrics were found but without timing data. Verse timestamps are evenly spaced — video sync will be approximate.";
        else
            TempData["Success"] = $"Lyrics fetched from lrclib: {parsedVerses.Count} verses with timestamps.";

        return RedirectToAction(nameof(Detail), new { id = song.Id });
    }

    // GET /songs/{id}
    public async Task<IActionResult> Detail(int id)
    {
        var song = await _db.Songs
            .Include(s => s.Verses)
            .Include(s => s.Jobs)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (song == null) return NotFound();

        var activeBatch = await _db.TranslationBatches
            .Where(b => b.SongId == song.Id && (b.Status == BatchStatus.Submitted || b.Status == BatchStatus.Processing))
            .OrderByDescending(b => b.SubmittedAt)
            .FirstOrDefaultAsync();

        ViewBag.ActiveBatch = activeBatch;

        return View(new SongDetailViewModel
        {
            Song      = song,
            LatestJob = song.Jobs.OrderByDescending(j => j.StartedAt).FirstOrDefault()
        });
    }

    // POST /songs/{id}/translate
    [HttpPost]
    public async Task<IActionResult> Translate(int id, List<string>? selectedLanguages, string? provider, string? translationMode)
    {
        var song = await _db.Songs
            .Include(s => s.Verses)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (song == null) return NotFound();

        if (selectedLanguages == null || selectedLanguages.Count == 0)
            selectedLanguages = ["RomanUrdu"];

        var resolvedProvider = string.IsNullOrWhiteSpace(provider) ? "Claude" : provider.Trim();
        song.SelectedLanguages       = string.Join(",", selectedLanguages);
        song.LastTranslationProvider = resolvedProvider;
        await _db.SaveChangesAsync();

        var isBatch = translationMode?.ToLowerInvariant() == "batch";

        // Gemini has no batch API — silently fall back to realtime
        if (isBatch && resolvedProvider.ToLowerInvariant() == "gemini")
            isBatch = false;

        if (isBatch)
        {
            song.Status = SongStatus.Translating;
            await _db.SaveChangesAsync();

            try
            {
                var batchSvc     = _batchFactory.Create(resolvedProvider);
                var externalId   = await batchSvc.SubmitBatchAsync(song, selectedLanguages);

                var batch = new TranslationBatch
                {
                    SongId          = song.Id,
                    Provider        = resolvedProvider,
                    ExternalBatchId = externalId,
                    Status          = BatchStatus.Submitted,
                    SubmittedAt     = DateTime.UtcNow
                };
                _db.TranslationBatches.Add(batch);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"Batch submitted to {resolvedProvider} (ID: {externalId}). Results will appear within 24h.";
            }
            catch (Exception ex)
            {
                song.Status = SongStatus.Uploaded;
                await _db.SaveChangesAsync();
                TempData["Error"] = $"Batch submission failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Detail), new { id });
        }

        // Realtime — enqueue to background queue
        song.Status = SongStatus.Translating;
        await _db.SaveChangesAsync();

        var songId    = song.SongId;
        var songDbId  = song.Id;
        var langs     = selectedLanguages.ToList();
        var prov      = resolvedProvider;

        _queue.Enqueue(async ct =>
        {
            using var scope      = _scopeFactory.CreateScope();
            var db               = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var translationFac   = scope.ServiceProvider.GetRequiredService<ITranslationServiceFactory>();

            var s = await db.Songs.Include(x => x.Verses).FirstOrDefaultAsync(x => x.Id == songDbId, ct);
            if (s == null) return;

            try
            {
                var translation   = translationFac.Create(prov);
                var orderedVerses = s.Verses.OrderBy(v => v.VerseNumber).ToList();
                var persianVerses = orderedVerses.Select(v => v.PersianText).ToList();
                var fullSongText  = string.Join("\n", persianVerses);

                var translations = await translation.TranslateVersesAsync(
                    persianVerses, s.Title, s.Artist, fullSongText, langs);

                foreach (var t in translations)
                {
                    var verse = s.Verses.FirstOrDefault(v => v.VerseNumber == t.VerseNumber);
                    if (verse != null)
                    {
                        if (t.RomanUrdu   != null) verse.RomanUrdu   = t.RomanUrdu;
                        if (t.EnglishText != null) verse.EnglishText = t.EnglishText;
                        if (t.HindiText   != null) verse.HindiText   = t.HindiText;
                        if (t.Keywords    != null && t.Keywords.Count > 0)
                            verse.KeywordsJson = JsonSerializer.Serialize(t.Keywords);
                        verse.LastEditedAt = DateTime.UtcNow;
                    }
                }

                s.Status = SongStatus.AwaitingReview;
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                s.Status = SongStatus.Uploaded;
                await db.SaveChangesAsync(CancellationToken.None);
            }
        });

        TempData["Success"] = $"Translation queued via {resolvedProvider} ({string.Join(", ", selectedLanguages)}). Page will update when complete.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    // POST /songs/translate-selected
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TranslateSelected(int[] songIds, string? provider)
    {
        if (songIds == null || songIds.Length == 0)
        {
            TempData["Error"] = "No songs selected.";
            return RedirectToAction(nameof(Index));
        }

        var resolvedProvider = string.IsNullOrWhiteSpace(provider) ? "Claude" : provider.Trim();

        if (resolvedProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Gemini does not support batch translation.";
            return RedirectToAction(nameof(Index));
        }

        var batchSvc  = _batchFactory.Create(resolvedProvider);
        var submitted = 0;
        var errors    = new List<string>();

        foreach (var id in songIds)
        {
            var song = await _db.Songs
                .Include(s => s.Verses)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (song == null) continue;

            var selectedLanguages = string.IsNullOrWhiteSpace(song.SelectedLanguages)
                ? new List<string> { "RomanUrdu" }
                : song.SelectedLanguages.Split(',').ToList();

            song.LastTranslationProvider = resolvedProvider;
            song.Status = SongStatus.Translating;
            await _db.SaveChangesAsync();

            try
            {
                var externalId = await batchSvc.SubmitBatchAsync(song, selectedLanguages);

                _db.TranslationBatches.Add(new TranslationBatch
                {
                    SongId          = song.Id,
                    Provider        = resolvedProvider,
                    ExternalBatchId = externalId,
                    Status          = BatchStatus.Submitted,
                    SubmittedAt     = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                submitted++;
            }
            catch (Exception ex)
            {
                song.Status = SongStatus.Uploaded;
                await _db.SaveChangesAsync();
                errors.Add($"{song.Title}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            TempData["Error"] = $"Some submissions failed: {string.Join("; ", errors)}";

        if (submitted > 0)
            TempData["Success"] = $"{submitted} song(s) submitted to {resolvedProvider} for batch translation.";

        return RedirectToAction(nameof(Index));
    }

    // GET /songs/{id}/batch-status
    [HttpGet]
    public async Task<IActionResult> CheckBatchStatus(int id)
    {
        var song = await _db.Songs.FindAsync(id);
        if (song == null) return NotFound();

        var batch = await _db.TranslationBatches
            .Where(b => b.SongId == id && (b.Status == BatchStatus.Submitted || b.Status == BatchStatus.Processing))
            .OrderByDescending(b => b.SubmittedAt)
            .FirstOrDefaultAsync();

        if (batch == null)
            return Json(new { status = "none", message = "No pending batch." });

        try
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var batchFac    = scope.ServiceProvider.GetRequiredService<BatchTranslationServiceFactory>();
            var svc         = batchFac.Create(batch.Provider);
            var complete    = await svc.PollAndApplyResultsAsync(batch);

            if (complete)
            {
                batch.Status      = BatchStatus.Complete;
                batch.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Json(new { status = "complete", message = "Batch complete. Translations applied." });
            }

            batch.Status = BatchStatus.Processing;
            await _db.SaveChangesAsync();
            return Json(new { status = "processing", message = "Still processing..." });
        }
        catch (Exception ex)
        {
            return Json(new { status = "error", message = ex.Message });
        }
    }

    // DELETE /songs/{id}
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var song = await _db.Songs.FindAsync(id);
        if (song == null) return NotFound();

        var inputPath = _storage.GetInputPath(song.SongId);
        var songRoot  = Directory.GetParent(inputPath)?.FullName;
        if (songRoot != null && Directory.Exists(songRoot))
            Directory.Delete(songRoot, recursive: true);

        _db.Songs.Remove(song);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Song '{song.Title}' deleted.";
        return RedirectToAction(nameof(Index));
    }
}

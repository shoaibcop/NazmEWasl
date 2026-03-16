using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Services;
using NazmEWasl.Web.Services.Interfaces;
using Domain = NazmEWasl.Web.Models.Domain;

namespace NazmEWasl.Web.Controllers;

public class AssetsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStorageService _storage;
    private readonly IBackgroundPipelineQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CancellationRegistry _registry;

    public AssetsController(
        AppDbContext db,
        IStorageService storage,
        IBackgroundPipelineQueue queue,
        IServiceScopeFactory scopeFactory,
        CancellationRegistry registry)
    {
        _db = db;
        _storage = storage;
        _queue = queue;
        _scopeFactory = scopeFactory;
        _registry = registry;
    }

    // GET /assets/{songId}
    public async Task<IActionResult> Index(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        ViewBag.CardPaths = _storage.GetCardPaths(song.SongId).Select(Path.GetFileName).ToList();
        ViewBag.VideoExists = _storage.GetVideoPath(song.SongId) != null;
        return View(song);
    }

    // POST /assets/{songId}/generate
    [HttpPost]
    public async Task<IActionResult> Generate(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        song.Status = SongStatus.GeneratingAssets;
        await _db.SaveChangesAsync();

        var songGuid = song.SongId;
        var songDbId = song.Id;
        var ct = _registry.Register(songGuid);

        _queue.Enqueue(async _ =>
        {
            using var scope  = _scopeFactory.CreateScope();
            var pipeline     = scope.ServiceProvider.GetRequiredService<IPipelineService>();
            var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reg          = scope.ServiceProvider.GetRequiredService<CancellationRegistry>();

            var s = await db.Songs.FindAsync(songDbId);
            if (s == null) { reg.Remove(songGuid); return; }

            try
            {
                await pipeline.RunCardRenderingAsync(songGuid, ct);
                await pipeline.RunVideoRenderingAsync(songGuid, ct);
            }
            catch
            {
                s = await db.Songs.FindAsync(songDbId);
                if (s != null && s.Status == SongStatus.GeneratingAssets)
                {
                    s.Status = SongStatus.Approved;
                    await db.SaveChangesAsync();
                }
            }
            finally
            {
                reg.Remove(songGuid);
            }
        });

        return RedirectToAction(nameof(Progress), new { songId });
    }

    // POST /assets/{songId}/cancel  — Feature 2
    [HttpPost]
    public async Task<IActionResult> Cancel(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        _registry.Cancel(song.SongId);
        TempData["Success"] = "Cancellation requested.";
        return RedirectToAction(nameof(Index), new { songId });
    }

    // POST /assets/{songId}/delete-assets  — Feature 3
    [HttpPost]
    public async Task<IActionResult> DeleteAssets(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        _storage.DeleteOutputs(song.SongId);
        song.Status = SongStatus.Approved;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Generated files deleted. Translations are preserved.";
        return RedirectToAction(nameof(Index), new { songId });
    }

    // POST /assets/{songId}/open-folder  — Feature 4
    [HttpPost]
    public async Task<IActionResult> OpenFolder(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        var folderPath = _storage.GetOutputPath(song.SongId, "");
        if (!Directory.Exists(folderPath))
            folderPath = _storage.GetInputPath(song.SongId);

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            TempData["Warning"] = "Could not open folder (only works when running locally on Windows).";
        }

        return RedirectToAction(nameof(Index), new { songId });
    }

    // GET /assets/{songId}/progress
    public async Task<IActionResult> Progress(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();
        return View(song);
    }

    // GET /assets/{songId}/poll-status
    [HttpGet]
    public async Task<IActionResult> PollStatus(int songId)
    {
        var song = await _db.Songs
            .Include(s => s.Jobs)
            .FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null) return NotFound();

        if (song.Status == SongStatus.Complete)
            return Json(new { percent = 100, message = "Complete!", status = "complete" });

        var latest = song.Jobs.OrderByDescending(j => j.StartedAt).FirstOrDefault();

        if (latest?.Status == Domain.JobStatus.Failed)
            return Json(new { percent = -1, message = latest.ErrorMessage ?? "Pipeline failed.", status = "failed" });

        var (percent, message) = (latest?.Step, latest?.Status) switch
        {
            (PipelineStep.CardRendering,  Domain.JobStatus.Running)  => (35, "Rendering verse cards..."),
            (PipelineStep.CardRendering,  Domain.JobStatus.Complete) => (60, "Cards done — rendering video..."),
            (PipelineStep.VideoRendering, Domain.JobStatus.Running)  => (80, "Assembling final video..."),
            (PipelineStep.VideoRendering, Domain.JobStatus.Complete) => (95, "Finalizing..."),
            _                                                         => (5,  "Queued, starting soon...")
        };

        return Json(new { percent, message, status = "running" });
    }

    // GET /assets/{songId}/job-log  — Feature 9
    [HttpGet]
    public async Task<IActionResult> JobLog(int songId)
    {
        var song = await _db.Songs
            .Include(s => s.Jobs)
            .FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null) return NotFound();

        var latest = song.Jobs.OrderByDescending(j => j.StartedAt).FirstOrDefault();
        var complete = song.Status == SongStatus.Complete
                    || song.Status == SongStatus.AwaitingReview
                    || latest?.Status == Domain.JobStatus.Failed
                    || latest?.Status == Domain.JobStatus.Complete;

        return Json(new
        {
            output   = latest?.Output ?? "",
            complete,
            status   = latest?.Status.ToString() ?? "none",
            error    = latest?.ErrorMessage ?? ""
        });
    }

    // GET /assets/{songId}/download/cards  — Feature 5: human-readable filename
    public async Task<IActionResult> DownloadCards(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        var cardPaths = _storage.GetCardPaths(song.SongId).ToList();
        if (!cardPaths.Any()) return NotFound("No cards generated yet.");

        var prefix = $"{Sanitize(song.Title)}_{Sanitize(song.Artist)}";

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in cardPaths)
                zip.CreateEntryFromFile(path, $"{prefix}_{Path.GetFileName(path)}");
        }
        ms.Seek(0, SeekOrigin.Begin);

        return File(ms, "application/zip", $"{prefix}_cards.zip");
    }

    // GET /assets/{songId}/download/video  — Feature 5: human-readable filename
    public async Task<IActionResult> DownloadVideo(int songId)
    {
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return NotFound();

        var videoPath = _storage.GetVideoPath(song.SongId);
        if (videoPath == null) return NotFound("No video generated yet.");

        var filename = $"{Sanitize(song.Title)}_{Sanitize(song.Artist)}_video.mp4";
        var stream   = System.IO.File.OpenRead(videoPath);
        return File(stream, "video/mp4", filename);
    }

    // Helper: sanitize string for use in filenames
    private static string Sanitize(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result  = string.Join("_", input.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        result      = Regex.Replace(result, @"\s+", "_");
        return result.Length > 40 ? result[..40] : result;
    }
}

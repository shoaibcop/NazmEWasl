using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Data;
using NazmEWasl.Web.Models.Domain;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class PipelineService : IPipelineService
{
    private readonly IStorageService _storage;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PipelineService> _logger;
    private readonly ISettingsService _settings;

    private string PythonExe    => _config["NazmEWasl:PythonExecutable"] ?? "python";
    private string ScriptsPath  => Path.GetFullPath(_config["NazmEWasl:PipelineScriptsPath"] ?? "../../scripts");

    public PipelineService(
        IStorageService storage,
        AppDbContext db,
        IConfiguration config,
        ILogger<PipelineService> logger,
        ISettingsService settings)
    {
        _storage  = storage;
        _db       = db;
        _config   = config;
        _logger   = logger;
        _settings = settings;
    }

    public Task RunImageGenerationAsync(string songId)
    {
        _logger.LogInformation("RunImageGenerationAsync: no image generator configured for {SongId}.", songId);
        return Task.CompletedTask;
    }

    public async Task RunCardRenderingAsync(string songId, CancellationToken ct = default)
    {
        var song = await _db.Songs.FirstAsync(s => s.SongId == songId, ct);
        var verses = await _db.Verses
            .Where(v => v.Song.SongId == songId)
            .OrderBy(v => v.VerseNumber)
            .ToListAsync(ct);

        var versesPayload = new
        {
            title = song.Title,
            artist = song.Artist,
            total_verses = verses.Count,
            verses = verses.Select(v =>
            {
                object? keywords = null;
                if (!string.IsNullOrWhiteSpace(v.KeywordsJson))
                {
                    try { keywords = JsonSerializer.Deserialize<object>(v.KeywordsJson); }
                    catch { /* leave null if malformed */ }
                }
                return (object)new
                {
                    verse_number = v.VerseNumber,
                    persian_text = v.PersianText,
                    roman_urdu   = v.RomanUrdu ?? "",
                    english_text = v.EnglishText,
                    hindi_text   = v.HindiText,
                    start_ms     = v.StartMs,
                    end_ms       = v.EndMs,
                    keywords
                };
            })
        };

        var versesJsonPath = Path.Combine(_storage.GetInputPath(songId), "verses.json");
        await File.WriteAllTextAsync(versesJsonPath,
            JsonSerializer.Serialize(versesPayload, new JsonSerializerOptions { WriteIndented = true }), ct);

        // Write render settings JSON for card_renderer.py
        var renderSettings = new
        {
            persian_font_size   = _settings.Get("Card.PersianFontSize",   "56"),
            roman_font_size     = _settings.Get("Card.RomanUrduFontSize", "34"),
            font_family         = _settings.Get("Card.FontFamily",        "Amiri"),
            overlay_opacity     = _settings.Get("Card.OverlayOpacity",    "0.72")
        };
        var settingsJsonPath = Path.Combine(_storage.GetInputPath(songId), "render_settings.json");
        await File.WriteAllTextAsync(settingsJsonPath,
            JsonSerializer.Serialize(renderSettings, new JsonSerializerOptions { WriteIndented = true }), ct);

        var scriptPath = Path.Combine(ScriptsPath, "card_renderer.py");
        await RunScriptAsync(songId, PipelineStep.CardRendering,
            $"\"{scriptPath}\" --song-id {songId} --settings-json \"{settingsJsonPath}\"", ct);
    }

    public async Task RunVideoRenderingAsync(string songId, CancellationToken ct = default)
    {
        var audioPath = Path.Combine(_storage.GetInputPath(songId), "audio.mp3");
        if (!File.Exists(audioPath))
            audioPath = Path.Combine(_storage.GetInputPath(songId), "audio.wav");

        var fps      = _settings.Get("Video.Fps",              "24");
        var width    = _settings.Get("Video.Width",            "1080");
        var height   = _settings.Get("Video.Height",           "1080");
        var endDur   = _settings.Get("Video.EndCardDurationSec","5");

        var scriptPath = Path.Combine(ScriptsPath, "video_renderer.py");
        await RunScriptAsync(songId, PipelineStep.VideoRendering,
            $"\"{scriptPath}\" --song-id {songId} --audio \"{audioPath}\"" +
            $" --fps {fps} --width {width} --height {height} --end-card-duration {endDur}", ct);

        var song = await _db.Songs.FirstAsync(s => s.SongId == songId, ct);
        if (_storage.GetVideoPath(songId) != null)
            song.Status = SongStatus.Complete;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RunScriptAsync(string songId, PipelineStep step, string args, CancellationToken ct = default)
    {
        var song = await _db.Songs.FirstAsync(s => s.SongId == songId, ct);

        var job = new PipelineJob
        {
            SongId    = song.Id,
            Step      = step,
            Status    = JobStatus.Running,
            StartedAt = DateTime.UtcNow,
            Output    = ""
        };
        _db.PipelineJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        var psi = new ProcessStartInfo(PythonExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start Python process for step {step}.");

            // Kill process on cancellation
            ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* ignore */ }
            });

            var outputLines  = new StringBuilder();
            var lineBuffer   = new List<string>();
            int flushCounter = 0;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                outputLines.AppendLine(e.Data);
                lineBuffer.Add(e.Data);
                flushCounter++;
            };
            process.BeginOutputReadLine();

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            job.Output = outputLines.ToString();

            if (ct.IsCancellationRequested)
            {
                job.Status       = JobStatus.Failed;
                job.ErrorMessage = "Cancelled by user";
                song.Status      = SongStatus.Approved;
            }
            else if (process.ExitCode != 0)
            {
                job.Status       = JobStatus.Failed;
                job.ErrorMessage = string.IsNullOrWhiteSpace(stderr)
                    ? $"Script exited with code {process.ExitCode}"
                    : stderr.Trim();
                _logger.LogError("Script {Step} failed for song {SongId}: {Error}", step, songId, job.ErrorMessage);
            }
            else
            {
                job.Status = JobStatus.Complete;
            }
        }
        catch (OperationCanceledException)
        {
            job.Status       = JobStatus.Failed;
            job.ErrorMessage = "Cancelled by user";
            song.Status      = SongStatus.Approved;
        }
        catch (Exception ex)
        {
            job.Status       = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to start script {Step} for song {SongId}", step, songId);
        }
        finally
        {
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        if (job.Status == JobStatus.Failed && !ct.IsCancellationRequested)
            throw new InvalidOperationException($"Pipeline step {step} failed: {job.ErrorMessage}");
    }
}

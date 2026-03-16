using Microsoft.Extensions.Configuration;
using NazmEWasl.Web.Services.Interfaces;

namespace NazmEWasl.Web.Services;

public class LocalStorageService : IStorageService
{
    private readonly string _songsRoot;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(IConfiguration config, ILogger<LocalStorageService> logger)
    {
        _songsRoot = Path.GetFullPath(config["NazmEWasl:SongsRootPath"] ?? "../../songs");
        _logger = logger;
    }

    public string GetInputPath(string songId) =>
        Path.Combine(_songsRoot, songId, "inputs");

    public string GetOutputPath(string songId, string subfolder) =>
        Path.Combine(_songsRoot, songId, "outputs", subfolder);

    public void InitialiseSongFolders(string songId)
    {
        Directory.CreateDirectory(GetInputPath(songId));
        Directory.CreateDirectory(GetOutputPath(songId, "cards"));
        Directory.CreateDirectory(GetOutputPath(songId, "video"));
    }

    public async Task<string> SaveInputFileAsync(string songId, string filename, Stream content)
    {
        try
        {
            var path = Path.Combine(GetInputPath(songId), filename);
            await using var fs = File.Create(path);
            await content.CopyToAsync(fs);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save input file {Filename} for song {SongId}", filename, songId);
            throw;
        }
    }

    public IEnumerable<string> GetCardPaths(string songId)
    {
        var cardsDir = GetOutputPath(songId, "cards");
        if (!Directory.Exists(cardsDir)) return [];
        return Directory.GetFiles(cardsDir, "verse_*.png").OrderBy(f => f);
    }

    public string? GetVideoPath(string songId)
    {
        var videoDir = GetOutputPath(songId, "video");
        if (!Directory.Exists(videoDir)) return null;
        return Directory.GetFiles(videoDir, "full_video.*").FirstOrDefault();
    }

    public void DeleteOutputs(string songId)
    {
        var outputsRoot = Path.Combine(_songsRoot, songId, "outputs");
        if (Directory.Exists(outputsRoot))
            Directory.Delete(outputsRoot, recursive: true);
        Directory.CreateDirectory(GetOutputPath(songId, "cards"));
        Directory.CreateDirectory(GetOutputPath(songId, "video"));
    }
}

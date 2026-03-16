namespace NazmEWasl.Web.Services.Interfaces;

public interface IStorageService
{
    string GetInputPath(string songId);
    string GetOutputPath(string songId, string subfolder);
    Task<string> SaveInputFileAsync(string songId, string filename, Stream content);
    IEnumerable<string> GetCardPaths(string songId);
    string? GetVideoPath(string songId);
    void InitialiseSongFolders(string songId);
    void DeleteOutputs(string songId);
}

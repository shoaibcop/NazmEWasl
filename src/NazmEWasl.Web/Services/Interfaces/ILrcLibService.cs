namespace NazmEWasl.Web.Services.Interfaces;

public interface ILrcLibService
{
    Task<LrcLibResult?> GetLyricsAsync(string title, string artist);
    List<ParsedVerse> ParseLyrics(LrcLibResult result);
}

public record LrcLibResult(string TrackName, string ArtistName, string? SyncedLyrics, string? PlainLyrics)
{
    public bool HasSyncedLyrics => !string.IsNullOrWhiteSpace(SyncedLyrics);
}

public record ParsedVerse(int VerseNumber, string Text, int StartMs, int EndMs);

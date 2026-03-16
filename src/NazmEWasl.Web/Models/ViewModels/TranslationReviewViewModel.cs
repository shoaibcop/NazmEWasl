using NazmEWasl.Web.Models.Domain;

namespace NazmEWasl.Web.Models.ViewModels;

public class TranslationReviewViewModel
{
    public Song Song { get; set; } = null!;
    public List<Verse> Verses { get; set; } = new();

    public IReadOnlyList<string> ActiveLanguages => Song.ParsedLanguages;
}

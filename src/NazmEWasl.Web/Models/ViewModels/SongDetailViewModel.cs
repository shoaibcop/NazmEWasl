using NazmEWasl.Web.Models.Domain;

namespace NazmEWasl.Web.Models.ViewModels;

public class SongDetailViewModel
{
    public Song Song { get; set; } = null!;
    public PipelineJob? LatestJob { get; set; }
}

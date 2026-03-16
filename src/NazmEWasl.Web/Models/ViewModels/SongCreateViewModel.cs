using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace NazmEWasl.Web.Models.ViewModels;

public class SongCreateViewModel
{
    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Artist { get; set; } = string.Empty;

    public string? Year { get; set; }

    public string? Notes { get; set; }

    [Required(ErrorMessage = "Audio file is required (.mp3 or .wav)")]
    public IFormFile AudioFile { get; set; } = null!;

    [Required(ErrorMessage = "Background photo is required (.jpg or .png)")]
    public IFormFile BackgroundFile { get; set; } = null!;
}

using System.ComponentModel.DataAnnotations;

namespace MusicRecommender.Models;

public class Recommendation
{
    [Key]
    public int Id { get; set; }
    public string SuggestedTrackName { get; set; } = string.Empty;
    public string SuggestedArtist { get; set; } = string.Empty;
    public bool IsMarkedAsFavorite { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
using System.ComponentModel.DataAnnotations;

namespace MusicRecommender.Models;

public class Recommendation
{
    [Key]
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public string FavoriteTrackNumbers { get; set; } = string.Empty;
    public string SuggestedTrackName { get; set; } = string.Empty;
    public string SuggestedArtist { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
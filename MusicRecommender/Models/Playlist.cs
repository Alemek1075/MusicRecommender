using System.ComponentModel.DataAnnotations;

namespace MusicRecommender.Models;

public class Playlist
{
    [Key]
    public int Id { get; set; }
    [Required]
    public string ExternalUrl { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TrackMetadata> Tracks { get; set; } = new List<TrackMetadata>();
}
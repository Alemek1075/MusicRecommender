using System.ComponentModel.DataAnnotations;

namespace MusicRecommender.Models;

public class TrackMetadata
{
    [Key]
    public int Id { get; set; }
    public string TrackName { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public int DurationMs { get; set; }

    public int PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }
}
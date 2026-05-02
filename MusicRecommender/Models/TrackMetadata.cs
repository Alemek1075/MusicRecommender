using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MusicRecommender.Models;

public class TrackMetadata
{
    [Key]
    [JsonIgnore]
    public int Id { get; set; }
    public int TrackNumber { get; set; }
    public string TrackName { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public int DurationMs { get; set; }

    public int PlaylistId { get; set; }
    [JsonIgnore]
    public Playlist? Playlist { get; set; }
}
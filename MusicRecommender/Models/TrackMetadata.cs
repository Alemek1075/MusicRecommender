using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MusicRecommender.Models;

/// <summary>
/// Normalized information for one track inside an imported playlist. The app stores this so the
/// frontend can render track lists and the recommendation engine can pick seed tracks without
/// calling Spotify or YouTube again.
/// </summary>
public class TrackMetadata
{
    /// <summary>
    /// Internal database key. It is hidden from JSON because clients use TrackNumber within a
    /// playlist, not database row identifiers, when selecting favourites.
    /// </summary>
    [Key]
    [JsonIgnore]
    public int Id { get; set; } // Internal row ID hidden from API clients.

    /// <summary>
    /// One-based position of the track in the imported playlist. This is what users select in the
    /// UI and what recommendation requests send back to the backend.
    /// </summary>
    public int TrackNumber { get; set; } // One-based playlist position visible to users.

    /// <summary>
    /// Display title for the track as supplied by Spotify/YouTube or parsed from public embed data.
    /// </summary>
    public string TrackName { get; set; } = string.Empty; // Human-readable song title.

    /// <summary>
    /// Primary artist/channel name used for statistics, genre lookup, and recommendation queries.
    /// </summary>
    public string ArtistName { get; set; } = string.Empty; // Artist/channel used for taste matching.

    /// <summary>
    /// Best-effort genre label from MusicBrainz. It can be null or a fallback value when external
    /// metadata is missing.
    /// </summary>
    public string? Genre { get; set; } // Optional MusicBrainz genre or fallback label.

    /// <summary>
    /// Track duration in milliseconds, used for playlist duration statistics and detail display.
    /// </summary>
    public int DurationMs { get; set; } // Duration stored in API/native millisecond format.

    /// <summary>
    /// Foreign key linking this track to its imported playlist.
    /// </summary>
    public int PlaylistId { get; set; } // Parent playlist foreign key.

    /// <summary>
    /// Navigation property back to the owning playlist. It is ignored in JSON to avoid cycles in
    /// API responses.
    /// </summary>
    [JsonIgnore]
    public Playlist? Playlist { get; set; } // EF navigation back to the parent playlist.
}

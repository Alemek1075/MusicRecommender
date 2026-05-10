using System.ComponentModel.DataAnnotations;

namespace MusicRecommender.Models;

/// <summary>
/// A persisted recommendation result. Each row stores the generated track and the playlist track
/// numbers that were used as favourite seeds so history can explain the recommendation later.
/// </summary>
public class Recommendation
{
    /// <summary>
    /// Internal database identifier used for deleting a single recommendation entry.
    /// </summary>
    [Key]
    public int Id { get; set; } // Primary key for a saved recommendation.

    /// <summary>
    /// Playlist that produced this recommendation.
    /// </summary>
    public int PlaylistId { get; set; } // Playlist whose tracks seeded this suggestion.

    /// <summary>
    /// Comma-separated one-based track numbers selected by the user. An empty string means the
    /// recommendation was generated from the full playlist rather than specific favourites.
    /// </summary>
    public string FavoriteTrackNumbers { get; set; } = string.Empty; // Serialized selected track numbers.

    /// <summary>
    /// Title of the suggested track returned by Spotify or YouTube search.
    /// </summary>
    public string SuggestedTrackName { get; set; } = string.Empty; // Recommended song title.

    /// <summary>
    /// Artist for the suggested track, shown beside the title in recommendations and history.
    /// </summary>
    public string SuggestedArtist { get; set; } = string.Empty; // Recommended song artist.

    /// <summary>
    /// UTC timestamp for when the recommendation was generated.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Generation timestamp for history.
}

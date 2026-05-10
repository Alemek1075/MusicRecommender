using Microsoft.EntityFrameworkCore;
using MusicRecommender.Models;

namespace MusicRecommender.Data;

/// <summary>
/// EF Core database context for the application. It exposes imported playlists, the normalized
/// track metadata extracted from those playlists, and saved recommendation history.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Creates the context with provider/options configured in Program.cs. In production this uses
    /// PostgreSQL through Npgsql; tests or local tooling can supply different DbContextOptions.
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { } // Pass configured provider/options to DbContext.

    /// <summary>
    /// Imported Spotify or YouTube playlists, including original URL, optional display name, and
    /// process timestamp.
    /// </summary>
    public DbSet<Playlist> Playlists { get; set; } = null!; // Table for imported playlist parents; EF initializes it.

    /// <summary>
    /// Per-track metadata stored after import so recommendations and UI views do not need to
    /// refetch the full playlist from external services every time.
    /// </summary>
    public DbSet<TrackMetadata> TrackMetadatas { get; set; } = null!; // Table for imported playlist tracks; EF initializes it.

    /// <summary>
    /// Saved recommendation results. These are kept separately from tracks because they represent
    /// generated suggestions and their favourite-track inputs, not playlist contents.
    /// </summary>
    public DbSet<Recommendation> Recommendations { get; set; } = null!; // Table for generated suggestion history; EF initializes it.
}

using Microsoft.EntityFrameworkCore;
using MusicRecommender.Models;

namespace MusicRecommender.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<TrackMetadata> TrackMetadatas { get; set; }
    public DbSet<Recommendation> Recommendations { get; set; }
}
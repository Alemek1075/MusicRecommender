using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MusicRecommender.Data;
using MusicRecommender.Models;
using MusicRecommender.Services;
using Xunit;

namespace MusicRecommender.Tests;

/// <summary>
/// Unit tests for PlaylistProcessingService using an in-memory EF Core database.
/// Each test gets its own isolated database instance via a unique GUID name.
/// Run with: dotnet test MusicRecommender.Tests
/// </summary>
public class PlaylistProcessingServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PlaylistProcessingService _service;

    public PlaylistProcessingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder().Build();

        // CreateClient is called in the constructor for the MusicBrainz HttpClient.
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        _service = new PlaylistProcessingService(_db, config, mockFactory.Object);
    }

    public void Dispose() => _db.Dispose();

    // ── URL validation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("just some text")]
    [InlineData("ftp://wrong-scheme.com")]
    public async Task ProcessAsync_MalformedUrl_ThrowsArgumentException(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ProcessAsync(url));
    }

    [Theory]
    [InlineData("https://example.com/playlist")]
    [InlineData("https://soundcloud.com/artist/track")]
    [InlineData("https://music.apple.com/playlist/123")]
    public async Task ProcessAsync_UnsupportedPlatform_ThrowsWithSpotifyYouTubeMessage(string url)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.ProcessAsync(url));
        Assert.Contains("Spotify or YouTube", ex.Message);
    }

    // ── Statistics ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsAllZeros()
    {
        var stats = await _service.GetStatisticsAsync(null);

        Assert.Equal(0, stats.TotalTracks);
        Assert.Null(stats.TopGenre);
        Assert.Null(stats.TopArtist);
        Assert.Equal(0, stats.TotalHours);
        Assert.Equal(0, stats.TotalMinutes);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithTracks_ReturnsCorrectTopGenreAndArtist()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc" };
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 1, TrackName = "Song A", ArtistName = "ArtistX", Genre = "Rock", DurationMs = 200_000 });
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 2, TrackName = "Song B", ArtistName = "ArtistX", Genre = "Rock", DurationMs = 180_000 });
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 3, TrackName = "Song C", ArtistName = "ArtistY", Genre = "Pop",  DurationMs = 160_000 });
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var stats = await _service.GetStatisticsAsync(null);

        Assert.Equal(3, stats.TotalTracks);
        Assert.Equal("Rock", stats.TopGenre);
        Assert.Equal("ArtistX", stats.TopArtist);
    }

    [Fact]
    public async Task GetStatisticsAsync_FilteredByPlaylistId_IgnoresOtherPlaylists()
    {
        var p1 = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/p1" };
        p1.Tracks.Add(new TrackMetadata { TrackNumber = 1, TrackName = "A", ArtistName = "ArtistX", Genre = "Rock", DurationMs = 200_000 });

        var p2 = new Playlist { ExternalUrl = "https://www.youtube.com/playlist?list=yt1" };
        p2.Tracks.Add(new TrackMetadata { TrackNumber = 1, TrackName = "B", ArtistName = "ArtistY", Genre = "Pop", DurationMs = 180_000 });
        p2.Tracks.Add(new TrackMetadata { TrackNumber = 2, TrackName = "C", ArtistName = "ArtistY", Genre = "Pop", DurationMs = 160_000 });

        _db.Playlists.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        var stats = await _service.GetStatisticsAsync([p1.Id]);

        Assert.Equal(1, stats.TotalTracks);
        Assert.Equal("ArtistX", stats.TopArtist);
        Assert.Equal("Rock", stats.TopGenre);
    }

    [Fact]
    public async Task GetStatisticsAsync_DurationSumsCorrectly()
    {
        // 2 hours = 2 * 3600 * 1000 ms
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/dur" };
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 1, TrackName = "A", ArtistName = "X", DurationMs = 3_600_000 });
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 2, TrackName = "B", ArtistName = "X", DurationMs = 3_600_000 });
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var stats = await _service.GetStatisticsAsync(null);

        Assert.Equal(2, stats.TotalHours);
        Assert.Equal(0, stats.TotalMinutes);
    }

    // ── Playlists ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlaylistsAsync_ReturnsNewestFirst()
    {
        var older = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/old", ProcessedAt = DateTime.UtcNow.AddDays(-2) };
        var newer = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/new", ProcessedAt = DateTime.UtcNow };
        _db.Playlists.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var playlists = await _service.GetPlaylistsAsync();

        Assert.Equal(newer.Id, playlists[0].Id);
        Assert.Equal(older.Id, playlists[1].Id);
    }

    // ── Tracks ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTracksAsync_NonExistentPlaylist_ReturnsNull()
    {
        var result = await _service.GetTracksAsync(99999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetTracksAsync_ExistingPlaylist_ReturnsTracksSortedByNumber()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc" };
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 2, TrackName = "Song B", ArtistName = "X", DurationMs = 100 });
        playlist.Tracks.Add(new TrackMetadata { TrackNumber = 1, TrackName = "Song A", ArtistName = "X", DurationMs = 100 });
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var tracks = await _service.GetTracksAsync(playlist.Id);

        Assert.NotNull(tracks);
        Assert.Equal(2, tracks.Count);
        Assert.Equal(1, tracks[0].TrackNumber);
        Assert.Equal(2, tracks[1].TrackNumber);
    }

    [Fact]
    public async Task GetTracksAsync_WithTrackNumberFilter_ReturnsOnlyRequestedTracks()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc" };
        for (int i = 1; i <= 5; i++)
            playlist.Tracks.Add(new TrackMetadata { TrackNumber = i, TrackName = $"Track {i}", ArtistName = "X", DurationMs = 100 });
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var tracks = await _service.GetTracksAsync(playlist.Id, [2, 4]);

        Assert.NotNull(tracks);
        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, t => Assert.Contains(t.TrackNumber, new[] { 2, 4 }));
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenamePlaylistAsync_NonExistentPlaylist_ReturnsNull()
    {
        var result = await _service.RenamePlaylistAsync(99999, "New Name");

        Assert.Null(result);
    }

    [Fact]
    public async Task RenamePlaylistAsync_ValidPlaylist_PersistsNewName()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc" };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var result = await _service.RenamePlaylistAsync(playlist.Id, "Summer Vibes");

        Assert.NotNull(result);
        Assert.Equal("Summer Vibes", result.Name);
    }

    [Fact]
    public async Task RenamePlaylistAsync_WhitespaceOnly_ClearsDisplayName()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc", Name = "Old Name" };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var result = await _service.RenamePlaylistAsync(playlist.Id, "   ");

        Assert.NotNull(result);
        Assert.Null(result.Name);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePlaylistAsync_NonExistentPlaylist_ReturnsFalse()
    {
        var result = await _service.DeletePlaylistAsync(99999);

        Assert.False(result);
    }

    [Fact]
    public async Task DeletePlaylistAsync_ExistingPlaylist_ReturnsTrueAndRemovesRow()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc" };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var result = await _service.DeletePlaylistAsync(playlist.Id);

        Assert.True(result);
        Assert.False(await _db.Playlists.AnyAsync(p => p.Id == playlist.Id));
    }

    [Fact]
    public async Task DeleteRecommendationAsync_NonExistentId_ReturnsFalse()
    {
        var result = await _service.DeleteRecommendationAsync(99999);

        Assert.False(result);
    }

    [Fact]
    public async Task DeletePlaylistHistoryAsync_NoHistory_ReturnsZero()
    {
        var playlist = new Playlist { ExternalUrl = "https://open.spotify.com/playlist/abc" };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var count = await _service.DeletePlaylistHistoryAsync(playlist.Id);

        Assert.Equal(0, count);
    }
}

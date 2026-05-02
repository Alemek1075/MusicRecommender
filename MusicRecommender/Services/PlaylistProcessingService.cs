using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Models;
using SpotifyAPI.Web;
using System.Text.RegularExpressions;
using YoutubeExplode;

namespace MusicRecommender.Services;

public record PlaylistStats(string? TopGenre, string? TopArtist, int TotalTracks, int TotalHours, int TotalMinutes);

public record PlaylistProcessingResult(
    Playlist Playlist,
    List<TrackMetadata> Tracks,
    Recommendation Recommendation,
    PlaylistStats Stats);

public class PlaylistProcessingService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    private static readonly (string Name, string Artist, int DurationMs, string Genre)[] MockSpotifyTracks =
    [
        ("Anti-Hero", "Taylor Swift", 200690, "Pop"),
        ("Flowers", "Miley Cyrus", 200455, "Pop"),
        ("As It Was", "Harry Styles", 167303, "Pop"),
        ("Unholy", "Sam Smith", 156943, "Pop"),
        ("Bad Habit", "Steve Lacy", 233872, "Alternative"),
        ("About Damn Time", "Lizzo", 193620, "Pop"),
        ("Levitating", "Dua Lipa", 203064, "Pop"),
        ("Blinding Lights", "The Weeknd", 200667, "Synth-pop"),
        ("Save Your Tears", "The Weeknd", 215627, "Synth-pop"),
        ("Starboy", "The Weeknd", 230453, "R&B"),
        ("Industry Baby", "Lil Nas X", 212000, "Hip-Hop"),
        ("Montero (Call Me By Your Name)", "Lil Nas X", 137417, "Hip-Hop"),
        ("Easy On Me", "Adele", 224983, "Pop"),
        ("Heat Waves", "Glass Animals", 238805, "Indie Pop"),
        ("Stay", "The Kid LAROI", 141805, "Pop"),
        ("Peaches", "Justin Bieber", 198082, "Pop"),
        ("good 4 u", "Olivia Rodrigo", 178147, "Pop-Punk"),
        ("drivers license", "Olivia Rodrigo", 242136, "Pop"),
        ("Shivers", "Ed Sheeran", 207853, "Pop"),
        ("Shape of You", "Ed Sheeran", 233712, "Pop"),
    ];

    private static readonly (string Title, string Artist)[] MockSpotifyRecommendations =
    [
        ("Cruel Summer", "Taylor Swift"),
        ("Golden Hour", "JVKE"),
        ("Calm Down", "Rema"),
        ("I Ain't Worried", "OneRepublic"),
        ("Running Up That Hill", "Kate Bush"),
        ("Escapism", "RAYE"),
        ("Surrender", "Natalie Taylor"),
    ];

    private static readonly Regex TitleSuffixPattern = new(
        @"\s*[\(\[][^\)\]]*[\)\]]" +
        @"|\s*\|\s.*$" +
        @"|\s+[-–]\s*(?:sped[\s\-]?up|slowed(?:\s*\+\s*reverb)?|reverb|official(?:\s+(?:video|audio|lyrics?|music\s+video))?|lyrics?|audio|music\s+video|live|acoustic|karaoke|instrumental|extended|radio\s+edit|remaster(?:ed)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PlaylistProcessingService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private static string NormalizeTitle(string title) =>
        TitleSuffixPattern.Replace(title, "").Trim().ToLowerInvariant();

    private static bool IsDuplicateTitle(string candidate, HashSet<string> existingNormalized)
    {
        var norm = NormalizeTitle(candidate);
        if (norm.Length == 0) return true;
        foreach (var e in existingNormalized)
        {
            if (norm == e) return true;
            if (e.Length >= 5 && norm.Contains(e)) return true;
            if (norm.Length >= 5 && e.Contains(norm)) return true;
        }
        return false;
    }

    public async Task<PlaylistProcessingResult> ProcessAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URL.");

        var host = uri.Host.ToLowerInvariant();

        if (host.Contains("spotify.com"))
            return await ProcessSpotifyAsync(url, uri);

        if (host.Contains("youtube.com") || host.Contains("youtu.be"))
            return await ProcessYouTubeAsync(url, uri);

        throw new ArgumentException("URL must be a Spotify or YouTube playlist link.");
    }

    public async Task<Recommendation> GenerateAsync(int playlistId, List<int> selectedTrackIds)
    {
        var tracks = await _db.TrackMetadatas.Where(t => t.PlaylistId == playlistId).ToListAsync();
        if (tracks.Count == 0)
            throw new InvalidOperationException("Playlist not found or has no tracks.");

        var selectedSet = new HashSet<int>(selectedTrackIds);

        var topArtist = tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Sum(t => selectedSet.Contains(t.Id) ? 1.5 : 1.0))
            .First().Key;

        var topGenre = tracks
            .Where(t => t.Genre != null)
            .GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Sum(t => selectedSet.Contains(t.Id) ? 1.5 : 1.0))
            .Select(g => g.Key)
            .FirstOrDefault();

        var existingNormalized = new HashSet<string>(tracks.Select(t => NormalizeTitle(t.TrackName)));
        var youtube = new YoutubeClient();
        var searchQuery = topGenre != null ? $"{topArtist} {topGenre}" : $"{topArtist} music";
        var recTitle = "No recommendation found";
        var recArtist = topArtist;
        var scanned = 0;

        await foreach (var result in youtube.Search.GetVideosAsync(searchQuery))
        {
            if (++scanned > 100) break;
            var (artist, title) = ParseYouTubeTitle(result.Title, result.Author.ChannelTitle);
            if (!IsDuplicateTitle(title, existingNormalized))
            {
                recTitle = title;
                recArtist = artist;
                break;
            }
        }

        var recommendation = new Recommendation { SuggestedTrackName = recTitle, SuggestedArtist = recArtist };
        _db.Recommendations.Add(recommendation);
        await _db.SaveChangesAsync();
        return recommendation;
    }

    public async Task<PlaylistStats> GetStatisticsAsync(List<int>? playlistIds)
    {
        IQueryable<TrackMetadata> query = _db.TrackMetadatas;
        if (playlistIds != null && playlistIds.Count > 0)
            query = query.Where(t => playlistIds.Contains(t.PlaylistId));

        var tracks = await query.ToListAsync();

        if (tracks.Count == 0)
            return new PlaylistStats(null, null, 0, 0, 0);

        var topGenre = tracks
            .Where(t => t.Genre != null)
            .GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var topArtist = tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var totalDuration = TimeSpan.FromMilliseconds(tracks.Sum(t => (long)t.DurationMs));
        return new PlaylistStats(topGenre, topArtist, tracks.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
    }

    public async Task<List<Playlist>> GetPlaylistsAsync()
    {
        return await _db.Playlists.OrderByDescending(p => p.ProcessedAt).ToListAsync();
    }

    public async Task<List<TrackMetadata>?> GetTracksAsync(int playlistId)
    {
        var exists = await _db.Playlists.AnyAsync(p => p.Id == playlistId);
        if (!exists) return null;
        return await _db.TrackMetadatas.Where(t => t.PlaylistId == playlistId).ToListAsync();
    }

    public async Task<List<Recommendation>> GetHistoryAsync()
    {
        return await _db.Recommendations
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<Recommendation?> MarkFavoriteAsync(int id)
    {
        var recommendation = await _db.Recommendations.FindAsync(id);
        if (recommendation is null)
            return null;

        recommendation.IsMarkedAsFavorite = true;
        await _db.SaveChangesAsync();
        return recommendation;
    }

    private async Task<PlaylistProcessingResult> ProcessYouTubeAsync(string url, Uri uri)
    {
        var queryParams = QueryHelpers.ParseQuery(uri.Query);
        if (!queryParams.TryGetValue("list", out var listValues) || string.IsNullOrWhiteSpace(listValues.FirstOrDefault()))
            throw new ArgumentException("No YouTube playlist ID found in URL. Ensure the URL contains a 'list' parameter.");

        var playlistId = listValues.First()!.Trim();
        var canonicalUrl = $"https://www.youtube.com/playlist?list={playlistId}";
        var youtube = new YoutubeClient();

        var rawVideos = new List<(string Title, string ChannelTitle, TimeSpan Duration)>();
        await foreach (var video in youtube.Playlists.GetVideosAsync(playlistId))
            rawVideos.Add((video.Title, video.Author.ChannelTitle, video.Duration ?? TimeSpan.Zero));

        if (rawVideos.Count == 0)
            throw new InvalidOperationException("The YouTube playlist is empty or could not be accessed.");

        var playlist = new Playlist { ExternalUrl = canonicalUrl };
        var totalDuration = TimeSpan.Zero;

        foreach (var (videoTitle, channelTitle, duration) in rawVideos)
        {
            var (artist, title) = ParseYouTubeTitle(videoTitle, channelTitle);
            playlist.Tracks.Add(new TrackMetadata { TrackName = title, ArtistName = artist, Genre = null, DurationMs = (int)duration.TotalMilliseconds });
            totalDuration += duration;
        }

        var topArtist = playlist.Tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var existingNormalized = new HashSet<string>(playlist.Tracks.Select(t => NormalizeTitle(t.TrackName)));
        var recTitle = "No recommendation found";
        var recArtist = topArtist;
        var checkedYt = 0;

        await foreach (var result in youtube.Search.GetVideosAsync($"{topArtist} music"))
        {
            if (++checkedYt > 100) break;
            var (artist, title) = ParseYouTubeTitle(result.Title, result.Author.ChannelTitle);
            if (!IsDuplicateTitle(title, existingNormalized))
            {
                recTitle = title;
                recArtist = artist;
                break;
            }
        }

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var recommendation = new Recommendation { SuggestedTrackName = recTitle, SuggestedArtist = recArtist };
        _db.Recommendations.Add(recommendation);
        await _db.SaveChangesAsync();

        var trackList = playlist.Tracks.ToList();
        var stats = new PlaylistStats(null, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, trackList, recommendation, stats);
    }

    private async Task<PlaylistProcessingResult> ProcessSpotifyAsync(string url, Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var playlistIdx = Array.IndexOf(segments, "playlist");
        if (playlistIdx < 0 || playlistIdx + 1 >= segments.Length)
            throw new ArgumentException("Invalid Spotify playlist URL.");
        var playlistId = segments[playlistIdx + 1];

        var clientId = _config["Spotify:ClientId"];
        var clientSecret = _config["Spotify:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || clientId == "mock_id")
            return await ProcessSpotifyMockAsync(url);

        var spotifyConfig = SpotifyClientConfig.CreateDefault()
            .WithAuthenticator(new ClientCredentialsAuthenticator(clientId, clientSecret!));
        var spotify = new SpotifyClient(spotifyConfig);

        var spotifyPlaylist = await spotify.Playlists.Get(playlistId);
        if (spotifyPlaylist.Items == null)
            throw new InvalidOperationException("Could not retrieve tracks from the Spotify playlist.");

        var rawTracks = new List<(string Name, string Artist, int DurationMs)>();
        await foreach (var item in spotify.Paginate(spotifyPlaylist.Items))
        {
            if (item.Track is FullTrack fullTrack)
            {
                rawTracks.Add((
                    fullTrack.Name,
                    fullTrack.Artists.FirstOrDefault()?.Name ?? "Unknown",
                    fullTrack.DurationMs
                ));
            }
        }

        if (rawTracks.Count == 0)
            throw new InvalidOperationException("The Spotify playlist is empty or has no audio tracks.");

        var playlist = new Playlist { ExternalUrl = url };
        var totalDurationMs = 0L;

        foreach (var (name, artist, durationMs) in rawTracks)
        {
            playlist.Tracks.Add(new TrackMetadata { TrackName = name, ArtistName = artist, Genre = null, DurationMs = durationMs });
            totalDurationMs += durationMs;
        }

        var totalDuration = TimeSpan.FromMilliseconds(totalDurationMs);
        var topArtist = playlist.Tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var existingNormalized = new HashSet<string>(playlist.Tracks.Select(t => NormalizeTitle(t.TrackName)));
        var recTitle = "No recommendation found";
        var recArtist = topArtist;

        try
        {
            var searchResult = await spotify.Search.Item(
                new SearchRequest(SearchRequest.Types.Artist, topArtist) { Limit = 1 });
            var artistId = searchResult.Artists?.Items?.FirstOrDefault()?.Id;

            if (artistId != null)
            {
                var recRequest = new RecommendationsRequest { Limit = 20 };
                recRequest.SeedArtists.Add(artistId);
                var recResponse = await spotify.Browse.GetRecommendations(recRequest);
                var pick = recResponse.Tracks.FirstOrDefault(t => !IsDuplicateTitle(t.Name, existingNormalized));
                if (pick != null)
                {
                    recTitle = pick.Name;
                    recArtist = pick.Artists.FirstOrDefault()?.Name ?? topArtist;
                }
            }
        }
        catch (APIException) { }

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var recommendation = new Recommendation { SuggestedTrackName = recTitle, SuggestedArtist = recArtist };
        _db.Recommendations.Add(recommendation);
        await _db.SaveChangesAsync();

        var trackList = playlist.Tracks.ToList();
        var stats = new PlaylistStats(null, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, trackList, recommendation, stats);
    }

    private async Task<PlaylistProcessingResult> ProcessSpotifyMockAsync(string url)
    {
        var playlist = new Playlist { ExternalUrl = url };
        var totalDurationMs = 0L;

        foreach (var (name, artist, durationMs, genre) in MockSpotifyTracks)
        {
            playlist.Tracks.Add(new TrackMetadata { TrackName = name, ArtistName = artist, Genre = genre, DurationMs = durationMs });
            totalDurationMs += durationMs;
        }

        var totalDuration = TimeSpan.FromMilliseconds(totalDurationMs);
        var topArtist = playlist.Tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var existingNormalized = new HashSet<string>(playlist.Tracks.Select(t => NormalizeTitle(t.TrackName)));
        var pick = MockSpotifyRecommendations.FirstOrDefault(r => !IsDuplicateTitle(r.Title, existingNormalized));
        var recTitle = pick.Title ?? "Cruel Summer";
        var recArtist = pick.Artist ?? "Taylor Swift";

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var recommendation = new Recommendation { SuggestedTrackName = recTitle, SuggestedArtist = recArtist };
        _db.Recommendations.Add(recommendation);
        await _db.SaveChangesAsync();

        var trackList = playlist.Tracks.ToList();
        var topGenre = trackList
            .Where(t => t.Genre != null)
            .GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var stats = new PlaylistStats(topGenre, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, trackList, recommendation, stats);
    }

    private static (string Artist, string Title) ParseYouTubeTitle(string videoTitle, string channelTitle)
    {
        var artist = channelTitle.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)
            ? channelTitle[..^" - Topic".Length].Trim()
            : channelTitle;

        var idx = videoTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
            return (videoTitle[..idx].Trim(), videoTitle[(idx + 3)..].Trim());

        return (artist, videoTitle.Trim());
    }
}

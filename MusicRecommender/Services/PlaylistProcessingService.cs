using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Models;
using SpotifyAPI.Web;
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

    public PlaylistProcessingService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
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

    public async Task<Recommendation> GenerateAsync()
    {
        var tracks = await _db.TrackMetadatas.ToListAsync();
        if (tracks.Count == 0)
            throw new InvalidOperationException("No tracks in the database. Submit a playlist first.");

        var topArtist = tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var existingTitles = new HashSet<string>(tracks.Select(t => t.TrackName), StringComparer.OrdinalIgnoreCase);
        var youtube = new YoutubeClient();

        var recTitle = "No recommendation found";
        var recArtist = topArtist;

        await foreach (var result in youtube.Search.GetVideosAsync($"{topArtist} music"))
        {
            var (artist, title) = ParseYouTubeTitle(result.Title, result.Author.ChannelTitle);
            if (!existingTitles.Contains(title))
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

    public async Task<PlaylistStats> GetStatisticsAsync()
    {
        var tracks = await _db.TrackMetadatas.ToListAsync();

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

        return new PlaylistStats(topGenre, topArtist, tracks.Count, 0, 0);
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

        var playlistId = listValues.First()!;
        var youtube = new YoutubeClient();

        var rawVideos = new List<(string Title, string ChannelTitle, TimeSpan Duration)>();
        await foreach (var video in youtube.Playlists.GetVideosAsync(playlistId))
            rawVideos.Add((video.Title, video.Author.ChannelTitle, video.Duration ?? TimeSpan.Zero));

        if (rawVideos.Count == 0)
            throw new InvalidOperationException("The YouTube playlist is empty or could not be accessed.");

        var playlist = new Playlist { ExternalUrl = url };
        var totalDuration = TimeSpan.Zero;

        foreach (var (videoTitle, channelTitle, duration) in rawVideos)
        {
            var (artist, title) = ParseYouTubeTitle(videoTitle, channelTitle);
            playlist.Tracks.Add(new TrackMetadata { TrackName = title, ArtistName = artist, Genre = null });
            totalDuration += duration;
        }

        var topArtist = playlist.Tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var existingTitles = new HashSet<string>(playlist.Tracks.Select(t => t.TrackName), StringComparer.OrdinalIgnoreCase);
        var recTitle = "No recommendation found";
        var recArtist = topArtist;

        await foreach (var result in youtube.Search.GetVideosAsync($"{topArtist} music"))
        {
            var (artist, title) = ParseYouTubeTitle(result.Title, result.Author.ChannelTitle);
            if (!existingTitles.Contains(title))
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
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Spotify:ClientId and Spotify:ClientSecret must be set in appsettings.json.");

        var spotifyConfig = SpotifyClientConfig.CreateDefault()
            .WithAuthenticator(new ClientCredentialsAuthenticator(clientId, clientSecret));
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
            playlist.Tracks.Add(new TrackMetadata { TrackName = name, ArtistName = artist, Genre = null });
            totalDurationMs += durationMs;
        }

        var totalDuration = TimeSpan.FromMilliseconds(totalDurationMs);
        var topArtist = playlist.Tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var existingTitles = new HashSet<string>(playlist.Tracks.Select(t => t.TrackName), StringComparer.OrdinalIgnoreCase);
        var recTitle = "No recommendation found";
        var recArtist = topArtist;

        try
        {
            var searchResult = await spotify.Search.Item(
                new SearchRequest(SearchRequest.Types.Artist, topArtist) { Limit = 1 });
            var artistId = searchResult.Artists?.Items?.FirstOrDefault()?.Id;

            if (artistId != null)
            {
                var recRequest = new RecommendationsRequest { Limit = 10 };
                recRequest.SeedArtists.Add(artistId);
                var recResponse = await spotify.Browse.GetRecommendations(recRequest);
                var pick = recResponse.Tracks.FirstOrDefault(t => !existingTitles.Contains(t.Name));
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

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Models;
using SpotifyAPI.Web;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeExplode;

namespace MusicRecommender.Services;

public record PlaylistStats(string? TopGenre, string? TopArtist, int TotalTracks, int TotalHours, int TotalMinutes);
public record PlaylistProcessingResult(Playlist Playlist, PlaylistStats Stats);
public record SuggestionEntry(int Id, List<int> FavoriteTrackNumbers, string SuggestedTrackName, string SuggestedArtist, DateTime CreatedAt);
public record PlaylistHistoryEntry(int PlaylistId, string PlaylistUrl, List<SuggestionEntry> Suggestions);

public class PlaylistProcessingService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpFactory;

    // Prepositions and social-media words that never appear in real genre names
    private static readonly HashSet<string> _tagStopWords = new(StringComparer.OrdinalIgnoreCase)
    { "by", "for", "from", "tiktok", "viral", "twitter", "instagram", "youtube", "tribute", "parody" };

    private static bool IsLikelyMusicGenre(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 45) return false;
        // Reject pure year/decade strings like "90s", "2020"
        if (tag.All(c => char.IsDigit(c) || c == 's')) return false;
        var words = tag.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        return !words.Any(w => _tagStopWords.Contains(w));
    }

    private static readonly Regex TitleSuffixPattern = new(
        @"\s*[\(\[][^\)\]]*[\)\]]" +
        @"|\s*\|\s.*$" +
        @"|\s+[-–]\s*(?:sped[\s\-]?up|slowed(?:\s*\+\s*reverb)?|reverb|official(?:\s+(?:video|audio|lyrics?|music\s+video))?|lyrics?|audio|music\s+video|live|acoustic|karaoke|instrumental|extended|radio\s+edit|remaster(?:ed)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PlaylistProcessingService(AppDbContext db, IConfiguration config, IHttpClientFactory httpFactory)
    {
        _db = db;
        _config = config;
        _http = httpFactory.CreateClient("musicbrainz");
        _httpFactory = httpFactory;
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

    private async Task<string?> LookupGenreAsync(string artistName)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artistName);
            using var response = await _http.GetAsync($"https://musicbrainz.org/ws/2/artist/?query={encoded}&fmt=json&limit=1");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("artists", out var artists) || artists.GetArrayLength() == 0) return null;
            var artist = artists[0];

            // Prefer the official curated genres array (not available on all artists)
            if (artist.TryGetProperty("genres", out var genres) && genres.GetArrayLength() > 0)
            {
                var topGenre = genres.EnumerateArray()
                    .OrderByDescending(g => g.TryGetProperty("count", out var c) ? c.GetInt32() : 0)
                    .Select(g => g.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .FirstOrDefault(g => g != null);
                if (topGenre != null)
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(topGenre);
            }

            // Fall back to community tags, filtered to plausible genre strings
            if (!artist.TryGetProperty("tags", out var tags) || tags.GetArrayLength() == 0) return null;
            var topTag = tags.EnumerateArray()
                .OrderByDescending(t => t.TryGetProperty("count", out var c) ? c.GetInt32() : 0)
                .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
                .FirstOrDefault(t => t != null && IsLikelyMusicGenre(t));
            return topTag != null ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(topTag) : null;
        }
        catch { return null; }
    }

    private async Task<Dictionary<string, string>> LookupGenresAsync(List<string> artists)
    {
        var raw = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < artists.Count; i++)
        {
            raw[artists[i]] = await LookupGenreAsync(artists[i]);
            if (i < artists.Count - 1)
                await Task.Delay(1100);
        }
        var fallback = raw.Values
            .Where(v => v != null)
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "Unknown";
        return raw.ToDictionary(kv => kv.Key, kv => kv.Value ?? fallback, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PlaylistProcessingResult> ProcessAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URL.");
        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("spotify.com")) return await ProcessSpotifyAsync(url, uri);
        if (host.Contains("youtube.com") || host.Contains("youtu.be")) return await ProcessYouTubeAsync(url, uri);
        throw new ArgumentException("URL must be a Spotify or YouTube playlist link.");
    }

    public async Task<Recommendation> GenerateAsync(int playlistId, List<int> selectedTrackNumbers)
    {
        // Deduplicate: same track ID submitted twice counts once
        selectedTrackNumbers = selectedTrackNumbers.Distinct().ToList();

        var tracks = await _db.TrackMetadatas
            .Where(t => t.PlaylistId == playlistId)
            .OrderBy(t => t.TrackNumber)
            .ToListAsync();
        if (tracks.Count == 0)
            throw new InvalidOperationException("Playlist not found or has no tracks.");

        if (selectedTrackNumbers.Count > 0)
        {
            var validNumbers = new HashSet<int>(tracks.Select(t => t.TrackNumber));
            var invalid = selectedTrackNumbers.Where(n => !validNumbers.Contains(n)).ToList();
            if (invalid.Count > 0)
                throw new ArgumentException($"Track numbers not found in playlist: {string.Join(", ", invalid)}");
        }

        var selectedSet = new HashSet<int>(selectedTrackNumbers);
        int F = selectedSet.Count;
        int N = tracks.Count;

        // ── FAVORITE WEIGHT ────────────────────────────────────────────────────
        // Collective share target: S = 0.35 + 0.65*(F/N)
        //   F=1  of 100 → S≈35.7%  W_f≈54.8  (1 favorite dominates strongly)
        //   F=10 of 100 → S=41.5%  W_f≈6.4   (10 favorites dominate together)
        //   F=50 of 100 → S=67.5%  W_f≈2.1   (majority of pull is from favorites)
        //   F=N         → W_f=1.0             (all-favorites edge case, avoids 0/0)
        // W_f is always ≥ 1.0; total favorite votes F×W_f strictly grows with F.
        double favoriteWeight = 1.0;
        if (F > 0 && F < N)
        {
            double S = 0.35 + 0.65 * ((double)F / N);
            favoriteWeight = S * (N - F) / (F * (1.0 - S));
        }

        // ── SEED TRACK (weighted-random pick) ──────────────────────────────────
        // Each favorite is favoriteWeight× more likely to be drawn than a non-favorite.
        // The recommendation is then built around this specific seed's artist + genre,
        // so a picked favorite directly and personally shapes what gets suggested.
        var rng = new Random();
        double totalPool = tracks.Sum(t => selectedSet.Contains(t.TrackNumber) ? favoriteWeight : 1.0);
        double roll = rng.NextDouble() * totalPool;
        var seedTrack = tracks[^1];
        foreach (var t in tracks)
        {
            roll -= selectedSet.Contains(t.TrackNumber) ? favoriteWeight : 1.0;
            if (roll <= 0) { seedTrack = t; break; }
        }
        bool seedIsFavorite = selectedSet.Contains(seedTrack.TrackNumber);

        // ── DEDUP SETS ─────────────────────────────────────────────────────────
        var pastRecs = await _db.Recommendations
            .Where(r => r.PlaylistId == playlistId)
            .Select(r => r.SuggestedTrackName)
            .ToListAsync();

        var existingNormalized = new HashSet<string>(tracks.Select(t => NormalizeTitle(t.TrackName)));
        foreach (var past in pastRecs)
            existingNormalized.Add(NormalizeTitle(past));

        // ── SEARCH QUERY ───────────────────────────────────────────────────────
        // Favorite seed  → genre + artist  e.g. "Witch House Sidewalks and Skeletons"
        //                   (precise, personal — YouTube finds that artist's scene)
        // Non-fav seed   → "best {genre} music"  (broad discovery mode)
        // No genre       → artist-similar fallback
        string searchQuery = seedIsFavorite
            ? (seedTrack.Genre != null
                ? $"{seedTrack.Genre} {seedTrack.ArtistName}"
                : $"{seedTrack.ArtistName} similar songs")
            : (seedTrack.Genre != null
                ? $"best {seedTrack.Genre} music"
                : "popular music");

        var youtube = new YoutubeClient();
        var scanned = 0;

        // Collect up to 5 valid candidates then pick the most-viewed one.
        // YouTube search is already popularity-ranked, but fetching view counts
        // lets us break ties and surface genuinely popular tracks over niche uploads.
        var candidates = new List<(string Title, string Artist, string VideoId)>();

        await foreach (var result in youtube.Search.GetVideosAsync(searchQuery))
        {
            if (++scanned > 150) break;
            if (result.Duration == null || result.Duration.Value.TotalMinutes > 15) continue;
            var titleLower = result.Title.ToLowerInvariant();
            if (titleLower.Contains("playlist") || titleLower.Contains("compilation") ||
                titleLower.Contains("reaction") || titleLower.Contains(" | ")) continue;
            var (artist, title) = ParseYouTubeTitle(result.Title, result.Author.ChannelTitle);
            var cleanTitle = TitleSuffixPattern.Replace(title, "").Trim();
            if (string.IsNullOrWhiteSpace(cleanTitle)) continue;
            if (!IsDuplicateTitle(cleanTitle, existingNormalized))
            {
                candidates.Add((cleanTitle, artist, result.Id));
                if (candidates.Count >= 5) break;
            }
        }

        var recTitle = "No recommendation found";
        var recArtist = seedTrack.ArtistName;

        if (candidates.Count > 0)
        {
            var viewCounts = await Task.WhenAll(candidates.Select(async c =>
            {
                try { return (await youtube.Videos.GetAsync(c.VideoId)).Engagement.ViewCount; }
                catch { return 0L; }
            }));
            var best = candidates
                .Select((c, i) => (c, views: viewCounts[i]))
                .OrderByDescending(x => x.views)
                .First();
            recTitle = best.c.Title;
            recArtist = best.c.Artist;
        }

        var recommendation = new Recommendation
        {
            PlaylistId = playlistId,
            FavoriteTrackNumbers = string.Join(",", selectedTrackNumbers),
            SuggestedTrackName = recTitle,
            SuggestedArtist = recArtist
        };
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
        if (tracks.Count == 0) return new PlaylistStats(null, null, 0, 0, 0);
        var topGenre = tracks.Where(t => t.Genre != null).GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var topArtist = tracks.GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var totalDuration = TimeSpan.FromMilliseconds(tracks.Sum(t => (long)t.DurationMs));
        return new PlaylistStats(topGenre, topArtist, tracks.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
    }

    public async Task<List<Playlist>> GetPlaylistsAsync() =>
        await _db.Playlists.OrderByDescending(p => p.ProcessedAt).ToListAsync();

    public async Task<List<TrackMetadata>?> GetTracksAsync(int playlistId, int? trackNumber = null)
    {
        var exists = await _db.Playlists.AnyAsync(p => p.Id == playlistId);
        if (!exists) return null;
        var query = _db.TrackMetadatas.Where(t => t.PlaylistId == playlistId);
        if (trackNumber.HasValue)
            query = query.Where(t => t.TrackNumber == trackNumber.Value);
        return await query.OrderBy(t => t.TrackNumber).ToListAsync();
    }

    public async Task<List<PlaylistHistoryEntry>> GetHistoryAsync()
    {
        var recommendations = await _db.Recommendations
            .OrderBy(r => r.PlaylistId).ThenBy(r => r.CreatedAt)
            .ToListAsync();
        var playlistIds = recommendations.Select(r => r.PlaylistId).Distinct().ToList();
        var playlists = await _db.Playlists
            .Where(p => playlistIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        return recommendations
            .GroupBy(r => r.PlaylistId)
            .Select(g => new PlaylistHistoryEntry(
                g.Key,
                playlists.TryGetValue(g.Key, out var p) ? p.ExternalUrl : "unknown",
                g.Select(r => new SuggestionEntry(
                    r.Id,
                    r.FavoriteTrackNumbers
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out var n) ? n : 0)
                        .Where(n => n > 0)
                        .ToList(),
                    r.SuggestedTrackName,
                    r.SuggestedArtist,
                    r.CreatedAt
                )).ToList()
            ))
            .ToList();
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

        var allUniqueArtists = rawVideos
            .Select(v => ParseYouTubeTitle(v.Title, v.ChannelTitle).Artist)
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        var topArtist = allUniqueArtists.First();
        var artistGenres = await LookupGenresAsync(allUniqueArtists);
        var fallbackGenre = artistGenres.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

        var playlist = new Playlist { ExternalUrl = canonicalUrl };
        var totalDuration = TimeSpan.Zero;
        var trackNum = 1;

        foreach (var (videoTitle, channelTitle, duration) in rawVideos)
        {
            var (artist, title) = ParseYouTubeTitle(videoTitle, channelTitle);
            playlist.Tracks.Add(new TrackMetadata
            {
                TrackNumber = trackNum++,
                TrackName = title,
                ArtistName = artist,
                Genre = artistGenres.TryGetValue(artist, out var genre) ? genre : fallbackGenre,
                DurationMs = (int)duration.TotalMilliseconds
            });
            totalDuration += duration;
        }

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var topGenre = artistGenres.TryGetValue(topArtist, out var tg) ? tg : fallbackGenre;
        var trackList = playlist.Tracks.ToList();
        var stats = new PlaylistStats(topGenre, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, stats);
    }

    private async Task<string?> GetSpotifyAnonymousTokenAsync()
    {
        try
        {
            using var client = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://open.spotify.com/get_access_token?reason=transport&productType=web_player");
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
        }
        catch { return null; }
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
        bool hasCredentials = !string.IsNullOrWhiteSpace(clientId) && clientId != "mock_id"
                           && !string.IsNullOrWhiteSpace(clientSecret) && clientSecret != "mock_secret";

        SpotifyClient spotify;
        if (hasCredentials)
        {
            var cfg = SpotifyClientConfig.CreateDefault()
                .WithAuthenticator(new ClientCredentialsAuthenticator(clientId!, clientSecret!));
            spotify = new SpotifyClient(cfg);
        }
        else
        {
            var anonToken = await GetSpotifyAnonymousTokenAsync();
            if (string.IsNullOrWhiteSpace(anonToken))
                throw new InvalidOperationException("Could not connect to Spotify. Set Spotify:ClientId and Spotify:ClientSecret in appsettings.json for reliable access.");
            spotify = new SpotifyClient(SpotifyClientConfig.CreateDefault().WithToken(anonToken));
        }

        var spotifyPlaylist = await spotify.Playlists.Get(playlistId);
        if (spotifyPlaylist.Items == null)
            throw new InvalidOperationException("Could not retrieve tracks from the Spotify playlist.");

        var rawTracks = new List<(string Name, string Artist, int DurationMs)>();
        await foreach (var item in spotify.Paginate(spotifyPlaylist.Items))
        {
            if (item.Track is FullTrack fullTrack)
                rawTracks.Add((fullTrack.Name, fullTrack.Artists.FirstOrDefault()?.Name ?? "Unknown", fullTrack.DurationMs));
        }

        if (rawTracks.Count == 0)
            throw new InvalidOperationException("The Spotify playlist is empty or has no audio tracks.");

        var allUniqueArtists = rawTracks
            .GroupBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        var topArtist = allUniqueArtists.First();
        var artistGenres = await LookupGenresAsync(allUniqueArtists);
        var fallbackGenre = artistGenres.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

        var playlist = new Playlist { ExternalUrl = url };
        var totalDurationMs = 0L;
        var trackNum = 1;

        foreach (var (name, artist, durationMs) in rawTracks)
        {
            playlist.Tracks.Add(new TrackMetadata
            {
                TrackNumber = trackNum++,
                TrackName = name,
                ArtistName = artist,
                Genre = artistGenres.TryGetValue(artist, out var genre) ? genre : fallbackGenre,
                DurationMs = durationMs
            });
            totalDurationMs += durationMs;
        }

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var totalDuration = TimeSpan.FromMilliseconds(totalDurationMs);
        var topGenre = artistGenres.TryGetValue(topArtist, out var tg) ? tg : fallbackGenre;
        var trackList = playlist.Tracks.ToList();
        var stats = new PlaylistStats(topGenre, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, stats);
    }

    private static (string Artist, string Title) ParseYouTubeTitle(string videoTitle, string channelTitle)
    {
        // Topic channels: title is the song name, channel minus " - Topic" is the artist
        if (channelTitle.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
            return (channelTitle[..^" - Topic".Length].Trim(), videoTitle.Trim());

        // "Channel - Song Title" format (artist uploads own song)
        var ownPrefix = channelTitle + " - ";
        if (videoTitle.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase))
            return (channelTitle, videoTitle[ownPrefix.Length..].Trim());

        // "Song Title - Channel" format (common for official music videos)
        var channelSuffix = " - " + channelTitle;
        var suffixIdx = videoTitle.IndexOf(channelSuffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIdx > 0)
            return (channelTitle, videoTitle[..suffixIdx].Trim());

        // Default: channel is artist, full video title is the song
        return (channelTitle, videoTitle.Trim());
    }
}

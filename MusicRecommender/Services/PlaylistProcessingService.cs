using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Models;
using SpotifyAPI.Web;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeExplode;

namespace MusicRecommender.Services;

/// <summary>
/// Compact statistics shown on dashboard and playlist detail cards.
/// </summary>
public record PlaylistStats(string? TopGenre, string? TopArtist, int TotalTracks, int TotalHours, int TotalMinutes);

/// <summary>
/// Result returned after importing a playlist: the saved playlist entity plus calculated summary
/// statistics for the imported tracks.
/// </summary>
public record PlaylistProcessingResult(Playlist Playlist, PlaylistStats Stats);

/// <summary>
/// History item for one generated recommendation. Favourite track numbers are expanded into names
/// so the frontend can show understandable history without extra calls for the common case.
/// </summary>
public record SuggestionEntry(int Id, List<int> FavoriteTrackNumbers, List<string> FavoriteTrackNames, string SuggestedTrackName, string SuggestedArtist, DateTime CreatedAt);

/// <summary>
/// Recommendation history grouped by playlist. This mirrors the History page layout.
/// </summary>
public record PlaylistHistoryEntry(int PlaylistId, string PlaylistUrl, string? PlaylistName, List<SuggestionEntry> Suggestions);

/// <summary>
/// Main application service. It owns playlist import, metadata enrichment, recommendation
/// generation, statistics, renaming/deletion, and recommendation history so controllers can remain
/// thin API adapters.
/// </summary>
public class PlaylistProcessingService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpFactory;

    // Spotify can be accessed with configured app credentials or a public web-player token. The
    // boolean flags whether the Web API track endpoint is expected to be reliable for that token.
    private sealed record SpotifyAuth(SpotifyClient Client, bool CanUseWebApiTrackEndpoint);

    // Lightweight track shape used while importing Spotify playlists before converting rows into
    // TrackMetadata entities.
    private sealed record SpotifyRawTrack(string Name, string Artist, int DurationMs);

    // Candidate used by weighted recommendation picking. Weight is derived from popularity/views.
    private sealed record TrackCandidate(string Title, string Artist, double Weight);

    // Prepositions and social-media words that never appear in real genre names
    private static readonly HashSet<string> _tagStopWords = new(StringComparer.OrdinalIgnoreCase)
    { "by", "for", "from", "tiktok", "viral", "twitter", "instagram", "youtube", "tribute", "parody" };

    private static readonly Regex SpotifyTrackReferencePattern = new(
        @"(?:open\.spotify\.com/track/|spotify:track:)([A-Za-z0-9]{22})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Filters MusicBrainz community tags down to plausible genre labels. MusicBrainz tags can
    /// contain platform names, jokes, tribute labels, and other non-genre text, so this protects
    /// the UI and query builder from noisy metadata.
    /// </summary>
    private static bool IsLikelyMusicGenre(string tag)
    {
        // Ignore blank or suspiciously long tags because they are rarely useful genre names.
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 45) return false;

        // Reject pure year/decade strings like "90s", "2020"
        if (tag.All(c => char.IsDigit(c) || c == 's')) return false;

        // Split compound tags so every word can be checked against the stop-word list.
        var words = tag.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);

        // A tag is genre-like only when none of its words are known non-genre markers.
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

    /// <summary>
    /// Normalizes a track title for duplicate checks by removing common video/audio suffixes and
    /// comparing lowercase text.
    /// </summary>
    private static string NormalizeTitle(string title) =>
        TitleSuffixPattern.Replace(title, "").Trim().ToLowerInvariant();

    /// <summary>
    /// Checks whether a candidate title is already in the playlist or recommendation history. It
    /// uses exact and containment checks so remasters or official-video suffixes do not slip
    /// through as "new" recommendations.
    /// </summary>
    private static bool IsDuplicateTitle(string candidate, HashSet<string> existingNormalized)
    {
        // Normalize the candidate the same way imported/past titles were normalized.
        var norm = NormalizeTitle(candidate);

        // Empty normalized titles are not safe to recommend.
        if (norm.Length == 0) return true;

        // Compare against every known playlist/history title.
        foreach (var e in existingNormalized)
        {
            // Exact normalized match is always a duplicate.
            if (norm == e) return true;

            // Containment catches "Song - Remastered" versus "Song" style near-duplicates.
            if (e.Length >= 5 && norm.Contains(e)) return true;
            if (norm.Length >= 5 && e.Contains(norm)) return true;
        }

        // No duplicate signal found.
        return false;
    }

    /// <summary>
    /// Looks up a best-effort genre for an artist from MusicBrainz. Official genres are preferred;
    /// filtered tags are used as a fallback; failures return null because genre enrichment should
    /// not block playlist import.
    /// </summary>
    private async Task<string?> LookupGenreAsync(string artistName)
    {
        try
        {
            // URL-encode artist names before placing them into the MusicBrainz query string.
            var encoded = Uri.EscapeDataString(artistName);

            // Limit to the top match because genre lookup is best-effort enrichment, not search UI.
            using var response = await _http.GetAsync($"https://musicbrainz.org/ws/2/artist/?query={encoded}&fmt=json&limit=1");

            // Treat any MusicBrainz failure as missing metadata so imports can continue.
            if (!response.IsSuccessStatusCode) return null;

            // Parse the JSON response into a disposable document.
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            // No artist match means no genre can be inferred.
            if (!root.TryGetProperty("artists", out var artists) || artists.GetArrayLength() == 0) return null;
            var artist = artists[0];

            // Prefer the official curated genres array (not available on all artists)
            if (artist.TryGetProperty("genres", out var genres) && genres.GetArrayLength() > 0)
            {
                var topGenre = genres.EnumerateArray()
                    // Pick the most counted official genre when counts are present.
                    .OrderByDescending(g => g.TryGetProperty("count", out var c) ? c.GetInt32() : 0)
                    // Extract the genre name from each JSON object.
                    .Select(g => g.TryGetProperty("name", out var n) ? n.GetString() : null)
                    // Use the first non-null genre after sorting.
                    .FirstOrDefault(g => g != null);
                if (topGenre != null)
                    // Normalize genre casing for display consistency.
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(topGenre);
            }

            // Fall back to community tags, filtered to plausible genre strings
            if (!artist.TryGetProperty("tags", out var tags) || tags.GetArrayLength() == 0) return null;
            var topTag = tags.EnumerateArray()
                // Prefer heavily used tags.
                .OrderByDescending(t => t.TryGetProperty("count", out var c) ? c.GetInt32() : 0)
                // Pull the tag name out of each tag object.
                .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
                // Keep only tags that pass the genre-noise filter.
                .FirstOrDefault(t => t != null && IsLikelyMusicGenre(t));

            // Return a display-cased tag or null if no good tag was found.
            return topTag != null ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(topTag) : null;
        }
        // MusicBrainz is optional enrichment; network/JSON errors should not fail playlist import.
        catch { return null; }
    }

    /// <summary>
    /// Looks up genres for a list of artists while respecting MusicBrainz rate limits. Any missing
    /// values receive the most common resolved genre, or "Unknown" if no lookup succeeds.
    /// </summary>
    private async Task<Dictionary<string, string>> LookupGenresAsync(List<string> artists)
    {
        // Preserve nulls while looking up so a fallback can be chosen after all attempts complete.
        var raw = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // MusicBrainz asks clients to be polite, so lookup sequentially with a delay.
        for (int i = 0; i < artists.Count; i++)
        {
            // Store whatever the single-artist lookup returns, including null.
            raw[artists[i]] = await LookupGenreAsync(artists[i]);

            // Delay between calls except after the last artist.
            if (i < artists.Count - 1)
                await Task.Delay(1100);
        }

        // Choose the most common resolved genre as the fallback for unresolved artists.
        var fallback = raw.Values
            .Where(v => v != null)
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "Unknown";

        // Produce a non-null genre dictionary for simpler import/statistics code downstream.
        return raw.ToDictionary(kv => kv.Key, kv => kv.Value ?? fallback, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Imports a playlist from a URL. The host decides whether Spotify or YouTube parsing is used,
    /// and invalid/non-playlist URLs are rejected before any external calls are made.
    /// </summary>
    public async Task<PlaylistProcessingResult> ProcessAsync(string url)
    {
        // Reject malformed URLs before examining hosts or making external requests.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URL.");

        // Host comparison is case-insensitive and tolerant of subdomains.
        var host = uri.Host.ToLowerInvariant();

        // Route Spotify links to Spotify-specific import logic.
        if (host.Contains("spotify.com")) return await ProcessSpotifyAsync(url, uri);

        // Route YouTube links, including youtu.be short URLs, to YouTube import logic.
        if (host.Contains("youtube.com") || host.Contains("youtu.be")) return await ProcessYouTubeAsync(url, uri);

        // Anything else is unsupported by this app.
        throw new ArgumentException("URL must be a Spotify or YouTube playlist link.");
    }

    /// <summary>
    /// Generates one or more recommendations for a saved playlist. Selected track numbers are used
    /// as favourite seed tracks; without selections, the method samples the whole playlist. Results
    /// are deduplicated against existing playlist tracks and past recommendations before being
    /// saved.
    /// </summary>
    public async Task<List<Recommendation>> GenerateAsync(int playlistId, List<int> selectedTrackNumbers, int count = 1)
    {
        // Keep batch generation bounded even if the controller is bypassed.
        count = Math.Clamp(count, 1, 20);

        // Deduplicate: same track ID submitted twice counts once
        selectedTrackNumbers = selectedTrackNumbers.Distinct().ToList();

        // Load all tracks for the playlist because recommendation seeding and duplicate checks need them.
        var tracks = await _db.TrackMetadatas
            .Where(t => t.PlaylistId == playlistId)
            .OrderBy(t => t.TrackNumber)
            .ToListAsync();

        // A playlist without stored tracks cannot produce recommendations.
        if (tracks.Count == 0)
            throw new InvalidOperationException("Playlist not found or has no tracks.");

        // Read playlist metadata without tracking because it is used only for platform detection.
        var playlist = await _db.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        // Spotify playlists use Spotify search; all other playlist types use YouTube search.
        var isSpotifyPlaylist = playlist?.ExternalUrl.Contains("spotify.com", StringComparison.OrdinalIgnoreCase) == true;

        // Only authenticate to Spotify when the playlist source needs Spotify recommendations.
        var spotifyAuth = isSpotifyPlaylist ? await CreateSpotifyAuthAsync() : null;

        if (selectedTrackNumbers.Count > 0)
        {
            // Build a fast lookup of valid one-based track numbers.
            var validNumbers = new HashSet<int>(tracks.Select(t => t.TrackNumber));

            // Collect any user-supplied numbers that do not exist in this playlist.
            var invalid = selectedTrackNumbers.Where(n => !validNumbers.Contains(n)).ToList();

            // Tell the caller exactly which selections were invalid.
            if (invalid.Count > 0)
                throw new ArgumentException($"Track numbers not found in playlist: {string.Join(", ", invalid)}");
        }

        // HashSet enables fast favourite membership checks during seed selection.
        var selectedSet = new HashSet<int>(selectedTrackNumbers);

        // Randomness is used for seed rotation, query order, and weighted result picking.
        var rng = new Random();

        // ── DEDUP SETS ─────────────────────────────────────────────────────────
        // Past recommendation titles should not be recommended again.
        var pastRecs = await _db.Recommendations
            .Where(r => r.PlaylistId == playlistId)
            .Select(r => r.SuggestedTrackName)
            .ToListAsync();

        // Existing playlist tracks are excluded so the app recommends something new.
        var existingNormalized = new HashSet<string>(tracks.Select(t => NormalizeTitle(t.TrackName)));

        // Add previous suggestions to the same duplicate set.
        foreach (var past in pastRecs)
            existingNormalized.Add(NormalizeTitle(past));

        // Results are accumulated and saved after the loop.
        var results = new List<Recommendation>();

        // Avoid using the same seed track repeatedly within a multi-recommendation batch.
        var usedSeeds = new HashSet<int>();

        // Generate up to the requested number of recommendations.
        for (int iter = 0; iter < count; iter++)
        {
            // ── SEED TRACK ─────────────────────────────────────────────────────
            // Rotate through different seed tracks across iterations so each
            // recommendation uses a distinct starting point where possible.
            TrackMetadata seedTrack;
            if (selectedSet.Count > 0)
            {
                // Limit seed pool to user-selected favourite tracks.
                var favTracks = tracks.Where(t => selectedSet.Contains(t.TrackNumber)).ToList();

                // Prefer favourites not used earlier in this batch.
                var unusedFavs = favTracks.Where(t => !usedSeeds.Contains(t.TrackNumber)).ToList();

                // If all favourites were used, recycle the favourite pool.
                var pool = unusedFavs.Count > 0 ? unusedFavs : favTracks;

                // Pick one seed randomly from the available favourite pool.
                seedTrack = pool[rng.Next(pool.Count)];
            }
            else
            {
                // Without explicit favourites, every playlist track can become a seed.
                var unused = tracks.Where(t => !usedSeeds.Contains(t.TrackNumber)).ToList();

                // Recycle the full pool only after every track has been used once.
                var pool = unused.Count > 0 ? unused : tracks;

                // Pick one seed randomly from the available full-playlist pool.
                seedTrack = pool[rng.Next(pool.Count)];
            }

            // Remember that this seed was used in this batch.
            usedSeeds.Add(seedTrack.TrackNumber);

            // Search query style differs slightly when the seed came from explicit favourites.
            bool seedIsFavorite = selectedSet.Contains(seedTrack.TrackNumber);

            // ── SEARCH QUERIES ─────────────────────────────────────────────────
            var queries = new List<string>();
            if (seedIsFavorite)
            {
                if (seedTrack.Genre != null)
                {
                    // Favourite + genre gives the search a strong artist/genre taste signal.
                    queries.Add($"{seedTrack.Genre} {seedTrack.ArtistName}");
                    queries.Add($"songs like {seedTrack.ArtistName}");
                    queries.Add($"{seedTrack.Genre} similar to {seedTrack.ArtistName}");
                }
                else
                {
                    // Without genre metadata, lean on artist similarity.
                    queries.Add($"{seedTrack.ArtistName} similar songs");
                    queries.Add($"songs like {seedTrack.ArtistName}");
                    queries.Add($"artists similar to {seedTrack.ArtistName}");
                }

                if (selectedSet.Count > 1)
                {
                    // Blend another favourite artist into the query when possible.
                    var others = tracks
                        .Where(t => selectedSet.Contains(t.TrackNumber) && t.TrackNumber != seedTrack.TrackNumber)
                        .ToList();
                    if (others.Count > 0)
                    {
                        // Randomly choose a second favourite to diversify the taste signal.
                        var other = others[rng.Next(others.Count)];
                        queries.Add($"{seedTrack.ArtistName} {other.ArtistName} similar");

                        // Add the other favourite's genre if it differs from the seed genre.
                        if (other.Genre != null && other.Genre != seedTrack.Genre)
                            queries.Add($"{other.Genre} {seedTrack.ArtistName}");
                    }
                }
            }
            else
            {
                if (seedTrack.Genre != null)
                {
                    // Full-playlist mode uses broader genre discovery queries.
                    queries.Add($"best {seedTrack.Genre} music");
                    queries.Add($"top {seedTrack.Genre} songs");
                    queries.Add($"{seedTrack.Genre} hits");
                }
                else
                {
                    // If genre is unknown, fall back to general music/artist similarity.
                    queries.Add("popular music");
                    queries.Add($"songs like {seedTrack.ArtistName}");
                }
            }

            // Combine taste-specific and broad fallback queries, remove duplicates, and randomize order.
            var searchQueries = queries
                .Concat(BuildFallbackRecommendationQueries(seedTrack))
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(_ => rng.Next())
                .ToList();

            // A recommendation is valid only after a real search candidate is chosen.
            string? recTitle = null;
            string? recArtist = null;

            if (isSpotifyPlaylist && spotifyAuth != null)
            {
                // Search Spotify directly for Spotify-sourced playlists.
                var spotifyCandidates = await SearchSpotifyTrackCandidatesAsync(
                    spotifyAuth.Client,
                    searchQueries,
                    existingNormalized);

                // Choose one candidate only if the search found non-duplicate results.
                if (spotifyCandidates.Count > 0)
                {
                    var pick = PickWeightedCandidate(spotifyCandidates, rng);
                    recTitle = pick.Title;
                    recArtist = pick.Artist;
                }
            }
            else
            {
                // ── YOUTUBE SEARCH ─────────────────────────────────────────────────
                // YouTube search is used for YouTube playlists and as the non-Spotify source path.
                var youtube = new YoutubeClient();

                // Limit total scanned results to avoid very long searches.
                var scanned = 0;

                // Store candidate titles/artists plus video IDs used later for view-count weighting.
                var candidates = new List<(string Title, string Artist, string VideoId)>();

                // Try each query until enough candidates are found or the scan cap is reached.
                foreach (var query in searchQueries)
                {
                    await foreach (var result in youtube.Search.GetVideosAsync(query))
                    {
                        // Stop scanning after a fixed cap across all queries.
                        if (++scanned > 200) break;

                        // Ignore unavailable/long-form videos because recommendations should be songs.
                        if (result.Duration == null || result.Duration.Value.TotalMinutes > 15) continue;

                        // Reject common non-song result shapes.
                        var titleLower = result.Title.ToLowerInvariant();
                        if (titleLower.Contains("playlist") || titleLower.Contains("compilation") ||
                            titleLower.Contains("reaction") || titleLower.Contains(" | ")) continue;

                        // Infer artist/title from YouTube metadata.
                        var (artist, title) = ParseYouTubeTitle(result.Title, result.Author.ChannelTitle);

                        // Strip common official-video/audio suffixes from the parsed title.
                        var cleanTitle = TitleSuffixPattern.Replace(title, "").Trim();

                        // Skip candidates with no useful title after cleaning.
                        if (string.IsNullOrWhiteSpace(cleanTitle)) continue;

                        // Exclude playlist/history duplicates and duplicates already gathered in this search.
                        if (!IsDuplicateTitle(cleanTitle, existingNormalized) &&
                            candidates.All(c => !NormalizeTitle(c.Title).Equals(NormalizeTitle(cleanTitle), StringComparison.OrdinalIgnoreCase)))
                        {
                            // Add the candidate and keep its video ID for later engagement lookup.
                            candidates.Add((cleanTitle, artist, result.Id));

                            // Twelve candidates is enough for a varied weighted pick.
                            if (candidates.Count >= 12) break;
                        }
                    }

                    // Stop trying additional queries after reaching either cap.
                    if (scanned > 200 || candidates.Count >= 12) break;
                }

                if (candidates.Count > 0)
                {
                    // Fetch view counts in parallel so popular videos can receive higher weight.
                    var viewCounts = await Task.WhenAll(candidates.Select(async c =>
                    {
                        try { return (await youtube.Videos.GetAsync(c.VideoId)).Engagement.ViewCount; }
                        catch { return 0L; }
                    }));

                    // Convert raw view counts to square-root weights so huge videos do not dominate completely.
                    var weighted = candidates
                        .Select((c, i) => (c, weight: Math.Sqrt(Math.Max(viewCounts[i], 1L))))
                        .ToList();

                    // Roll a weighted random value across the candidate set.
                    double total = weighted.Sum(x => x.weight);
                    double roll = rng.NextDouble() * total;
                    var pick = weighted[^1].c;
                    foreach (var w in weighted)
                    {
                        // Subtract each weight until the rolled bucket is reached.
                        roll -= w.weight;
                        if (roll <= 0) { pick = w.c; break; }
                    }

                    // Store the selected YouTube candidate as the recommendation.
                    recTitle = pick.Title;
                    recArtist = pick.Artist;
                }
            }

            if (recTitle == null || recArtist == null)
            {
                // If a batch already has some results, return those instead of failing the whole batch.
                if (results.Count > 0) break;

                // If the first recommendation fails, tell the user to change the seed/source.
                throw new InvalidOperationException(
                    "Could not find a new recommendation for this playlist. Try choosing different favourite tracks or importing a broader playlist.");
            }

            // Persist enough data to render recommendation history later.
            var recommendation = new Recommendation
            {
                PlaylistId = playlistId,
                FavoriteTrackNumbers = string.Join(",", selectedTrackNumbers),
                SuggestedTrackName = recTitle,
                SuggestedArtist = recArtist
            };

            // Queue the new recommendation for database insertion.
            _db.Recommendations.Add(recommendation);

            // Prevent later iterations in this batch from recommending the same title.
            existingNormalized.Add(NormalizeTitle(recTitle));

            // Return the same entity instance to the caller after SaveChanges assigns IDs.
            results.Add(recommendation);
        }

        // Commit all generated recommendations in a single save.
        await _db.SaveChangesAsync();

        // Return generated rows to the controller/frontend.
        return results;
    }

    /// <summary>
    /// Builds broad fallback search phrases for a seed track. These are appended to the more
    /// taste-specific queries so the search still has room to find a real song when the first
    /// query is too narrow.
    /// </summary>
    private static IEnumerable<string> BuildFallbackRecommendationQueries(TrackMetadata seedTrack)
    {
        // Search for the seed title and artist together to find related search clusters.
        yield return $"{seedTrack.TrackName} {seedTrack.ArtistName}";

        // Ask directly for songs like the seed title.
        yield return $"songs like {seedTrack.TrackName}";

        // Use artist popularity as a broad fallback.
        yield return $"{seedTrack.ArtistName} popular songs";

        // "Radio" often surfaces related tracks on music platforms.
        yield return $"{seedTrack.ArtistName} radio";

        // Add genre-level fallbacks only when genre metadata exists.
        if (!string.IsNullOrWhiteSpace(seedTrack.Genre))
        {
            yield return $"{seedTrack.Genre} songs";
            yield return $"popular {seedTrack.Genre}";
        }
    }

    /// <summary>
    /// Searches Spotify across multiple queries and returns unique, non-duplicate track candidates.
    /// Each candidate receives a popularity-based weight used later for random-but-biased picking.
    /// </summary>
    private async Task<List<TrackCandidate>> SearchSpotifyTrackCandidatesAsync(
        SpotifyClient spotify,
        IReadOnlyCollection<string> queries,
        HashSet<string> existingNormalized)
    {
        // Accumulate usable Spotify results across all queries.
        var candidates = new List<TrackCandidate>();

        // Avoid adding the same Spotify title twice from different queries.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try each query until enough candidates are collected.
        foreach (var query in queries)
        {
            SearchResponse search;
            try
            {
                // Request Spotify tracks only, using market availability and a generous per-query limit.
                search = await spotify.Search.Item(new SearchRequest(
                    SearchRequest.Types.Track,
                    query)
                {
                    Market = GetSpotifyMarket(),
                    Limit = 50
                });
            }
            catch (APIException ex)
            {
                // Convert Spotify API failures into the same user-facing error format as imports.
                throw new InvalidOperationException(
                    $"Could not search Spotify for recommendations. {FormatSpotifyApiError(ex)}", ex);
            }

            // Inspect every returned track in the current query response.
            foreach (var track in search.Tracks.Items ?? [])
            {
                // Spotify can return null/malformed items; skip anything without a title.
                if (track == null || string.IsNullOrWhiteSpace(track.Name)) continue;

                // Normalize once for duplicate checks and this-query de-duping.
                var normalized = NormalizeTitle(track.Name);

                // Skip already-seen titles and anything already in the playlist/history.
                if (seen.Contains(normalized) || IsDuplicateTitle(track.Name, existingNormalized)) continue;

                // Remember this title so another query does not add it again.
                seen.Add(normalized);
#pragma warning disable CS0618 // Spotify still returns this weight in current track/search payloads.
                // Popularity is deprecated in the client type but still present in Spotify payloads.
                var popularity = track.Popularity;
#pragma warning restore CS0618

                // Store the title, first artist, and square-root popularity weight.
                candidates.Add(new TrackCandidate(
                    track.Name,
                    track.Artists.FirstOrDefault()?.Name ?? "Unknown",
                    Math.Sqrt(Math.Max(popularity, 1))));

                // Twenty candidates gives enough variety without doing extra API work.
                if (candidates.Count >= 20) return candidates;
            }
        }

        // Return whatever was found, even if empty.
        return candidates;
    }

    /// <summary>
    /// Selects one candidate using weighted randomness, so popular results are more likely but not
    /// guaranteed. This keeps recommendations varied while still favoring stronger matches.
    /// </summary>
    private static TrackCandidate PickWeightedCandidate(List<TrackCandidate> candidates, Random rng)
    {
        // Sum all candidate weights to create the random range.
        double total = candidates.Sum(c => c.Weight);

        // Pick a random point in the weighted range.
        double roll = rng.NextDouble() * total;

        // Default to the last item in case floating-point subtraction never crosses zero.
        var pick = candidates[^1];

        // Walk through the weighted buckets until the roll lands in one.
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll <= 0)
            {
                pick = candidate;
                break;
            }
        }

        // Return the selected candidate.
        return pick;
    }

    /// <summary>
    /// Updates the optional display name for a playlist. Whitespace clears the custom name and
    /// returns the app to URL-based display.
    /// </summary>
    public async Task<Playlist?> RenamePlaylistAsync(int id, string? name)
    {
        // Find the playlist row to update.
        var playlist = await _db.Playlists.FirstOrDefaultAsync(p => p.Id == id);

        // Return null so the controller can emit 404.
        if (playlist is null) return null;

        // Blank or whitespace-only names clear the custom display name.
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        // Apply the normalized display name.
        playlist.Name = trimmed;

        // Persist the rename.
        await _db.SaveChangesAsync();

        // Return the updated playlist for frontend state refresh.
        return playlist;
    }

    /// <summary>
    /// Deletes an imported playlist and its recommendation history. Track rows are removed through
    /// the playlist relationship configured by the database model/migrations.
    /// </summary>
    public async Task<bool> DeletePlaylistAsync(int id)
    {
        // Load the playlist row by ID.
        var playlist = await _db.Playlists.FirstOrDefaultAsync(p => p.Id == id);

        // Missing rows cannot be deleted.
        if (playlist is null) return false;

        // Delete recommendation history explicitly so it never becomes orphaned.
        var recs = _db.Recommendations.Where(r => r.PlaylistId == id);
        _db.Recommendations.RemoveRange(recs);

        // Delete the playlist itself; related tracks are removed by the relationship setup.
        _db.Playlists.Remove(playlist);

        // Commit both recommendation and playlist removal.
        await _db.SaveChangesAsync();

        // Signal success to the controller.
        return true;
    }

    /// <summary>
    /// Deletes one recommendation history row. Returns false when the ID is not present so the
    /// controller can respond with 404.
    /// </summary>
    public async Task<bool> DeleteRecommendationAsync(int id)
    {
        // Find the recommendation row by ID.
        var rec = await _db.Recommendations.FirstOrDefaultAsync(r => r.Id == id);

        // Missing rows map to 404 in the controller.
        if (rec is null) return false;

        // Queue the row for deletion.
        _db.Recommendations.Remove(rec);

        // Persist deletion.
        await _db.SaveChangesAsync();

        // Signal success.
        return true;
    }

    /// <summary>
    /// Deletes all recommendation rows associated with one playlist and returns the number removed.
    /// The playlist and track metadata remain intact.
    /// </summary>
    public async Task<int> DeletePlaylistHistoryAsync(int playlistId)
    {
        // Load all recommendations for the target playlist.
        var recs = await _db.Recommendations.Where(r => r.PlaylistId == playlistId).ToListAsync();

        // Nothing to delete is still a successful no-op.
        if (recs.Count == 0) return 0;

        // Queue every matching history row for deletion.
        _db.Recommendations.RemoveRange(recs);

        // Commit deletion.
        await _db.SaveChangesAsync();

        // Return the deleted count for UI feedback/debugging.
        return recs.Count;
    }

    /// <summary>
    /// Calculates top genre, top artist, total tracks, and total duration for either all tracks or
    /// a selected list of playlist IDs.
    /// </summary>
    public async Task<PlaylistStats> GetStatisticsAsync(List<int>? playlistIds)
    {
        // Start with all stored tracks.
        IQueryable<TrackMetadata> query = _db.TrackMetadatas;

        // Narrow to selected playlists when the caller provided IDs.
        if (playlistIds != null && playlistIds.Count > 0)
            query = query.Where(t => playlistIds.Contains(t.PlaylistId));

        // Materialize once so grouping and duration math happen in memory.
        var tracks = await query.ToListAsync();

        // Empty selections/library still return a stable stats object.
        if (tracks.Count == 0) return new PlaylistStats(null, null, 0, 0, 0);

        // Pick the most frequent non-null genre.
        var topGenre = tracks.Where(t => t.Genre != null).GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        // Pick the most frequent artist.
        var topArtist = tracks.GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        // Sum durations using long to avoid integer overflow on large libraries.
        var totalDuration = TimeSpan.FromMilliseconds(tracks.Sum(t => (long)t.DurationMs));

        // Return the compact stat record expected by the frontend.
        return new PlaylistStats(topGenre, topArtist, tracks.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
    }

    /// <summary>
    /// Returns saved playlists newest first so recently imported playlists appear first in the UI.
    /// </summary>
    public async Task<List<Playlist>> GetPlaylistsAsync() =>
        await _db.Playlists.OrderByDescending(p => p.ProcessedAt).ToListAsync();

    /// <summary>
    /// Returns track metadata for a playlist, optionally filtered to specific one-based track
    /// numbers. Null indicates that the playlist does not exist.
    /// </summary>
    public async Task<List<TrackMetadata>?> GetTracksAsync(int playlistId, IReadOnlyCollection<int>? trackNumbers = null)
    {
        // Check playlist existence first so missing playlist and empty playlist are distinguishable.
        var exists = await _db.Playlists.AnyAsync(p => p.Id == playlistId);
        if (!exists) return null;

        // Start with all tracks for this playlist.
        var query = _db.TrackMetadatas.Where(t => t.PlaylistId == playlistId);

        // Apply optional track-number filtering.
        if (trackNumbers != null && trackNumbers.Count > 0)
        {
            // Deduplicate requested numbers before putting them into the SQL IN filter.
            var distinct = trackNumbers.Distinct().ToList();
            query = query.Where(t => distinct.Contains(t.TrackNumber));
        }

        // Always return tracks in playlist order.
        return await query.OrderBy(t => t.TrackNumber).ToListAsync();
    }

    /// <summary>
    /// Builds grouped recommendation history and expands favourite track numbers into readable
    /// track labels. The extra lookup avoids forcing the frontend to fetch every playlist's tracks.
    /// </summary>
    public async Task<List<PlaylistHistoryEntry>> GetHistoryAsync()
    {
        // Load recommendations ordered by playlist and creation time for stable grouping/display.
        var recommendations = await _db.Recommendations
            .OrderBy(r => r.PlaylistId).ThenBy(r => r.CreatedAt)
            .ToListAsync();

        // Gather only playlist IDs that actually have recommendations.
        var playlistIds = recommendations.Select(r => r.PlaylistId).Distinct().ToList();

        // Load playlist metadata for history card headers.
        var playlists = await _db.Playlists
            .Where(p => playlistIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // Build a nested lookup: playlist ID -> track number -> readable track label.
        var trackLookup = (await _db.TrackMetadatas
                .Where(t => playlistIds.Contains(t.PlaylistId))
                .Select(t => new { t.PlaylistId, t.TrackNumber, t.TrackName, t.ArtistName })
                .ToListAsync())
            .GroupBy(t => t.PlaylistId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.TrackNumber, x => $"{x.TrackName} — {x.ArtistName}"));

        // Convert flat recommendation rows into grouped history entries.
        return recommendations
            .GroupBy(r => r.PlaylistId)
            .Select(g =>
            {
                // Find track-name lookup for this playlist, if its tracks still exist.
                trackLookup.TryGetValue(g.Key, out var byNumber);

                // Build a history group matching the frontend's card structure.
                return new PlaylistHistoryEntry(
                    g.Key,
                    playlists.TryGetValue(g.Key, out var p) ? p.ExternalUrl : "unknown",
                    playlists.TryGetValue(g.Key, out var pl) ? pl.Name : null,
                    g.Select(r =>
                    {
                        // Deserialize stored comma-separated favourite numbers.
                        var nums = r.FavoriteTrackNumbers
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => int.TryParse(s, out var n) ? n : 0)
                            .Where(n => n > 0)
                            .ToList();

                        // Resolve favourite numbers to names, falling back to "Track #n".
                        var names = nums
                            .Select(n => byNumber != null && byNumber.TryGetValue(n, out var name) ? name : $"Track #{n}")
                            .ToList();

                        // Create the API-facing suggestion entry.
                        return new SuggestionEntry(
                            r.Id,
                            nums,
                            names,
                            r.SuggestedTrackName,
                            r.SuggestedArtist,
                            r.CreatedAt
                        );
                    }).ToList()
                );
            })
            .ToList();
    }

    /// <summary>
    /// Imports a YouTube playlist by reading video metadata, parsing likely artist/title pairs,
    /// enriching artists with genres, then saving normalized tracks and summary statistics.
    /// </summary>
    private async Task<PlaylistProcessingResult> ProcessYouTubeAsync(string url, Uri uri)
    {
        // Parse query parameters to find YouTube's playlist identifier.
        var queryParams = QueryHelpers.ParseQuery(uri.Query);

        // YouTube playlist URLs must include a list parameter.
        if (!queryParams.TryGetValue("list", out var listValues) || string.IsNullOrWhiteSpace(listValues.FirstOrDefault()))
            throw new ArgumentException("No YouTube playlist ID found in URL. Ensure the URL contains a 'list' parameter.");

        // Normalize imported YouTube URLs to the canonical playlist URL.
        var playlistId = listValues.First()!.Trim();
        var canonicalUrl = $"https://www.youtube.com/playlist?list={playlistId}";

        // YoutubeExplode handles public playlist traversal without an API key.
        var youtube = new YoutubeClient();

        // Collect raw video data before transforming it into TrackMetadata.
        var rawVideos = new List<(string Title, string ChannelTitle, TimeSpan Duration)>();

        // Iterate every video in playlist order.
        await foreach (var video in youtube.Playlists.GetVideosAsync(playlistId))
            rawVideos.Add((video.Title, video.Author.ChannelTitle, video.Duration ?? TimeSpan.Zero));

        // Empty/private/unavailable playlists cannot be imported.
        if (rawVideos.Count == 0)
            throw new InvalidOperationException("The YouTube playlist is empty or could not be accessed.");

        // Infer artists, group by frequency, and order most-listened first for stats/fallback genre.
        var allUniqueArtists = rawVideos
            .Select(v => ParseYouTubeTitle(v.Title, v.ChannelTitle).Artist)
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        // Most frequent artist becomes playlist top artist.
        var topArtist = allUniqueArtists.First();

        // Fetch genre labels for all unique artists.
        var artistGenres = await LookupGenresAsync(allUniqueArtists);

        // Use the most common genre as fallback if a parsed artist misses a dictionary match.
        var fallbackGenre = artistGenres.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

        // Create the playlist entity with normalized source URL.
        var playlist = new Playlist { ExternalUrl = canonicalUrl };

        // Sum durations while adding tracks.
        var totalDuration = TimeSpan.Zero;

        // Store one-based playlist positions.
        var trackNum = 1;

        // Convert raw YouTube video rows into normalized track metadata.
        foreach (var (videoTitle, channelTitle, duration) in rawVideos)
        {
            // Parse a best-effort artist/title pair from YouTube title/channel data.
            var (artist, title) = ParseYouTubeTitle(videoTitle, channelTitle);

            // Add the track as a child entity of the playlist.
            playlist.Tracks.Add(new TrackMetadata
            {
                TrackNumber = trackNum++,
                TrackName = title,
                ArtistName = artist,
                Genre = artistGenres.TryGetValue(artist, out var genre) ? genre : fallbackGenre,
                DurationMs = (int)duration.TotalMilliseconds
            });

            // Add to playlist duration total.
            totalDuration += duration;
        }

        // Persist the playlist and its child tracks.
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        // Pick display stats after persistence.
        var topGenre = artistGenres.TryGetValue(topArtist, out var tg) ? tg : fallbackGenre;
        var trackList = playlist.Tracks.ToList();
        var stats = new PlaylistStats(topGenre, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, stats);
    }

    /// <summary>
    /// Attempts to obtain Spotify's public web-player token. This fallback allows public playlist
    /// imports when app credentials are not configured, but it is less reliable than official
    /// Client Credentials auth.
    /// </summary>
    private async Task<string?> GetSpotifyAnonymousTokenAsync()
    {
        try
        {
            // Use a plain HTTP client because this endpoint is not part of SpotifyAPI.Web.
            using var client = _httpFactory.CreateClient();

            // Request the same public token the Spotify web player uses.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://open.spotify.com/get_access_token?reason=transport&productType=web_player");

            // Browser-like headers make Spotify more likely to return the public token payload.
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("application/json");

            // Send the token request.
            using var response = await client.SendAsync(request);

            // Non-success means anonymous auth is unavailable.
            if (!response.IsSuccessStatusCode) return null;

            // Parse the JSON response and extract accessToken.
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
        }
        // Anonymous auth is optional; failures simply return null.
        catch { return null; }
    }

    /// <summary>
    /// Reads the Spotify market setting and defaults to US. Spotify search and track hydration use
    /// a market because availability and metadata can vary by region.
    /// </summary>
    private string GetSpotifyMarket()
    {
        // Read optional market configuration from appsettings/user secrets/environment.
        var market = _config["Spotify:Market"];

        // Default to US because Spotify requires a valid market for reliable availability filtering.
        return string.IsNullOrWhiteSpace(market) ? "US" : market.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Creates a Spotify client. Configured ClientId/ClientSecret are preferred; otherwise the
    /// method falls back to an anonymous web-player token for public data only.
    /// </summary>
    private async Task<SpotifyAuth> CreateSpotifyAuthAsync()
    {
        // Read configured Spotify API credentials.
        var clientId = _config["Spotify:ClientId"];
        var clientSecret = _config["Spotify:ClientSecret"];

        // Treat placeholder values the same as missing credentials.
        bool hasCredentials = !string.IsNullOrWhiteSpace(clientId) && clientId != "mock_id"
                           && !string.IsNullOrWhiteSpace(clientSecret) && clientSecret != "mock_secret";

        if (hasCredentials)
        {
            try
            {
                // Request a Client Credentials token for server-side Spotify API access.
                var token = await new OAuthClient().RequestToken(
                    new ClientCredentialsRequest(clientId!, clientSecret!));

                // Build a Spotify client using the returned access token.
                var cfg = SpotifyClientConfig.CreateDefault()
                    .WithToken(token.AccessToken, token.TokenType ?? "Bearer");

                // Official credentials can use playlist-item and track endpoints.
                return new SpotifyAuth(new SpotifyClient(cfg), true);
            }
            catch (APIException ex)
            {
                // Configured-but-invalid credentials should fail loudly so they can be fixed.
                throw new InvalidOperationException(
                    $"Spotify rejected the configured API credentials. {FormatSpotifyApiError(ex)}", ex);
            }
        }

        // Fall back to a public web-player token when credentials are not configured.
        var anonToken = await GetSpotifyAnonymousTokenAsync();

        // If anonymous auth also fails, give setup guidance.
        if (string.IsNullOrWhiteSpace(anonToken))
            throw new InvalidOperationException("Could not connect to Spotify. Set Spotify:ClientId and Spotify:ClientSecret in appsettings.json for reliable access.");

        // Anonymous tokens can read some public embed data but should not be trusted for track hydration.
        return new SpotifyAuth(
            new SpotifyClient(SpotifyClientConfig.CreateDefault().WithToken(anonToken, "Bearer")),
            false);
    }

    /// <summary>
    /// Downloads the public Spotify embed HTML for a playlist. The embed page often exposes enough
    /// track information to import public playlists that the Web API refuses.
    /// </summary>
    private async Task<string?> FetchSpotifyEmbedHtmlAsync(string playlistId)
    {
        try
        {
            // Use a generic HTTP client for the public embed page.
            using var client = _httpFactory.CreateClient();

            // Request Spotify's embeddable playlist page.
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://open.spotify.com/embed/playlist/{Uri.EscapeDataString(playlistId)}");

            // Browser-like headers help access public embed HTML.
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("text/html");

            // Fetch the embed HTML.
            using var response = await client.SendAsync(request);

            // Failed embed fetch means this fallback is unavailable.
            if (!response.IsSuccessStatusCode) return null;

            // Return the raw HTML for ID/state extraction.
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            // Public embed fallback should fail softly.
            return null;
        }
    }

    /// <summary>
    /// Finds Spotify track IDs in embed HTML so they can be hydrated through the official track
    /// endpoint when the current auth method supports it.
    /// </summary>
    private static List<string> ExtractSpotifyTrackIds(string html) =>
        SpotifyTrackReferencePattern.Matches(html)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Recursively searches Spotify's embedded JSON for a property by name. The embed payload shape
    /// can move between releases, so recursive lookup is safer than relying on a fixed path.
    /// </summary>
    private static bool TryFindJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        // Search object properties directly, then recursively inspect child values.
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Fast path: the current object has the requested property.
            if (element.TryGetProperty(propertyName, out value))
                return true;

            // Recursively search each property value.
            foreach (var property in element.EnumerateObject())
            {
                if (TryFindJsonProperty(property.Value, propertyName, out value))
                    return true;
            }
        }
        // Arrays can contain nested objects, so search every item.
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindJsonProperty(item, propertyName, out value))
                    return true;
            }
        }

        // No matching property found in this branch.
        value = default;
        return false;
    }

    /// <summary>
    /// Parses Spotify's Next.js embed state and extracts basic track rows. This is the last-resort
    /// import path when playlist items or track ID hydration are unavailable.
    /// </summary>
    private static List<SpotifyRawTrack> ExtractSpotifyTracksFromEmbedState(string html)
    {
        // Find Spotify's server-rendered Next.js data script in the embed HTML.
        var match = Regex.Match(
            html,
            @"<script[^>]+id=[""']__NEXT_DATA__[""'][^>]*>(?<json>.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // If the script is missing, this extraction strategy cannot work.
        if (!match.Success) return [];

        try
        {
            // Decode HTML entities before parsing JSON.
            var json = WebUtility.HtmlDecode(match.Groups["json"].Value);

            // Parse the embedded application state.
            using var doc = JsonDocument.Parse(json);

            // Locate Spotify's trackList array wherever it lives in the payload.
            if (!TryFindJsonProperty(doc.RootElement, "trackList", out var trackList) ||
                trackList.ValueKind != JsonValueKind.Array)
                return [];

            // Convert embedded state items into raw track records.
            var tracks = new List<SpotifyRawTrack>();
            foreach (var item in trackList.EnumerateArray())
            {
                // Track title is required.
                var title = item.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString()
                    : null;

                // Subtitle usually contains artist name in Spotify embeds.
                var artist = item.TryGetProperty("subtitle", out var subtitleElement)
                    ? subtitleElement.GetString()
                    : null;

                // Duration may be missing in public embed state.
                var duration = item.TryGetProperty("duration", out var durationElement) &&
                               durationElement.TryGetInt32(out var durationMs)
                    ? durationMs
                    : 0;

                // Store only rows that have a usable title.
                if (!string.IsNullOrWhiteSpace(title))
                    tracks.Add(new SpotifyRawTrack(title, artist ?? "Unknown", duration));
            }

            // Return all extracted embedded tracks.
            return tracks;
        }
        catch
        {
            // Malformed/changed embed state should fail softly and let other paths handle errors.
            return [];
        }
    }

    /// <summary>
    /// Converts Spotify track IDs into full track names, artists, and durations using the Web API.
    /// </summary>
    private async Task<List<SpotifyRawTrack>> HydrateSpotifyTracksAsync(SpotifyClient spotify, List<string> trackIds)
    {
        // Store hydrated track records in the same lightweight import shape.
        var tracks = new List<SpotifyRawTrack>();

        // Market affects track availability and metadata.
        var market = GetSpotifyMarket();

        // Hydrate each discovered track ID through Spotify's track endpoint.
        foreach (var trackId in trackIds)
        {
            var fullTrack = await spotify.Tracks.Get(trackId, new TrackRequest { Market = market });
            tracks.Add(new SpotifyRawTrack(
                fullTrack.Name,
                fullTrack.Artists.FirstOrDefault()?.Name ?? "Unknown",
                fullTrack.DurationMs));
        }

        // Return all successfully hydrated tracks.
        return tracks;
    }

    /// <summary>
    /// Attempts public-embed Spotify import. It first tries to hydrate discovered track IDs through
    /// the Web API, then falls back to the less complete embedded state if hydration fails.
    /// </summary>
    private async Task<List<SpotifyRawTrack>?> TryGetSpotifyTracksFromPublicEmbedAsync(string playlistId, SpotifyAuth auth)
    {
        // Download public embed HTML first.
        var html = await FetchSpotifyEmbedHtmlAsync(playlistId);

        // Without HTML there is no public fallback data.
        if (string.IsNullOrWhiteSpace(html))
            return null;

        // Try to extract direct track IDs from links/URIs in the embed page.
        var trackIds = ExtractSpotifyTrackIds(html);

        // Hydrate track IDs only when the current auth mode can use the Web API track endpoint.
        if (auth.CanUseWebApiTrackEndpoint && trackIds.Count > 0)
        {
            try
            {
                // Convert IDs into complete track data.
                var hydratedTracks = await HydrateSpotifyTracksAsync(auth.Client, trackIds);
                if (hydratedTracks.Count > 0)
                    return hydratedTracks;
            }
            catch (APIException)
            {
                // Fall back to Spotify's public embed state below.
            }
        }

        // Final fallback: parse whatever track data Spotify embedded in the page state.
        var embeddedTracks = ExtractSpotifyTracksFromEmbedState(html);

        // Null means "fallback unavailable"; non-empty list means import can continue.
        return embeddedTracks.Count > 0 ? embeddedTracks : null;
    }

    /// <summary>
    /// Imports a Spotify playlist through the Web API when possible and through public embed data
    /// when necessary. The resulting raw tracks are normalized into TrackMetadata entities.
    /// </summary>
    private async Task<PlaylistProcessingResult> ProcessSpotifyAsync(string url, Uri uri)
    {
        // Split the Spotify URL path to find /playlist/{id}.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Locate the "playlist" path segment.
        var playlistIdx = Array.IndexOf(segments, "playlist");

        // A Spotify URL without a playlist ID is invalid for this importer.
        if (playlistIdx < 0 || playlistIdx + 1 >= segments.Length)
            throw new ArgumentException("Invalid Spotify playlist URL.");

        // Extract the playlist ID from the URL.
        var playlistId = segments[playlistIdx + 1];

        // Create Spotify client/auth before attempting playlist item reads.
        var auth = await CreateSpotifyAuthAsync();

        // Raw tracks are collected before being converted into EF entities.
        var rawTracks = new List<SpotifyRawTrack>();
        try
        {
            // Request the first page of playlist items from Spotify.
            var spotifyPlaylistItems = await auth.Client.Playlists.GetPlaylistItems(playlistId);

            // SpotifyAPI.Web paginator walks through every result page.
            await foreach (var item in auth.Client.Paginate(spotifyPlaylistItems))
            {
                // Only actual audio tracks are imported; local/episode/etc. items are skipped.
                if (item.Track is FullTrack fullTrack)
                    rawTracks.Add(new SpotifyRawTrack(
                        fullTrack.Name,
                        fullTrack.Artists.FirstOrDefault()?.Name ?? "Unknown",
                        fullTrack.DurationMs));
            }
        }
        catch (APIException ex)
        {
            // If the Web API rejects the playlist, try the public embed fallback.
            var fallbackTracks = await TryGetSpotifyTracksFromPublicEmbedAsync(playlistId, auth);
            if (fallbackTracks is { Count: > 0 })
                rawTracks = fallbackTracks;
            else
                // No fallback means the Spotify error becomes the user-facing import error.
                throw new InvalidOperationException(
                    $"Could not access that Spotify playlist. {FormatSpotifyApiError(ex)}", ex);
        }

        // Some API calls succeed but produce zero usable tracks, so try embed fallback then too.
        if (rawTracks.Count == 0)
        {
            var fallbackTracks = await TryGetSpotifyTracksFromPublicEmbedAsync(playlistId, auth);
            if (fallbackTracks is { Count: > 0 })
                rawTracks = fallbackTracks;
        }

        // If every path failed to produce tracks, import cannot continue.
        if (rawTracks.Count == 0)
            throw new InvalidOperationException(
                "The Spotify playlist is empty, has no audio tracks, or is not available through Spotify's public playlist data.");

        // Group artists by frequency for top-artist stats and genre lookup order.
        var allUniqueArtists = rawTracks
            .GroupBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        // Most frequent artist becomes top artist.
        var topArtist = allUniqueArtists.First();

        // Fetch genre labels for all unique Spotify artists.
        var artistGenres = await LookupGenresAsync(allUniqueArtists);

        // Use the most common genre when a specific artist lookup is missing.
        var fallbackGenre = artistGenres.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

        // Preserve the original Spotify URL on the playlist entity.
        var playlist = new Playlist { ExternalUrl = url };

        // Use long while summing durations to avoid overflow.
        var totalDurationMs = 0L;

        // Store one-based positions in import order.
        var trackNum = 1;

        // Convert raw Spotify tracks into persisted TrackMetadata children.
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

            // Add track duration to the playlist total.
            totalDurationMs += durationMs;
        }

        // Save playlist and child tracks together.
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        // Convert total duration into hours/minutes for the stats response.
        var totalDuration = TimeSpan.FromMilliseconds(totalDurationMs);

        // Pick top genre from top artist when possible, otherwise use fallback.
        var topGenre = artistGenres.TryGetValue(topArtist, out var tg) ? tg : fallbackGenre;

        // Materialize child collection for count.
        var trackList = playlist.Tracks.ToList();

        // Return imported playlist plus summary stats.
        var stats = new PlaylistStats(topGenre, topArtist, trackList.Count, (int)totalDuration.TotalHours, totalDuration.Minutes);
        return new PlaylistProcessingResult(playlist, stats);
    }

    /// <summary>
    /// Converts Spotify API status codes into user-facing error text that explains the likely
    /// cause and what the user can try next.
    /// </summary>
    private static string FormatSpotifyApiError(APIException ex)
    {
        // SpotifyAPI.Web exposes response metadata on APIException.
        var status = ex.Response?.StatusCode;

        // Convert known statuses into friendlier explanations.
        return status switch
        {
            HttpStatusCode.BadRequest => "Spotify returned 400 Bad Request.",
            HttpStatusCode.Unauthorized => "Spotify returned 401 Unauthorized. Reading playlist items may require a user-authorized Spotify token, not only app client credentials.",
            HttpStatusCode.Forbidden => "Spotify returned 403 Forbidden. The playlist may be private or unavailable to this app.",
            HttpStatusCode.NotFound => "Spotify returned 404 Not Found. The playlist may be private, personalized, region-limited, or not available through the Spotify Web API.",
            HttpStatusCode.TooManyRequests => "Spotify returned 429 Too Many Requests. Please wait a bit and try again.",
            null => "Spotify returned an unknown API error.",
            _ => $"Spotify returned {(int)status.Value} {status.Value}."
        };
    }

    /// <summary>
    /// Infers artist and track title from YouTube metadata. Official topic channels and common
    /// "artist - title" patterns are handled before falling back to channel-as-artist.
    /// </summary>
    private static (string Artist, string Title) ParseYouTubeTitle(string videoTitle, string channelTitle)
    {
        // Topic channels: title is the song name, channel minus " - Topic" is the artist
        if (channelTitle.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
            return (channelTitle[..^" - Topic".Length].Trim(), videoTitle.Trim());

        // "Channel - Song Title" format (artist uploads own song)
        // Build the prefix once so comparison and slicing use the same text.
        var ownPrefix = channelTitle + " - ";
        if (videoTitle.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase))
            return (channelTitle, videoTitle[ownPrefix.Length..].Trim());

        // "Song Title - Channel" format (common for official music videos)
        // Build the suffix pattern for cases where channel name appears at the end.
        var channelSuffix = " - " + channelTitle;

        // Locate the suffix inside the video title.
        var suffixIdx = videoTitle.IndexOf(channelSuffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIdx > 0)
            return (channelTitle, videoTitle[..suffixIdx].Trim());

        // Default: channel is artist, full video title is the song
        return (channelTitle, videoTitle.Trim());
    }
}

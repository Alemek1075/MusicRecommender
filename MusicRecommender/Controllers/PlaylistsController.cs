using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Services;

namespace MusicRecommender.Controllers;

/// <summary>
/// Handles playlist-facing API calls: importing a Spotify/YouTube playlist, listing saved
/// playlists, retrieving track metadata, renaming playlists, and deleting a playlist with its
/// related data. The controller stays intentionally thin and delegates business rules to
/// <see cref="PlaylistProcessingService"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private readonly PlaylistProcessingService _service;

    /// <summary>
    /// Receives the processing service through dependency injection so each request can use the
    /// same EF Core scope and external API helpers configured in Program.cs.
    /// </summary>
    public PlaylistsController(PlaylistProcessingService service) => _service = service;

    /// <summary>
    /// Imports a playlist URL, extracts its tracks, enriches artist genres where possible, saves
    /// the playlist, and returns the created playlist plus summary statistics.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] PlaylistSubmitDto dto)
    {
        // Refuse empty input before the service does any URL parsing or network work.
        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest("Url is required.");

        try
        {
            // Import and persist the playlist, then return both playlist data and calculated stats.
            var result = await _service.ProcessAsync(dto.Url);
            return Ok(result);
        }
        // Validation and import failures are expected user-correctable errors, so expose them as 400.
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>
    /// Returns all imported playlists in most-recent-first order. The service controls ordering
    /// and persistence details so the controller only serializes the result.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // The service returns playlists in display order for the frontend.
        var playlists = await _service.GetPlaylistsAsync();
        return Ok(playlists);
    }

    /// <summary>
    /// Returns every track for one playlist, or a subset when one or more track numbers are
    /// supplied. Both a singular trackNumber and repeated trackNumbers query parameter are
    /// accepted because the frontend uses both patterns in different views.
    /// </summary>
    [HttpGet("{id:int}/tracks")]
    public async Task<IActionResult> GetTracks(
        int id,
        [FromQuery] int? trackNumber = null,
        [FromQuery] List<int>? trackNumbers = null)
    {
        // Start with repeated query parameters, or an empty list when none were supplied.
        var filter = trackNumbers ?? new List<int>();

        // Preserve support for the older singular query parameter.
        if (trackNumber.HasValue) filter.Add(trackNumber.Value);

        // Passing null means "return every track"; passing a list means "filter to these numbers".
        var tracks = await _service.GetTracksAsync(id, filter.Count > 0 ? filter : null);

        // A null result means the playlist ID does not exist.
        if (tracks is null)
            return NotFound();

        // Return the ordered track metadata list.
        return Ok(tracks);
    }

    /// <summary>
    /// Stores a friendly display name for a playlist. Passing null or whitespace clears the
    /// custom name and lets the UI fall back to showing the original playlist URL.
    /// </summary>
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] PlaylistRenameDto dto)
    {
        // The service trims whitespace and converts blank names to null.
        var playlist = await _service.RenamePlaylistAsync(id, dto.Name);

        // Missing playlist IDs become 404 instead of silently creating anything.
        if (playlist is null) return NotFound();

        // Return the updated entity so the UI can refresh local state without another fetch.
        return Ok(playlist);
    }

    /// <summary>
    /// Deletes a playlist and relies on EF/database relationships to remove associated track and
    /// recommendation records where configured by the model/migrations.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Delete returns false when the playlist was not found.
        var ok = await _service.DeletePlaylistAsync(id);
        if (!ok) return NotFound();

        // Successful deletion has no body.
        return NoContent();
    }
}

/// <summary>
/// Request body for playlist imports. The URL is validated by the controller and parsed by the
/// processing service, which supports Spotify and YouTube playlist links.
/// </summary>
public record PlaylistSubmitDto(string Url);

/// <summary>
/// Request body for playlist renaming. A null name means "remove the custom display name".
/// </summary>
public record PlaylistRenameDto(string? Name);

using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Services;

namespace MusicRecommender.Controllers;

/// <summary>
/// Exposes recommendation operations: generating new track suggestions, reading recommendation
/// history, and deleting individual or playlist-wide history entries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RecommendationsController : ControllerBase
{
    private readonly PlaylistProcessingService _service;

    /// <summary>
    /// Uses the central playlist processing service because recommendation generation depends on
    /// saved playlist tracks, past recommendations, Spotify search, and YouTube search.
    /// </summary>
    public RecommendationsController(PlaylistProcessingService service) => _service = service;

    /// <summary>
    /// Generates one or more recommendations for a playlist. selectedTrackNumbers are treated as
    /// favourites/seeds; if none are provided, the service uses the full playlist as the taste
    /// profile. count is clamped so the API cannot request an unbounded search workload.
    /// </summary>
    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] int playlistId, [FromQuery] List<int>? selectedTrackNumbers, [FromQuery] int count = 1)
    {
        try
        {
            // Empty selections are valid and mean "use the whole playlist"; count is bounded twice for safety.
            var result = await _service.GenerateAsync(playlistId, selectedTrackNumbers ?? [], Math.Clamp(count, 1, 20));

            // Return the generated recommendation rows exactly as saved.
            return Ok(result);
        }
        // Bad playlist IDs, invalid track numbers, and no-candidate cases are shown to the user.
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>
    /// Returns recommendation history grouped by playlist, including enough favourite-track
    /// metadata for the frontend to explain why each suggestion was made.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        // History is already grouped and expanded by the service for the UI.
        var history = await _service.GetHistoryAsync();
        return Ok(history);
    }

    /// <summary>
    /// Removes a single saved recommendation row from history without touching the playlist or
    /// other suggestions generated from the same playlist.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Remove one recommendation entry by database ID.
        var ok = await _service.DeleteRecommendationAsync(id);

        // Non-existent recommendation IDs are reported as 404.
        if (!ok) return NotFound();

        // Successful delete does not need a response payload.
        return NoContent();
    }

    /// <summary>
    /// Removes all saved recommendation history for one playlist while leaving the imported
    /// playlist and its track metadata available for future suggestions.
    /// </summary>
    [HttpDelete("playlist/{playlistId:int}")]
    public async Task<IActionResult> DeletePlaylistHistory(int playlistId)
    {
        // Delete every recommendation row for the playlist and return how many rows were removed.
        var count = await _service.DeletePlaylistHistoryAsync(playlistId);
        return Ok(new { deleted = count });
    }
}

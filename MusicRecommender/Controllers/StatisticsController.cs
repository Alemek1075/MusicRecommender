using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Services;

namespace MusicRecommender.Controllers;

/// <summary>
/// Provides aggregate library statistics for either the whole imported collection or a selected
/// subset of playlists. The frontend uses this endpoint for dashboard cards and playlist detail
/// summaries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StatisticsController : ControllerBase
{
    private readonly PlaylistProcessingService _service;

    /// <summary>
    /// Receives the shared processing service so statistics are calculated with the same data
    /// access rules used by playlist import and recommendation endpoints.
    /// </summary>
    public StatisticsController(PlaylistProcessingService service) => _service = service;

    /// <summary>
    /// Returns top genre, top artist, track count, and total duration. playlistIds can be repeated
    /// in the query string; when omitted, the method summarizes every imported playlist.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] List<int>? playlistIds)
    {
        // Null or empty playlistIds means aggregate across the entire imported library.
        var stats = await _service.GetStatisticsAsync(playlistIds);

        // Always return a stats object; empty libraries produce zero/null values.
        return Ok(stats);
    }
}

using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Services;

namespace MusicRecommender.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecommendationsController : ControllerBase
{
    private readonly PlaylistProcessingService _service;

    public RecommendationsController(PlaylistProcessingService service) => _service = service;

    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] int playlistId, [FromQuery] List<int>? selectedTrackNumbers)
    {
        try
        {
            var result = await _service.GenerateAsync(playlistId, selectedTrackNumbers ?? []);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var history = await _service.GetHistoryAsync();
        return Ok(history);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await _service.DeleteRecommendationAsync(id);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpDelete("playlist/{playlistId:int}")]
    public async Task<IActionResult> DeletePlaylistHistory(int playlistId)
    {
        var count = await _service.DeletePlaylistHistoryAsync(playlistId);
        return Ok(new { deleted = count });
    }
}

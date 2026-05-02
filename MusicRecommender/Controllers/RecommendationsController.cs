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
}

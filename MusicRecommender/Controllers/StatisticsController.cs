using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Services;

namespace MusicRecommender.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatisticsController : ControllerBase
{
    private readonly PlaylistProcessingService _service;

    public StatisticsController(PlaylistProcessingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var stats = await _service.GetStatisticsAsync();
        return Ok(stats);
    }
}

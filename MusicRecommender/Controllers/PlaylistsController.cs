using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Services;

namespace MusicRecommender.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private readonly PlaylistProcessingService _service;

    public PlaylistsController(PlaylistProcessingService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] PlaylistSubmitDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest("Url is required.");

        try
        {
            var result = await _service.ProcessAsync(dto.Url);
            return Ok(result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var playlists = await _service.GetPlaylistsAsync();
        return Ok(playlists);
    }

    [HttpGet("{id:int}/tracks")]
    public async Task<IActionResult> GetTracks(int id, [FromQuery] int? trackNumber = null)
    {
        var tracks = await _service.GetTracksAsync(id, trackNumber);
        if (tracks is null)
            return NotFound();
        return Ok(tracks);
    }
}

public record PlaylistSubmitDto(string Url);

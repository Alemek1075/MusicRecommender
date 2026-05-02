using Microsoft.AspNetCore.Mvc;
using MusicRecommender.Data;
using MusicRecommender.Models;

namespace MusicRecommender.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PlaylistsController(AppDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] PlaylistSubmitDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest("Url is required.");

        var playlist = new Playlist { ExternalUrl = dto.Url };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        var tracks = new List<TrackMetadata>
        {
            new() { TrackName = "Bohemian Rhapsody", ArtistName = "Queen", Genre = "Rock", PlaylistId = playlist.Id },
            new() { TrackName = "Blinding Lights", ArtistName = "The Weeknd", Genre = "Pop", PlaylistId = playlist.Id },
            new() { TrackName = "Lose Yourself", ArtistName = "Eminem", Genre = "Hip-Hop", PlaylistId = playlist.Id },
            new() { TrackName = "Hotel California", ArtistName = "Eagles", Genre = "Rock", PlaylistId = playlist.Id },
            new() { TrackName = "Shape of You", ArtistName = "Ed Sheeran", Genre = "Pop", PlaylistId = playlist.Id },
        };

        _db.TrackMetadatas.AddRange(tracks);
        await _db.SaveChangesAsync();

        return Ok(new { playlist.Id, playlist.ExternalUrl, playlist.ProcessedAt, TrackCount = tracks.Count });
    }
}

public record PlaylistSubmitDto(string Url);

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;

namespace MusicRecommender.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatisticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatisticsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var tracks = await _db.TrackMetadatas.ToListAsync();

        if (tracks.Count == 0)
            return Ok(new { TopGenre = (string?)null, TopArtist = (string?)null });

        var topGenre = tracks
            .Where(t => t.Genre != null)
            .GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var topArtist = tracks
            .GroupBy(t => t.ArtistName)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        return Ok(new { TopGenre = topGenre, TopArtist = topArtist });
    }
}

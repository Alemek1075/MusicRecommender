using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Models;

namespace MusicRecommender.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecommendationsController : ControllerBase
{
    private readonly AppDbContext _db;

    private static readonly (string Track, string Artist)[] SpotifyMock =
    [
        ("Thunderstruck", "AC/DC"),
        ("Starboy", "The Weeknd"),
        ("God's Plan", "Drake"),
        ("Yellow", "Coldplay"),
        ("Smells Like Teen Spirit", "Nirvana"),
        ("Levitating", "Dua Lipa"),
        ("Blueberry Faygo", "Lil Mosey"),
    ];

    public RecommendationsController(AppDbContext db) => _db = db;

    [HttpPost("generate")]
    public async Task<IActionResult> Generate()
    {
        var pick = SpotifyMock[Random.Shared.Next(SpotifyMock.Length)];

        var recommendation = new Recommendation
        {
            SuggestedTrackName = pick.Track,
            SuggestedArtist = pick.Artist,
        };

        _db.Recommendations.Add(recommendation);
        await _db.SaveChangesAsync();

        return Ok(recommendation);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var history = await _db.Recommendations
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(history);
    }

    [HttpPost("{id:int}/favorite")]
    public async Task<IActionResult> MarkFavorite(int id)
    {
        var recommendation = await _db.Recommendations.FindAsync(id);
        if (recommendation is null)
            return NotFound();

        recommendation.IsMarkedAsFavorite = true;
        await _db.SaveChangesAsync();

        return Ok(recommendation);
    }
}

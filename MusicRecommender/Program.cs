using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Services;

var builder = WebApplication.CreateBuilder(args); // Create the ASP.NET Core host/application builder.

// Register the EF Core context against the configured PostgreSQL connection. All controllers use
// this scoped context through PlaylistProcessingService.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))); // Bind EF Core to PostgreSQL.

// MusicBrainz requires a descriptive User-Agent and can be slow or unavailable, so this named
// client keeps genre lookups polite and bounded by a short timeout.
builder.Services.AddHttpClient("musicbrainz", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicRecommender/1.0 (+musicrecommender)"); // Identify app to MusicBrainz.
    client.Timeout = TimeSpan.FromSeconds(10); // Prevent genre lookup from hanging imports.
});

// Central application service containing playlist import, statistics, history, and recommendation
// logic. Scoped lifetime matches the DbContext lifetime.
builder.Services.AddScoped<PlaylistProcessingService>();

// Ignore reference cycles because Playlist -> Tracks -> Playlist navigation properties are useful
// in EF but should not make JSON serialization recurse forever.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles); // Avoid Playlist/Track cycles.

// Swagger is enabled for local API discovery and quick manual endpoint testing.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build(); // Materialize the configured web app.

// Apply pending migrations on startup. The retry loop gives the database container a chance to
// finish accepting connections during docker-compose startup.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // Resolve DbContext in a startup scope.
    var retries = 10; // Allow database startup lag in Docker/local environments.
    while (true)
    {
        try
        {
            dbContext.Database.Migrate(); // Apply any pending EF migrations.
            break; // Stop retrying after migration succeeds.
        }
        catch (Exception) when (retries-- > 0) // Retry only while attempts remain.
        {
            Thread.Sleep(2000); // Wait before trying the database again.
        }
    }
}

// API documentation UI remains available in development-style deployments for easier debugging.
app.UseSwagger(); // Serve generated OpenAPI JSON.
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MusicRecommender v1")); // Serve Swagger UI.

// The API is controller-based; HTTPS redirection and authorization middleware are registered even
// though this app currently has no authenticated endpoints.
app.UseHttpsRedirection(); // Redirect HTTP requests to HTTPS when possible.
app.UseAuthorization(); // Keep authorization middleware in the standard pipeline.
app.MapControllers(); // Map attribute-routed API controllers.

// Start the ASP.NET Core request pipeline.
app.Run(); // Start listening for HTTP requests.

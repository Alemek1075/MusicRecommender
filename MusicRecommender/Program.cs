using Microsoft.EntityFrameworkCore;
using MusicRecommender.Data;
using MusicRecommender.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient("musicbrainz", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicRecommender/1.0 (+musicrecommender)");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<PlaylistProcessingService>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var retries = 10;
    while (true)
    {
        try
        {
            dbContext.Database.Migrate();
            break;
        }
        catch (Exception) when (retries-- > 0)
        {
            Thread.Sleep(2000);
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MusicRecommender v1"));

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

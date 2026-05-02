using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicRecommender.Migrations
{
    public partial class AddTrackDuration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "TrackMetadatas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "TrackMetadatas");
        }
    }
}

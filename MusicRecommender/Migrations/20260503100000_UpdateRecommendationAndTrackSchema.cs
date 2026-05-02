using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicRecommender.Migrations
{
    public partial class UpdateRecommendationAndTrackSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMarkedAsFavorite",
                table: "Recommendations");

            migrationBuilder.AddColumn<string>(
                name: "FavoriteTrackNumbers",
                table: "Recommendations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlaylistId",
                table: "Recommendations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrackNumber",
                table: "TrackMetadatas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FavoriteTrackNumbers",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "PlaylistId",
                table: "Recommendations");

            migrationBuilder.AddColumn<bool>(
                name: "IsMarkedAsFavorite",
                table: "Recommendations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.DropColumn(
                name: "TrackNumber",
                table: "TrackMetadatas");
        }
    }
}

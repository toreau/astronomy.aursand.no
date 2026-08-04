using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace S09Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class V2AddSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "SatelliteElements",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "SatelliteElements");
        }
    }
}

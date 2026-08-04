using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.Modules.Satellites.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Elements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatasetVersion = table.Column<string>(type: "TEXT", nullable: false),
                    NoradId = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectName = table.Column<string>(type: "TEXT", nullable: false),
                    EpochUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ElementsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Elements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Elements_DatasetVersion_NoradId",
                table: "Elements",
                columns: new[] { "DatasetVersion", "NoradId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Elements");
        }
    }
}

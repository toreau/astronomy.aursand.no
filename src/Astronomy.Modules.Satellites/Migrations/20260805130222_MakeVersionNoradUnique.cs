using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.Modules.Satellites.Migrations
{
    /// <inheritdoc />
    public partial class MakeVersionNoradUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Elements_DatasetVersion_NoradId",
                table: "Elements");

            // Dedupe rows staged before the unique constraint existed (same-day
            // re-stages could have inserted duplicates).
            migrationBuilder.Sql(
                "DELETE FROM Elements WHERE Id NOT IN (SELECT MIN(Id) FROM Elements GROUP BY DatasetVersion, NoradId);");

            migrationBuilder.CreateIndex(
                name: "IX_Elements_DatasetVersion_NoradId",
                table: "Elements",
                columns: new[] { "DatasetVersion", "NoradId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Elements_DatasetVersion_NoradId",
                table: "Elements");

            migrationBuilder.CreateIndex(
                name: "IX_Elements_DatasetVersion_NoradId",
                table: "Elements",
                columns: new[] { "DatasetVersion", "NoradId" });
        }
    }
}

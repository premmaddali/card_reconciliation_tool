using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class SchemeOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchemeProfileId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchemeProfileId",
                table: "ImportBatches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_SchemeProfileId",
                table: "ReconRuns",
                column: "SchemeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_SchemeProfileId",
                table: "ImportBatches",
                column: "SchemeProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_SchemeProfileId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_SchemeProfileId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "SchemeProfileId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "SchemeProfileId",
                table: "ImportBatches");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations
{
    /// <inheritdoc />
    public partial class NetworkToAtmRecon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "GlBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "NetworkBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconRuns_NetworkBatchId",
                table: "ReconRuns",
                column: "NetworkBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReconRuns_ImportBatches_NetworkBatchId",
                table: "ReconRuns",
                column: "NetworkBatchId",
                principalTable: "ImportBatches",
                principalColumn: "ImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReconRuns_ImportBatches_NetworkBatchId",
                table: "ReconRuns");

            migrationBuilder.DropIndex(
                name: "IX_ReconRuns_NetworkBatchId",
                table: "ReconRuns");

            migrationBuilder.DropColumn(
                name: "NetworkBatchId",
                table: "ReconRuns");

            migrationBuilder.AlterColumn<int>(
                name: "GlBatchId",
                table: "ReconRuns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recon.Web.Migrations;

public partial class AmountToleranceRunParameters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "ApplyAmountTolerance", table: "ReconRuns", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<decimal>(name: "AmountTolerance", table: "ReconRuns", type: "decimal(19,4)", nullable: true);
        migrationBuilder.CreateTable(
            name: "ReconParameters",
            columns: table => new
            {
                ReconParametersId = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AmountTolerance = table.Column<decimal>(type: "decimal(19,4)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ReconParameters", x => x.ReconParametersId));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ReconParameters");
        migrationBuilder.DropColumn(name: "ApplyAmountTolerance", table: "ReconRuns");
        migrationBuilder.DropColumn(name: "AmountTolerance", table: "ReconRuns");
    }
}

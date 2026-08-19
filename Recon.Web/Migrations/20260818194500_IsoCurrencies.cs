using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Recon.Web.Data;

#nullable disable

namespace Recon.Web.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260818194500_IsoCurrencies")]
public partial class IsoCurrencies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IsoCurrencies",
            columns: table => new
            {
                NumericCode = table.Column<int>(type: "int", nullable: false),
                AlphaCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_IsoCurrencies", x => x.NumericCode));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "IsoCurrencies");
}

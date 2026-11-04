using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tryum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUnitsAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributesJson",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnitsJson",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VariantsJson",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttributesJson",
                table: "products");

            migrationBuilder.DropColumn(
                name: "UnitsJson",
                table: "products");

            migrationBuilder.DropColumn(
                name: "VariantsJson",
                table: "products");
        }
    }
}

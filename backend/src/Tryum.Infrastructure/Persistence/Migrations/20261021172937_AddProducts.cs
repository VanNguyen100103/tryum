using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tryum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Brand = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    SalePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CostPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    SourceImageUrlsJson = table.Column<string>(type: "jsonb", nullable: false),
                    R2ObjectKeysJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_barcode",
                table: "products",
                columns: new[] { "TenantId", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_name",
                table: "products",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_sku",
                table: "products",
                columns: new[] { "TenantId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");
        }
    }
}

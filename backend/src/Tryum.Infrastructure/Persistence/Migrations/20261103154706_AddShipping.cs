using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tryum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoshipCityId",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoshipDistrictId",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoshipWardId",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCityId",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCityName",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryDistrictId",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryDistrictName",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryWardId",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryWardName",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientPhone",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCarrier",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingExpected",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFee",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ShippingRateId",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingService",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingStatus",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingStatusText",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoshipCityId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "GoshipDistrictId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "GoshipWardId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "DeliveryCityId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryCityName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryDistrictId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryDistrictName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryWardId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryWardName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RecipientPhone",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingCarrier",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingExpected",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingRateId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingService",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingStatus",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingStatusText",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "orders");
        }
    }
}

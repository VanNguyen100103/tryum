using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Tryum.Api.Redis;
using Tryum.Application.Common.Security;
using Tryum.Domain.Sales;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Shipping;

/// <summary>Địa chỉ lấy hàng, bảng giá và vận đơn Goship cho POS Bán giao hàng.</summary>
public static class ShippingEndpoints
{
    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shipping").WithTags("Shipping")
            .RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        group.MapGet("/pickup", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var tenant = await CurrentTenantAsync(db, ct);
            return tenant is null
                ? Results.NotFound(new { message = "Không tìm thấy cửa hàng." })
                : Results.Ok(PickupResponse(tenant));
        });

        group.MapPut("/pickup", async (PickupRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var tenant = await CurrentTenantAsync(db, ct);
            if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
            tenant.GoshipCityId = Clean(request.CityId);
            tenant.GoshipDistrictId = Clean(request.DistrictId);
            tenant.GoshipWardId = Clean(request.WardId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(PickupResponse(tenant));
        }).WithSummary("Lưu điểm lấy hàng Goship của cửa hàng");

        group.MapGet("/cities", async (GoshipClient client, RedisCache cache, CancellationToken ct) =>
            await ProxyAsync(async () => Results.Ok(new
            {
                cities = await cache.GetOrSetAsync(
                    "goship:cities",
                    TimeSpan.FromHours(24),
                    () => client.CitiesAsync(ct)),
            })));

        group.MapGet("/districts", async (string cityId, GoshipClient client, RedisCache cache, CancellationToken ct) =>
            await ProxyAsync(async () => Results.Ok(new
            {
                districts = await cache.GetOrSetAsync(
                    $"goship:districts:{cityId}",
                    TimeSpan.FromHours(24),
                    () => client.DistrictsAsync(cityId, ct)),
            })));

        group.MapGet("/wards", async (string districtId, GoshipClient client, RedisCache cache, CancellationToken ct) =>
            await ProxyAsync(async () => Results.Ok(new
            {
                wards = await cache.GetOrSetAsync(
                    $"goship:wards:{districtId}",
                    TimeSpan.FromHours(24),
                    () => client.WardsAsync(districtId, ct)),
            })));

        group.MapPost("/rates", async (RateRequest request, ApplicationDbContext db, GoshipClient client, CancellationToken ct) =>
        {
            var tenant = await CurrentTenantAsync(db, ct);
            if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
            if (string.IsNullOrWhiteSpace(tenant.GoshipCityId) || string.IsNullOrWhiteSpace(tenant.GoshipDistrictId))
                return Results.BadRequest(new { message = "Chưa cấu hình điểm lấy hàng. Chọn Tỉnh/Quận/Phường của cửa hàng trước." });

            var cod = request.CodAmount ?? 0;
            var declared = request.DeclaredAmount ?? cod;
            var body = new
            {
                shipment = new
                {
                    address_from = new { city = tenant.GoshipCityId, district = tenant.GoshipDistrictId },
                    address_to = new { city = request.ToCityId, district = request.ToDistrictId },
                    parcel = new
                    {
                        cod,
                        amount = declared,
                        weight = request.WeightGrams,
                        length = request.LengthCm ?? 20,
                        width = request.WidthCm ?? 20,
                        height = request.HeightCm ?? 10,
                    },
                },
            };

            return await ProxyAsync(async () =>
            {
                var data = await client.RatesAsync(body, ct);
                var rates = ParseRates(data);
                return Results.Ok(new { rates });
            });
        }).WithSummary("Bảng giá tất cả hãng vận chuyển cho một tuyến");

        group.MapPost("/shipments", async (
            CreateShipmentRequest request,
            ApplicationDbContext db,
            GoshipClient client,
            HttpContext http,
            CancellationToken ct) =>
        {
            var tenant = await CurrentTenantAsync(db, ct);
            if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
            if (string.IsNullOrWhiteSpace(tenant.GoshipCityId) || string.IsNullOrWhiteSpace(tenant.GoshipDistrictId)
                || string.IsNullOrWhiteSpace(tenant.GoshipWardId))
                return Results.BadRequest(new { message = "Chưa cấu hình điểm lấy hàng Goship." });

            var cod = request.CodAmount ?? 0;
            var declared = request.DeclaredAmount ?? 0;
            var body = new
            {
                shipment = new
                {
                    rate = request.RateId,
                    payer = request.SenderPaysShipping == false ? 0 : 1,
                    order_id = request.OrderId?.ToString() ?? $"TT{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    is_recall = 0,
                    address_from = new
                    {
                        name = tenant.Name,
                        phone = tenant.Phone ?? string.Empty,
                        street = tenant.Address ?? string.Empty,
                        ward = tenant.GoshipWardId,
                        district = tenant.GoshipDistrictId,
                        city = tenant.GoshipCityId,
                    },
                    address_to = new
                    {
                        name = request.ToName.Trim(),
                        phone = request.ToPhone.Trim(),
                        street = request.ToAddress.Trim(),
                        ward = request.ToWardId,
                        district = request.ToDistrictId,
                        city = request.ToCityId,
                    },
                    parcel = new
                    {
                        cod,
                        amount = declared,
                        weight = request.WeightGrams,
                        length = request.LengthCm ?? 20,
                        width = request.WidthCm ?? 20,
                        height = request.HeightCm ?? 10,
                        metadata = string.IsNullOrWhiteSpace(request.Note) ? "Không cho xem hàng" : "Không cho xem hàng - " + request.Note,
                    },
                },
            };

            return await ProxyAsync(async () =>
            {
                var data = await client.CreateShipmentAsync(body, ct);
                var shipment = new
                {
                    id = Text(data, "id"),
                    trackingNumber = Text(data, "tracking_number"),
                    carrier = Text(data, "carrier"),
                    carrierShortName = Text(data, "carrier_short_name"),
                    fee = Number(data, "fee"),
                    status = Int(data, "shipment_status"),
                    statusText = Text(data, "shipment_status_txt"),
                };

                if (request.OrderId is { } orderId)
                {
                    var order = await db.Orders.FirstOrDefaultAsync(row => row.Id == orderId, ct);
                    if (order is not null)
                    {
                        order.ShippingCarrier = shipment.carrier ?? shipment.carrierShortName;
                        order.TrackingNumber = shipment.trackingNumber;
                        order.ShippingRateId = request.RateId;
                        order.ShippingService = request.Service;
                        order.ShippingExpected = request.Expected;
                        order.ShippingFee = shipment.fee;
                        order.ShippingStatus = shipment.status;
                        order.ShippingStatusText = shipment.statusText;
                        order.DeliveryCityId = request.ToCityId;
                        order.DeliveryDistrictId = request.ToDistrictId;
                        order.DeliveryWardId = request.ToWardId;
                        order.DeliveryCityName = request.ToCityName;
                        order.DeliveryDistrictName = request.ToDistrictName;
                        order.DeliveryWardName = request.ToWardName;
                        order.RecipientName = request.ToName.Trim();
                        order.RecipientPhone = request.ToPhone.Trim();
                        order.DeliveryAddress = request.ToAddress.Trim();
                        await db.SaveChangesAsync(ct);
                    }
                }

                return Results.Ok(new { message = "Đã tạo vận đơn", shipment });
            });
        }).WithSummary("Tạo vận đơn Goship và gắn vào hóa đơn");

        return app;
    }

    private static async Task<IResult> ProxyAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (GoshipException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static List<RateResult> ParseRates(JsonNode data)
    {
        var items = data is JsonArray array ? array : new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rates = new List<RateResult>();
        foreach (var node in items)
        {
            var id = Text(node, "id");
            var carrier = Text(node, "carrier_short_name") ?? Text(node, "carrier_name") ?? string.Empty;
            var service = Text(node, "service") ?? string.Empty;
            var total = Number(node, "total_fee");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add($"{carrier}|{service}|{total}"))
                continue;
            rates.Add(new RateResult(
                id,
                Text(node, "carrier_name"),
                Text(node, "carrier_short_name"),
                Text(node, "carrier_logo"),
                service,
                Text(node, "expected"),
                total,
                Number(node, "cod_fee"),
                Number(node, "service_fee"),
                node?["report"]?["success_percent"]?.GetValue<double>()));
        }
        return rates.OrderBy(rate => rate.TotalFee).ToList();
    }

    private static async Task<Tryum.Domain.Tenants.Tenant?> CurrentTenantAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var tenantId = TenantContext.RequireCurrent();
        return await db.Tenants.FirstOrDefaultAsync(row => row.Id == tenantId, ct);
    }

    private static object PickupResponse(Tryum.Domain.Tenants.Tenant tenant) => new
    {
        name = tenant.Name,
        phone = tenant.Phone,
        street = tenant.Address,
        cityId = tenant.GoshipCityId,
        districtId = tenant.GoshipDistrictId,
        wardId = tenant.GoshipWardId,
    };

    private static string? Text(JsonNode? node, string field)
    {
        var value = node?[field];
        return value is null ? null : value.ToString();
    }

    private static decimal Number(JsonNode? node, string field)
    {
        var value = node?[field];
        if (value is null) return 0;
        try { return value.GetValue<decimal>(); }
        catch { return decimal.TryParse(value.ToString(), out var parsed) ? parsed : 0; }
    }

    private static int? Int(JsonNode? node, string field)
    {
        var value = node?[field];
        if (value is null) return null;
        try { return value.GetValue<int>(); }
        catch { return int.TryParse(value.ToString(), out var parsed) ? parsed : null; }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record PickupRequest(string? CityId, string? DistrictId, string? WardId);

    private sealed record RateResult(
        string Id,
        string? CarrierName,
        string? CarrierShortName,
        string? CarrierLogo,
        string Service,
        string? Expected,
        decimal TotalFee,
        decimal CodFee,
        decimal ServiceFee,
        double? SuccessPercent);
    public sealed record RateRequest(
        string ToCityId,
        string ToDistrictId,
        int WeightGrams,
        int? LengthCm,
        int? WidthCm,
        int? HeightCm,
        decimal? CodAmount,
        decimal? DeclaredAmount);
    public sealed record CreateShipmentRequest(
        Guid? OrderId,
        string RateId,
        string ToName,
        string ToPhone,
        string ToAddress,
        string ToCityId,
        string? ToCityName,
        string ToDistrictId,
        string? ToDistrictName,
        string ToWardId,
        string? ToWardName,
        int WeightGrams,
        int? LengthCm,
        int? WidthCm,
        int? HeightCm,
        decimal? CodAmount,
        decimal? DeclaredAmount,
        bool? SenderPaysShipping,
        string? Service,
        string? Expected,
        string? Note);
}

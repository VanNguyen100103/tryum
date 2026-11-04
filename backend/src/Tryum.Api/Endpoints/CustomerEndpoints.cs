using Microsoft.EntityFrameworkCore;
using Tryum.Application.Common.Security;
using Tryum.Domain.Customers;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").WithTags("Customers")
            .RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        group.MapGet("", async (int? page, int? pageSize, string? search, ApplicationDbContext db, CancellationToken ct) =>
        {
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var query = db.Customers.AsNoTracking();
            var term = search?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(customer => customer.Name.Contains(term)
                    || customer.Code.Contains(term)
                    || (customer.Phone != null && customer.Phone.Contains(term))
                    || (customer.Email != null && customer.Email.Contains(term)));

            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(customer => customer.Name)
                .Skip((currentPage - 1) * size).Take(size)
                .ToListAsync(ct);
            var items = rows.Select(ToResponse).ToList();
            return Results.Ok(new { items, total, page = currentPage, pageSize = size });
        }).WithSummary("Tìm khách hàng trong cửa hàng đang đăng nhập");

        group.MapGet("/{customerId:guid}/history", async (
            Guid customerId,
            int? page,
            int? pageSize,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == customerId, ct);
            if (customer is null)
                return Results.NotFound(new { message = "Không tìm thấy khách hàng." });

            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 10, 1, 100);
            var query = db.Orders.AsNoTracking().Where(order => order.CustomerId == customerId);
            var orderCount = await query.CountAsync(ct);
            var totalSales = await query
                .Where(order => order.Status != Tryum.Domain.Sales.OrderStatus.Cancelled)
                .SumAsync(order => (decimal?)order.Total, ct) ?? 0;
            var orders = await query
                .OrderByDescending(order => order.CreatedAt)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .Select(order => new
                {
                    id = order.Id,
                    code = order.Code,
                    createdAt = order.CreatedAt,
                    soldByName = order.SoldByName,
                    total = order.Total,
                    paidAmount = order.PaidAmount,
                    status = order.Status.ToString(),
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                customer = ToResponse(customer),
                orders,
                total = orderCount,
                page = currentPage,
                pageSize = size,
                totalSales,
            });
        })
        .WithSummary("Lịch sử bán hàng và dư nợ của khách hàng")
        .Produces(StatusCodes.Status200OK);

        group.MapPost("", async (CreateCustomerRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { message = "Vui lòng nhập tên khách hàng." });
            if (name.Length > 200 || request.Phone?.Length > 40 || request.Email?.Length > 254)
                return Results.BadRequest(new { message = "Một số thông tin khách hàng vượt quá độ dài cho phép." });
            if (!string.IsNullOrWhiteSpace(request.Email) && !System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
                return Results.BadRequest(new { message = "Địa chỉ email không hợp lệ." });

            var customer = new Customer
            {
                Code = $"KH{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 1000)}",
                Name = name,
                Phone = Clean(request.Phone),
                Email = Clean(request.Email),
                Address = Clean(request.Address),
                Region = Clean(request.Region),
                Ward = Clean(request.Ward),
                GroupName = Clean(request.GroupName),
                BirthDate = request.BirthDate,
                Gender = Clean(request.Gender),
                Facebook = Clean(request.Facebook),
                AssignedTo = Clean(request.AssignedTo),
                Note = Clean(request.Note),
                CompanyName = Clean(request.CompanyName),
                TaxCode = Clean(request.TaxCode),
                InvoiceAddress = Clean(request.InvoiceAddress),
            };

            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/customers/{customer.Id}", ToResponse(customer));
        }).WithSummary("Tạo khách hàng cho cửa hàng đang đăng nhập");

        return app;
    }

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.Code, customer.Name, customer.Phone, customer.Email, customer.Address,
        customer.Region, customer.Ward, customer.GroupName, customer.BirthDate, customer.Gender,
        customer.Facebook, customer.AssignedTo, customer.Note, customer.CompanyName,
        customer.TaxCode, customer.InvoiceAddress, customer.Points, customer.DebtAmount);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record CreateCustomerRequest(
        string? Name, string? Phone, string? Email, string? Address, string? Region, string? Ward,
        string? GroupName, DateOnly? BirthDate, string? Gender, string? Facebook, string? AssignedTo,
        string? Note, string? CompanyName, string? TaxCode, string? InvoiceAddress);

    public sealed record CustomerResponse(
        Guid Id, string Code, string Name, string? Phone, string? Email, string? Address,
        string? Region, string? Ward, string? GroupName, DateOnly? BirthDate, string? Gender,
        string? Facebook, string? AssignedTo, string? Note, string? CompanyName,
        string? TaxCode, string? InvoiceAddress, int Points, decimal DebtAmount);
}

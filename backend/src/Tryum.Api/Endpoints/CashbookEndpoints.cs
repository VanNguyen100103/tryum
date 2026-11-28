using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Tryum.Application.Common.Security;
using Tryum.Domain.Cashbook;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>
/// API sổ quỹ: danh sách phiếu thu/chi, tạo phiếu, và tổng kết số dư quỹ.
/// </summary>
public static class CashbookEndpoints
{
    public static IEndpointRouteBuilder MapCashbookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cashbook").WithTags("Cashbook")
            .RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        group.MapGet("", async (
            int? page,
            int? pageSize,
            string? type,
            string? method,
            DateOnly? from,
            DateOnly? to,
            string? search,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var query = db.CashTransactions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(type)
                && Enum.TryParse<CashflowType>(type, ignoreCase: true, out var parsedType))
            {
                query = query.Where(transaction => transaction.Type == parsedType);
            }
            if (!string.IsNullOrWhiteSpace(method)
                && Enum.TryParse<CashflowMethod>(method, ignoreCase: true, out var parsedMethod))
            {
                query = query.Where(transaction => transaction.Method == parsedMethod);
            }
            if (from is not null)
                query = query.Where(transaction => transaction.TransactedAt >= StartOfVietnamDay(from.Value));
            if (to is not null)
                query = query.Where(transaction => transaction.TransactedAt < StartOfVietnamDay(to.Value.AddDays(1)));

            var term = search?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(transaction => transaction.Code.Contains(term)
                    || (transaction.PartnerName != null && transaction.PartnerName.Contains(term))
                    || (transaction.ReferenceCode != null && transaction.ReferenceCode.Contains(term))
                    || (transaction.Note != null && transaction.Note.Contains(term)));

            var total = await query.CountAsync(ct);
            var income = await query
                .Where(transaction => transaction.Type == CashflowType.Income)
                .SumAsync(transaction => (decimal?)transaction.Amount, ct) ?? 0;
            var expense = await query
                .Where(transaction => transaction.Type == CashflowType.Expense)
                .SumAsync(transaction => (decimal?)transaction.Amount, ct) ?? 0;

            var items = await query
                .OrderByDescending(transaction => transaction.TransactedAt)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .Select(transaction => new CashTransactionItem(
                    transaction.Id,
                    transaction.Code,
                    transaction.Type,
                    transaction.Method,
                    transaction.Amount,
                    transaction.Category,
                    transaction.PartnerName,
                    transaction.ReferenceCode,
                    transaction.Note,
                    transaction.TransactedAt,
                    transaction.CreatedByName))
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                total,
                page = currentPage,
                pageSize = size,
                summary = new
                {
                    income,
                    expense,
                    balance = Math.Round(income - expense, 2, MidpointRounding.AwayFromZero),
                },
            });
        }).WithSummary("Danh sách phiếu thu/chi + tổng kết số dư quỹ");

        group.MapPost("", async (
            CreateCashTransactionRequest request,
            HttpContext http,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<CashflowType>(request.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new { message = "Loại phiếu không hợp lệ (Income/Expense)." });
            if (!Enum.TryParse<CashflowMethod>(request.Method, ignoreCase: true, out var method))
                return Results.BadRequest(new { message = "Phương thức thanh toán không hợp lệ." });

            var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0)
                return Results.BadRequest(new { message = "Số tiền phải lớn hơn 0." });
            if (amount > 1_000_000_000)
                return Results.BadRequest(new { message = "Số tiền vượt quá giới hạn cho phép." });

            var category = Clean(request.Category);
            if (string.IsNullOrWhiteSpace(category))
                return Results.BadRequest(new { message = "Vui lòng chọn nhóm thu/chi." });
            if (category.Length > 160)
                return Results.BadRequest(new { message = "Nhóm thu/chi quá dài." });

            var transactedAt = request.TransactedAt ?? DateTimeOffset.UtcNow;
            var code = await NextCodeAsync(db, type, ct);

            var transaction = new CashTransaction
            {
                Code = code,
                Type = type,
                Method = method,
                Amount = amount,
                Category = category,
                PartnerName = Clean(request.PartnerName),
                ReferenceCode = Clean(request.ReferenceCode),
                Note = Clean(request.Note),
                TransactedAt = transactedAt,
                CreatedByUserId = Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
                CreatedByName = http.User.FindFirstValue(ClaimTypes.Name),
            };

            db.CashTransactions.Add(transaction);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/cashbook/{transaction.Id}", ToItem(transaction));
        }).WithSummary("Tạo phiếu thu/chi mới");

        return app;
    }

    private static async Task<string> NextCodeAsync(ApplicationDbContext db, CashflowType type, CancellationToken ct)
    {
        var next = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('cashflow_code_seq') AS \"Value\"")
            .SingleAsync(ct);
        return type == CashflowType.Income ? $"PT{next:D6}" : $"PC{next:D6}";
    }

    private static DateTimeOffset StartOfVietnamDay(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CashTransactionItem ToItem(CashTransaction transaction) => new(
        transaction.Id,
        transaction.Code,
        transaction.Type,
        transaction.Method,
        transaction.Amount,
        transaction.Category,
        transaction.PartnerName,
        transaction.ReferenceCode,
        transaction.Note,
        transaction.TransactedAt,
        transaction.CreatedByName);

    public sealed record CreateCashTransactionRequest(
        string Type,
        string Method,
        decimal Amount,
        string? Category,
        string? PartnerName,
        string? ReferenceCode,
        string? Note,
        DateTimeOffset? TransactedAt);

    public sealed record CashTransactionItem(
        Guid Id,
        string Code,
        CashflowType Type,
        CashflowMethod Method,
        decimal Amount,
        string? Category,
        string? PartnerName,
        string? ReferenceCode,
        string? Note,
        DateTimeOffset TransactedAt,
        string? CreatedByName);
}

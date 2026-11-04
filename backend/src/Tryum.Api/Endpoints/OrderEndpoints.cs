using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Security;
using Tryum.Domain.Customers;
using Tryum.Domain.Sales;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>
/// API bán hàng: tạo hóa đơn (trừ kho trong 1 transaction), danh sách hóa đơn,
/// thu thêm tiền (xác nhận chuyển khoản thủ công) và sinh mã VietQR.
/// </summary>
public static class OrderEndpoints
{
    /// <summary>Giới hạn số dòng / hóa đơn để tránh request quá lớn.</summary>
    private const int MaxLines = 200;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders").WithTags("Orders")
            .RequireAuthorization(AuthorizationPolicies.Authenticated);

        group.MapGet("", async (
            int? page,
            int? pageSize,
            string? search,
            string? status,
            DateOnly? from,
            DateOnly? to,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 30, 1, 100);
            var query = db.Orders.AsNoTracking();

            var term = search?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(order => order.Code.Contains(term)
                    || (order.CustomerName != null && order.CustomerName.Contains(term))
                    || (order.CustomerPhone != null && order.CustomerPhone.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(order => order.Status == parsedStatus);
            }

            if (from is not null)
                query = query.Where(order => order.CreatedAt >= StartOfVietnamDay(from.Value));
            if (to is not null)
                query = query.Where(order => order.CreatedAt < StartOfVietnamDay(to.Value.AddDays(1)));

            var total = await query.CountAsync(ct);
            var orders = await query
                .OrderByDescending(order => order.CreatedAt)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .Include(order => order.Lines)
                .Include(order => order.Payments)
                .ToListAsync(ct);

            var items = orders.Select(ToSummary).ToList();
            return Results.Ok(new { items, total, page = currentPage, pageSize = size });
        })
        .WithSummary("Danh sách hóa đơn (lọc theo ngày, trạng thái, mã/tên/SĐT khách)")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/{orderId:guid}", async (Guid orderId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking()
                .Include(row => row.Lines)
                .Include(row => row.Payments)
                .FirstOrDefaultAsync(row => row.Id == orderId, ct);
            return order is null
                ? Results.NotFound(new { message = "Không tìm thấy hóa đơn." })
                : Results.Ok(ToResponse(order));
        })
        .WithSummary("Chi tiết hóa đơn")
        .Produces<OrderResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
            CreateOrderRequest request,
            ApplicationDbContext db,
            HttpContext http,
            Tryum.Api.Messaging.RabbitMqPublisher rabbit,
            CancellationToken ct) =>
        {
            if (request.Lines is null || request.Lines.Count == 0)
                return Results.BadRequest(new { message = "Hóa đơn phải có ít nhất 1 sản phẩm." });
            if (request.Lines.Count > MaxLines)
                return Results.BadRequest(new { message = $"Mỗi hóa đơn tối đa {MaxLines} dòng hàng." });

            var duplicateProduct = request.Lines
                .GroupBy(line => line.ProductId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateProduct is not null)
                return Results.BadRequest(new { message = "Một sản phẩm đang bị lặp lại trong hóa đơn." });

            var channel = SaleChannel.Regular;
            if (!string.IsNullOrWhiteSpace(request.Channel)
                && !Enum.TryParse(request.Channel, ignoreCase: true, out channel))
            {
                return Results.BadRequest(new { message = "Kênh bán không hợp lệ." });
            }

            var productIds = request.Lines.Select(line => line.ProductId).ToArray();
            var products = await db.Products.AsNoTracking()
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, ct);
            if (products.Count != productIds.Length)
                return Results.BadRequest(new { message = "Có sản phẩm không tồn tại hoặc không thuộc cửa hàng này." });

            // Khách hàng (nếu chọn) - cần trước khi tính điểm/công nợ.
            Customer? customer = null;
            if (request.CustomerId is { } selectedCustomerId)
            {
                customer = await db.Customers.FirstOrDefaultAsync(row => row.Id == selectedCustomerId, ct);
                if (customer is null)
                    return Results.BadRequest(new { message = "Không tìm thấy khách hàng đã chọn." });
            }

            // ── Dựng snapshot dòng hàng + kiểm tra tồn kho ──────────────────────
            var lines = new List<OrderLine>(request.Lines.Count);
            foreach (var item in request.Lines)
            {
                var product = products[item.ProductId];
                if (item.Quantity <= 0 || item.Quantity > 1_000_000)
                    return Results.BadRequest(new { message = $"Số lượng của \"{product.Name}\" không hợp lệ." });

                var unitPrice = item.UnitPrice ?? product.SalePrice ?? 0;
                if (unitPrice < 0)
                    return Results.BadRequest(new { message = $"Đơn giá của \"{product.Name}\" không hợp lệ." });

                var gross = Round(unitPrice * item.Quantity);
                var lineDiscount = Math.Clamp(item.Discount ?? 0, 0, gross);
                var lineTotal = Round(gross - lineDiscount);

                lines.Add(new OrderLine
                {
                    TenantId = product.TenantId,
                    ProductId = product.Id,
                    Sku = product.Sku,
                    ProductName = product.Name,
                    UnitName = null,
                    UnitPrice = unitPrice,
                    UnitCost = product.CostPrice ?? 0,
                    Quantity = item.Quantity,
                    Discount = lineDiscount,
                    LineTotal = lineTotal,
                });
            }

            var subtotal = Round(lines.Sum(line => line.LineTotal));
            var orderDiscount = Math.Clamp(request.Discount ?? 0, 0, subtotal);

            // 1 điểm = 1.000đ; chỉ được dùng tối đa bằng số điểm khách có và số tiền còn lại.
            var requestedPoints = Math.Max(0, request.PointsToRedeem ?? 0);
            if (requestedPoints > 0 && customer is null)
                return Results.BadRequest(new { message = "Chọn khách hàng trước khi dùng điểm." });
            var maxRedeemable = (int)Math.Floor(Math.Max(0, subtotal - orderDiscount) / 1_000m);
            var pointsToRedeem = Math.Min(Math.Min(requestedPoints, customer?.Points ?? 0), maxRedeemable);
            var pointsDiscount = Round(pointsToRedeem * 1_000m);

            var total = Round(subtotal - orderDiscount - pointsDiscount + Math.Max(0, request.ShippingFee ?? 0));
            if (total < 0)
                return Results.BadRequest(new { message = "Tổng tiền hóa đơn không hợp lệ." });

            // 1 điểm cho mỗi 10.000đ khách thực trả.
            var pointsEarned = (int)Math.Floor(total / 10_000m);

            // ── Phương thức thanh toán ─────────────────────────────────────────
            var paymentDrafts = new List<Payment>();
            var linkedQrSessions = new List<PosQrSession>();
            var usedQrReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in request.Payments ?? [])
            {
                if (!Enum.TryParse<PaymentMethod>(item.Method, ignoreCase: true, out var method))
                    return Results.BadRequest(new { message = $"Phương thức thanh toán \"{item.Method}\" không hợp lệ." });
                if (item.Amount <= 0)
                    return Results.BadRequest(new { message = "Số tiền thanh toán phải lớn hơn 0." });

                var isCash = method == PaymentMethod.Cash;
                var reference = Clean(item.Reference);
                PosQrSession? qrSession = null;

                // QR tại quầy: dù phiên đã Paid hay còn Pending đều gắn vào hóa đơn.
                // Pending thì webhook sau đó sẽ xác nhận khoản chuyển khoản của đơn này.
                if (method == PaymentMethod.Transfer && !string.IsNullOrWhiteSpace(reference)
                    && !usedQrReferences.Contains(reference))
                {
                    qrSession = await db.PosQrSessions.FirstOrDefaultAsync(
                        session => session.Reference == reference
                            && session.OrderId == null
                            && (session.Status == PosQrStatus.Paid || session.Status == PosQrStatus.Pending),
                        ct);

                    // Chỉ gắn khi số tiền khớp; QR của đơn khác/đã đổi giỏ thì bỏ qua.
                    if (qrSession is not null && OrderEndpoints.Round(qrSession.Amount) != OrderEndpoints.Round(item.Amount))
                        qrSession = null;

                    if (qrSession is not null)
                    {
                        usedQrReferences.Add(reference);
                        linkedQrSessions.Add(qrSession);
                    }
                }

                var isConfirmed = method != PaymentMethod.Transfer || qrSession?.Status == PosQrStatus.Paid;
                paymentDrafts.Add(new Payment
                {
                    Method = method,
                    Status = isConfirmed ? PaymentStatus.Confirmed : PaymentStatus.Pending,
                    Amount = Round(item.Amount),
                    Provider = method == PaymentMethod.Transfer ? "SePay" : null,
                    ProviderTransactionId = qrSession?.Reference,
                    Reference = qrSession?.Reference ?? reference,
                    PaidAt = isConfirmed ? DateTimeOffset.UtcNow : null,
                    Note = null,
                });

                // Tiền mặt được phép thu vượt (trả lại tiền thừa); ví/thẻ/chuyển khoản thì không.
                if (!isCash && paymentDrafts.Where(payment => payment.Method != PaymentMethod.Cash)
                        .Sum(payment => payment.Amount) > total)
                {
                    return Results.BadRequest(new { message = "Số tiền thanh toán không được vượt quá tổng hóa đơn." });
                }
            }

            var paid = Round(paymentDrafts.Where(payment => payment.Status == PaymentStatus.Confirmed)
                .Sum(payment => payment.Amount));
            var overpay = Round(Math.Max(0, paid - total));
            var creditOverpay = request.CreditOverpayment == true && overpay > 0;
            if (creditOverpay && customer is null)
                return Results.BadRequest(new { message = "Chọn khách hàng để tính tiền thừa vào công nợ." });

            var order = new Order
            {
                Code = await NextOrderCodeAsync(db, ct),
                CustomerId = request.CustomerId,
                CustomerName = customer?.Name ?? Clean(request.CustomerName),
                CustomerPhone = customer?.Phone ?? Clean(request.CustomerPhone),
                SoldByUserId = Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
                SoldByName = http.User.FindFirstValue(ClaimTypes.Name),
                Channel = channel,
                DeliveryAddress = Clean(request.DeliveryAddress),
                RecipientName = Clean(request.RecipientName),
                RecipientPhone = Clean(request.RecipientPhone),
                DeliveryCityId = Clean(request.DeliveryCityId),
                DeliveryDistrictId = Clean(request.DeliveryDistrictId),
                DeliveryWardId = Clean(request.DeliveryWardId),
                DeliveryCityName = Clean(request.DeliveryCityName),
                DeliveryDistrictName = Clean(request.DeliveryDistrictName),
                DeliveryWardName = Clean(request.DeliveryWardName),
                ShippingFee = Math.Max(0, request.ShippingFee ?? 0),
                Note = Clean(request.Note),
                CouponCode = Clean(request.CouponCode),
                Subtotal = subtotal,
                Discount = orderDiscount,
                Total = total,
                PaidAmount = paid,
                ChangeAmount = creditOverpay ? 0 : overpay,
                PointsRedeemed = pointsToRedeem,
                PointsDiscount = pointsDiscount,
                PointsEarned = pointsEarned,
                Status = StatusFor(total, paid),
            };

            // Cập nhật điểm và công nợ của khách trong cùng transaction với hóa đơn.
            if (customer is not null)
            {
                customer.Points = customer.Points - pointsToRedeem + pointsEarned;
                if (creditOverpay)
                    customer.DebtAmount = Round(customer.DebtAmount - overpay);
            }

            foreach (var line in lines)
            {
                line.OrderId = order.Id;
                order.Lines.Add(line);
            }

            // Gắn phiên QR vào hóa đơn: Paid -> Used; Pending -> webhook sau sẽ xác nhận đơn.
            foreach (var qrSession in linkedQrSessions)
            {
                if (qrSession.Status == PosQrStatus.Paid) qrSession.Status = PosQrStatus.Used;
                qrSession.OrderId = order.Id;
            }

            foreach (var payment in paymentDrafts)
            {
                payment.OrderId = order.Id;
                order.Payments.Add(payment);
            }

            // ── Trừ kho + lưu hóa đơn trong 1 transaction ──────────────────────
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var line in order.Lines)
            {
                var affected = await db.Products
                    .Where(product => product.Id == line.ProductId
                        && (product.Stock == null || product.Stock >= line.Quantity))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(product => product.Stock, product => product.Stock - line.Quantity)
                        .SetProperty(product => product.UpdatedAt, DateTimeOffset.UtcNow), ct);

                if (affected == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return Results.BadRequest(new
                    {
                        message = $"\"{line.ProductName}\" không đủ tồn kho để bán {line.Quantity:0.###}.",
                    });
                }
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await rabbit.PublishAsync("order.created", new
            {
                tenantId = order.TenantId,
                orderId = order.Id,
                code = order.Code,
                order.Total,
                channel = order.Channel.ToString(),
                order.CustomerId,
                createdAt = order.CreatedAt,
            }, ct);

            return Results.Created($"/api/v1/orders/{order.Id}", ToResponse(order));
        })
        .WithSummary("Tạo hóa đơn: trừ kho, ghi nhận thanh toán nhiều phương thức, chặn bán âm kho")
        .Produces<OrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{orderId:guid}/payments", async (
            Guid orderId,
            AddPaymentRequest request,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
                return Results.BadRequest(new { message = "Phương thức thanh toán không hợp lệ." });
            if (request.Amount <= 0)
                return Results.BadRequest(new { message = "Số tiền thanh toán phải lớn hơn 0." });

            var order = await db.Orders.Include(row => row.Payments)
                .FirstOrDefaultAsync(row => row.Id == orderId, ct);
            if (order is null)
                return Results.NotFound(new { message = "Không tìm thấy hóa đơn." });
            if (order.Status == OrderStatus.Cancelled)
                return Results.BadRequest(new { message = "Hóa đơn đã hủy, không thể thu thêm tiền." });

            var confirmed = request.Confirm;
            var amount = Round(request.Amount);
            var now = DateTimeOffset.UtcNow;
            var pendingTransfer = method == PaymentMethod.Transfer && confirmed
                ? order.Payments
                    .Where(payment => payment.Method == PaymentMethod.Transfer
                        && payment.Status == PaymentStatus.Pending)
                    .OrderBy(payment => payment.CreatedAt)
                    .FirstOrDefault()
                : null;

            // Xác nhận tay đúng khoản chuyển khoản đang chờ: cập nhật payment đó thay vì
            // tạo thêm (tránh hóa đơn in ra bị cộng hai lần và còn rác Pending).
            if (pendingTransfer is not null)
            {
                var applied = Math.Min(amount, pendingTransfer.Amount);
                var leftover = pendingTransfer.Amount - applied;
                pendingTransfer.Status = PaymentStatus.Confirmed;
                pendingTransfer.Amount = applied;
                pendingTransfer.Provider ??= "SePay";
                pendingTransfer.Reference = Clean(request.Reference) ?? pendingTransfer.Reference;
                pendingTransfer.PaidAt = now;
                amount -= applied;

                // Khách mới chuyển một phần: giữ lại khoản chờ cho phần còn lại.
                if (leftover > 0)
                {
                    order.Payments.Add(new Payment
                    {
                        TenantId = order.TenantId,
                        Method = PaymentMethod.Transfer,
                        Status = PaymentStatus.Pending,
                        Amount = leftover,
                        Provider = "SePay",
                    });
                }
            }

            if (pendingTransfer is null || amount > 0)
            {
                order.Payments.Add(new Payment
                {
                    TenantId = order.TenantId,
                    Method = method,
                    Status = confirmed ? PaymentStatus.Confirmed : PaymentStatus.Pending,
                    Amount = amount,
                    Provider = method == PaymentMethod.Transfer ? "SePay" : null,
                    Reference = Clean(request.Reference),
                    PaidAt = confirmed ? now : null,
                });
            }

            ApplyPaymentTotals(order);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(order));
        })
        .WithSummary("Thu thêm tiền / xác nhận thủ công một giao dịch chuyển khoản")
        .Produces<OrderResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{orderId:guid}/cancel", async (
            Guid orderId,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var order = await db.Orders.Include(row => row.Lines).Include(row => row.Payments)
                .FirstOrDefaultAsync(row => row.Id == orderId, ct);
            if (order is null)
                return Results.NotFound(new { message = "Không tìm thấy hóa đơn." });
            if (order.Status == OrderStatus.Cancelled)
                return Results.Ok(ToResponse(order));

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var line in order.Lines)
            {
                await db.Products
                    .Where(product => product.Id == line.ProductId && product.Stock != null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(product => product.Stock, product => product.Stock + line.Quantity)
                        .SetProperty(product => product.UpdatedAt, DateTimeOffset.UtcNow), ct);
            }

            order.Status = OrderStatus.Cancelled;
            foreach (var payment in order.Payments.Where(payment => payment.Status == PaymentStatus.Pending))
                payment.Status = PaymentStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToResponse(order));
        })
        .RequireAuthorization(AuthorizationPolicies.OwnerOrManager)
        .WithSummary("Hủy hóa đơn và hoàn lại tồn kho")
        .Produces<OrderResponse>();

        group.MapGet("/{orderId:guid}/vietqr", async (
            Guid orderId,
            ApplicationDbContext db,
            IOptions<SePayOptions> sepayOptions,
            CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking()
                .Include(row => row.Payments)
                .FirstOrDefaultAsync(row => row.Id == orderId, ct);
            if (order is null)
                return Results.NotFound(new { message = "Không tìm thấy hóa đơn." });

            var remaining = Round(Math.Max(0, order.Total - order.PaidAmount));
            if (remaining <= 0)
                return Results.BadRequest(new { message = "Hóa đơn đã thanh toán đủ, không cần mã QR." });
            if (order.Status == OrderStatus.Cancelled)
                return Results.BadRequest(new { message = "Hóa đơn đã hủy." });

            var options = sepayOptions.Value;
            if (!options.IsBankConfigured)
            {
                return Results.Conflict(new
                {
                    message = "Chưa cấu hình tài khoản ngân hàng nhận tiền (SePay:BankBin / SePay:AccountNumber).",
                    configured = false,
                });
            }

            var template = string.IsNullOrWhiteSpace(options.VietQrTemplate) ? "compact2" : options.VietQrTemplate;

            // Hóa đơn đang chờ một QR tại quầy (POS...): hiện lại đúng mã khách đã quét,
            // không sinh thêm mã HD... khác khiến khách chuyển vào một nội dung mới.
            var pendingReference = order.Payments
                .Where(payment => payment.Status == PaymentStatus.Pending
                    && payment.Method == PaymentMethod.Transfer
                    && payment.Reference != null
                    && payment.Reference.StartsWith("POS", StringComparison.OrdinalIgnoreCase))
                .Select(payment => payment.Reference!)
                .FirstOrDefault();
            if (pendingReference is not null)
            {
                var session = await db.PosQrSessions.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.Reference == pendingReference, ct);
                if (session is not null && session.Status != PosQrStatus.Expired)
                {
                    return Results.Ok(new VietQrResponse(
                        ImageUrl: VietQrUrlBuilder.Build(options, session.Amount, session.Reference),
                        Amount: session.Amount,
                        OrderCode: session.Reference,
                        BankName: options.BankName,
                        AccountNumber: options.AccountNumber,
                        AccountName: options.AccountName,
                        Template: template,
                        ExpiresInSeconds: Math.Max(
                            0,
                            (int)Math.Ceiling((session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds))));
                }
            }

            var imageUrl = VietQrUrlBuilder.Build(options, remaining, order.Code);

            return Results.Ok(new VietQrResponse(
                ImageUrl: imageUrl,
                Amount: remaining,
                OrderCode: order.Code,
                BankName: options.BankName,
                AccountNumber: options.AccountNumber,
                AccountName: options.AccountName,
                Template: template,
                ExpiresInSeconds: 900));
        })
        .WithSummary("Sinh mã VietQR cho số tiền còn lại của hóa đơn")
        .Produces<VietQrResponse>()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{orderId:guid}/invoice-qr", async (
            Guid orderId,
            ApplicationDbContext db,
            IOptions<SePayOptions> sepayOptions,
            CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == orderId, ct);
            if (order is null)
                return Results.NotFound(new { message = "Không tìm thấy hóa đơn." });

            var options = sepayOptions.Value;
            var imageUrl = options.IsBankConfigured
                ? VietQrUrlBuilder.Build(options, order.Total, order.Code)
                : null;

            return Results.Ok(new InvoiceQrResponse(imageUrl, order.Total, order.Code));
        })
        .WithSummary("Mã QR in trên hóa đơn (kể cả khi đã thanh toán)")
        .Produces<InvoiceQrResponse>();

        return app;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Mã hóa đơn tăng dần toàn hệ thống: HD000001 (sequence trong PostgreSQL).</summary>
    private static async Task<string> NextOrderCodeAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var next = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('order_code_seq') AS \"Value\"")
            .SingleAsync(ct);
        return $"HD{next:D6}";
    }

    /// <summary>Tính lại trạng thái hóa đơn sau mỗi lần thu tiền.</summary>
    internal static void ApplyPaymentTotals(Order order)
    {
        var confirmed = Round(order.Payments
            .Where(payment => payment.Status == PaymentStatus.Confirmed)
            .Sum(payment => payment.Amount));

        order.PaidAmount = confirmed;
        order.ChangeAmount = Round(Math.Max(0, confirmed - order.Total));
        order.Status = order.Status == OrderStatus.Cancelled
            ? OrderStatus.Cancelled
            : StatusFor(order.Total, confirmed);
    }

    internal static OrderStatus StatusFor(decimal total, decimal paid) =>
        total <= 0 ? OrderStatus.Completed
        : paid >= total ? OrderStatus.Completed
        : paid > 0 ? OrderStatus.PartiallyPaid
        : OrderStatus.Pending;

    internal static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Đầu ngày theo giờ Việt Nam (UTC+7) – DateTimeOffset lưu UTC.</summary>
    private static DateTimeOffset StartOfVietnamDay(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OrderSummary ToSummary(Order order) => new(
        order.Id,
        order.Code,
        order.Status,
        order.Channel,
        order.CustomerId,
        order.CustomerName,
        order.CustomerPhone,
        order.SoldByName,
        order.Subtotal,
        order.Discount,
        order.Total,
        order.PaidAmount,
        Round(Math.Max(0, order.Total - order.PaidAmount)),
        order.ChangeAmount,
        order.Lines.Count,
        order.Lines.Sum(line => line.Quantity),
        string.Join(" + ", order.Payments
            .Where(payment => payment.Status != PaymentStatus.Cancelled)
            .Select(payment => MethodLabel(payment.Method))
            .Distinct()),
        order.CreatedAt);

    internal static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.Transfer => "Chuyển khoản",
        PaymentMethod.Card => "Thẻ",
        PaymentMethod.Wallet => "Ví",
        PaymentMethod.Voucher => "Voucher",
        _ => method.ToString(),
    };

    internal static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.Code,
        order.Status,
        order.Channel,
        order.CustomerId,
        order.CustomerName,
        order.CustomerPhone,
        order.SoldByName,
        order.Subtotal,
        order.Discount,
        order.Total,
        order.PaidAmount,
        Round(Math.Max(0, order.Total - order.PaidAmount)),
        order.ChangeAmount,
        order.PointsRedeemed,
        order.PointsDiscount,
        order.PointsEarned,
        order.Note,
        order.CouponCode,
        order.DeliveryAddress,
        order.ShippingFee,
        order.RecipientName,
        order.RecipientPhone,
        order.DeliveryCityName,
        order.DeliveryDistrictName,
        order.DeliveryWardName,
        order.ShippingCarrier,
        order.TrackingNumber,
        order.CreatedAt,
        order.Lines
            .OrderBy(line => line.Sku)
            .Select(line => new OrderLineResponse(
                line.Id, line.ProductId, line.Sku, line.ProductName, line.UnitName,
                line.UnitPrice, line.Quantity, line.Discount, line.LineTotal))
            .ToList(),
        order.Payments
            .OrderBy(payment => payment.CreatedAt)
            .Select(payment => new PaymentResponse(
                payment.Id, payment.Method, payment.Status, payment.Amount,
                payment.Provider, payment.Reference, payment.PaidAt))
            .ToList());

    // ── DTOs ────────────────────────────────────────────────────────────────
    public sealed record CreateOrderLineRequest(Guid ProductId, decimal Quantity, decimal? UnitPrice, decimal? Discount);

    public sealed record CreateOrderPaymentRequest(string Method, decimal Amount, string? Reference);

    public sealed record CreateOrderRequest(
        Guid? CustomerId,
        string? CustomerName,
        string? CustomerPhone,
        string? Note,
        string? CouponCode,
        decimal? Discount,
        string? Channel,
        string? DeliveryAddress,
        decimal? ShippingFee,
        string? RecipientName,
        string? RecipientPhone,
        string? DeliveryCityId,
        string? DeliveryDistrictId,
        string? DeliveryWardId,
        string? DeliveryCityName,
        string? DeliveryDistrictName,
        string? DeliveryWardName,
        IReadOnlyList<CreateOrderLineRequest>? Lines,
        IReadOnlyList<CreateOrderPaymentRequest>? Payments,
        int? PointsToRedeem,
        bool? CreditOverpayment);

    public sealed record AddPaymentRequest(string Method, decimal Amount, string? Reference, bool Confirm);

    public sealed record VietQrResponse(
        string ImageUrl,
        decimal Amount,
        string OrderCode,
        string? BankName,
        string? AccountNumber,
        string? AccountName,
        string Template,
        int ExpiresInSeconds);

    public sealed record InvoiceQrResponse(
        string? ImageUrl,
        decimal Amount,
        string OrderCode);

    public sealed record OrderLineResponse(
        Guid Id,
        Guid ProductId,
        string Sku,
        string ProductName,
        string? UnitName,
        decimal UnitPrice,
        decimal Quantity,
        decimal Discount,
        decimal LineTotal);

    public sealed record PaymentResponse(
        Guid Id,
        PaymentMethod Method,
        PaymentStatus Status,
        decimal Amount,
        string? Provider,
        string? Reference,
        DateTimeOffset? PaidAt);

    public sealed record OrderResponse(
        Guid Id,
        string Code,
        OrderStatus Status,
        SaleChannel Channel,
        Guid? CustomerId,
        string? CustomerName,
        string? CustomerPhone,
        string? SoldByName,
        decimal Subtotal,
        decimal Discount,
        decimal Total,
        decimal PaidAmount,
        decimal Remaining,
        decimal ChangeAmount,
        int PointsRedeemed,
        decimal PointsDiscount,
        int PointsEarned,
        string? Note,
        string? CouponCode,
        string? DeliveryAddress,
        decimal ShippingFee,
        string? RecipientName,
        string? RecipientPhone,
        string? DeliveryCityName,
        string? DeliveryDistrictName,
        string? DeliveryWardName,
        string? ShippingCarrier,
        string? TrackingNumber,
        DateTimeOffset CreatedAt,
        IReadOnlyList<OrderLineResponse> Lines,
        IReadOnlyList<PaymentResponse> Payments);

    public sealed record OrderSummary(
        Guid Id,
        string Code,
        OrderStatus Status,
        SaleChannel Channel,
        Guid? CustomerId,
        string? CustomerName,
        string? CustomerPhone,
        string? SoldByName,
        decimal Subtotal,
        decimal Discount,
        decimal Total,
        decimal PaidAmount,
        decimal Remaining,
        decimal ChangeAmount,
        int ItemCount,
        decimal Quantity,
        string PaymentMethods,
        DateTimeOffset CreatedAt);
}

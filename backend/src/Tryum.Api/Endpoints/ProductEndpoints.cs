using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Security;
using Tryum.Domain.Products;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

public static class ProductEndpoints
{
    private const long MaxWorkbookBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn2-retail-images.kiotviet.vn",
        "cdn1-retail-images.kiotviet.vn",
        "cdn-images.kiotviet.vn",
        "han02.vstorage.vngcloud.vn",
    };

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products").WithTags("Products")
            .RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        group.MapGet("", async (int? page, int? pageSize, string? search, ApplicationDbContext db, CancellationToken ct) =>
        {
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var products = db.Products.AsNoTracking();
            var term = search?.Trim();
            if (!string.IsNullOrEmpty(term))
                products = products.Where(product => product.Sku.Contains(term)
                    || product.Name.Contains(term)
                    || (product.Barcode != null && product.Barcode.Contains(term)));
            var query = products.OrderBy(product => product.Name);
            var total = await query.CountAsync(ct);
            var items = await query.Skip((currentPage - 1) * size).Take(size)
                .Select(product => new ProductListItem(product.Id, product.Sku, product.Barcode, product.Name,
                    product.ItemType, product.CategoryPath, product.Brand, product.SalePrice, product.CostPrice,
                    product.Stock, product.CreatedAt,
                    (Guid?)product.Images.Where(image => image.Status == "Stored")
                        .OrderBy(image => image.CreatedAt).Select(image => image.Id).FirstOrDefault(),
                    product.Images.Count(image => image.Status == "Pending"),
                    product.Images.Count(image => image.Status == "Failed")))
                .ToListAsync(ct);
            return Results.Ok(new { items, total, page = currentPage, pageSize = size });
        }).WithSummary("Danh sách sản phẩm thuộc cửa hàng đang đăng nhập");

        group.MapGet("/{productId:guid}/images/{imageId:guid}", async (
            Guid productId, Guid imageId, ApplicationDbContext db, IObjectStorage storage, CancellationToken ct) =>
        {
            var image = await db.ProductImages.AsNoTracking().SingleOrDefaultAsync(
                row => row.Id == imageId && row.ProductId == productId && row.Status == "Stored", ct);
            if (image is null) return Results.NotFound();
            var content = await storage.GetAsync(image.ObjectKey, ct);
            return content is null ? Results.NotFound() : Results.Stream(content, image.ContentType);
        }).WithSummary("Đọc ảnh sản phẩm riêng tư từ Cloudflare R2");

        group.MapPost("/import", async (
            IFormFile file,
            ProductImportJobStore jobs,
            IServiceScopeFactory scopeFactory,
            IOptions<R2Options> r2) =>
        {
            if (file.Length is <= 0 or > MaxWorkbookBytes)
                return Results.BadRequest(new { message = "File rỗng hoặc vượt quá giới hạn 20 MB." });
            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "API import hiện nhận file Excel .xlsx." });

            var tenantId = TenantContext.RequireCurrent();
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            var workbook = buffer.ToArray();

            var job = jobs.Create(tenantId, file.FileName);
            job.ImageStorageConfigured = r2.Value.IsConfigured;
            _ = Task.Run(() => ProcessImportJobAsync(job, workbook, scopeFactory));

            return Results.Accepted($"/api/v1/products/import/{job.Id}", new
            {
                jobId = job.Id,
                status = job.Status,
                startedAt = job.StartedAt,
                imageStorageConfigured = job.ImageStorageConfigured,
            });
        })
        .DisableAntiforgery()
        .WithSummary("Nhận file Excel và import nền, trả jobId để theo dõi tiến độ")
        .Produces(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/import/{jobId:guid}", (Guid jobId, ProductImportJobStore jobs) =>
        {
            var job = jobs.Get(jobId);
            if (job is null || job.TenantId != TenantContext.RequireCurrent())
                return Results.NotFound(new { message = "Không tìm thấy tiến trình import." });
            return Results.Ok(job.Snapshot());
        })
        .WithSummary("Theo dõi tiến độ import sản phẩm")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/images/sync-status", async (
            DateTimeOffset? since,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var query = db.ProductImages.AsNoTracking();
            if (since is { } from)
                query = query.Where(image => image.CreatedAt >= from);

            var groups = await query
                .GroupBy(image => image.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(ct);

            int Count(string status) => groups.FirstOrDefault(group => group.Status == status)?.Count ?? 0;
            return Results.Ok(new
            {
                pending = Count("Pending"),
                stored = Count("Stored"),
                failed = Count("Failed"),
                total = groups.Sum(group => group.Count),
            });
        })
        .WithSummary("Tiến độ đồng bộ ảnh sản phẩm lên R2")
        .Produces(StatusCodes.Status200OK);

        group.MapPost("", async (CreateProductRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { message = "Tên hàng là bắt buộc." });
            if (name.Length > 500)
                return Results.BadRequest(new { message = "Tên hàng quá dài." });
            if (request.SalePrice is < 0 || request.CostPrice is < 0 || request.Stock is < 0
                || request.MinStock is < 0 || request.MaxStock is < 0 || request.Weight is < 0)
            {
                return Results.BadRequest(new { message = "Giá bán, giá vốn, tồn kho và trọng lượng không được âm." });
            }
            if (request.MinStock is { } minStock && request.MaxStock is { } maxStock && minStock > maxStock)
                return Results.BadRequest(new { message = "Định mức tồn thấp nhất không được lớn hơn mức cao nhất." });

            var sku = request.Sku?.Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                do
                {
                    sku = $"SP{Random.Shared.Next(100000, 999999)}";
                } while (await db.Products.AnyAsync(product => product.Sku == sku, ct));
            }
            else if (sku.Length > 120)
            {
                return Results.BadRequest(new { message = "Mã hàng quá dài." });
            }

            if (await db.Products.AnyAsync(product => product.Sku == sku, ct))
                return Results.Conflict(new { message = $"Mã hàng {sku} đã tồn tại." });

            var product = new Product
            {
                TenantId = TenantContext.RequireCurrent(),
                Sku = sku,
                Barcode = CleanText(request.Barcode),
                Name = name,
                ItemType = string.IsNullOrWhiteSpace(request.ItemType) ? "Hàng hóa" : request.ItemType.Trim(),
                CategoryPath = CleanText(request.CategoryPath),
                Brand = CleanText(request.Brand),
                SalePrice = request.SalePrice,
                CostPrice = request.CostPrice,
                Stock = request.Stock,
                MinStock = request.MinStock,
                MaxStock = request.MaxStock,
                Position = CleanText(request.Position),
                Weight = request.Weight,
                Description = CleanText(request.Description),
                IsDirectSale = request.IsDirectSale ?? true,
                UnitsJson = JsonSerializer.Serialize(request.Units ?? []),
                AttributesJson = JsonSerializer.Serialize(request.Attributes ?? []),
                VariantsJson = JsonSerializer.Serialize(request.Variants ?? []),
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/products/{product.Id}", ToDetail(product));
        })
        .WithSummary("Tạo sản phẩm mới (mã hàng để trống sẽ tự sinh)")
        .Produces<ProductDetail>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{productId:guid}", async (Guid productId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.AsNoTracking()
                .Include(row => row.Images)
                .FirstOrDefaultAsync(row => row.Id == productId, ct);
            return product is null
                ? Results.NotFound(new { message = "Không tìm thấy hàng hóa." })
                : Results.Ok(ToDetail(product));
        })
        .WithSummary("Chi tiết hàng hóa")
        .Produces<ProductDetail>();

        group.MapPut("/{productId:guid}", async (
            Guid productId,
            UpdateProductRequest request,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var product = await db.Products.Include(row => row.Images)
                .FirstOrDefaultAsync(row => row.Id == productId, ct);
            if (product is null) return Results.NotFound(new { message = "Không tìm thấy hàng hóa." });

            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { message = "Tên hàng là bắt buộc." });
            if (name.Length > 500) return Results.BadRequest(new { message = "Tên hàng quá dài." });
            if (request.SalePrice is < 0 || request.CostPrice is < 0 || request.Stock is < 0
                || request.MinStock is < 0 || request.MaxStock is < 0 || request.Weight is < 0)
            {
                return Results.BadRequest(new { message = "Giá bán, giá vốn, tồn kho và trọng lượng không được âm." });
            }
            if (request.MinStock is { } minStock && request.MaxStock is { } maxStock && minStock > maxStock)
                return Results.BadRequest(new { message = "Định mức tồn thấp nhất không được lớn hơn mức cao nhất." });

            var sku = request.Sku?.Trim();
            if (string.IsNullOrWhiteSpace(sku)) return Results.BadRequest(new { message = "Mã hàng là bắt buộc." });
            if (sku.Length > 120) return Results.BadRequest(new { message = "Mã hàng quá dài." });
            if (!string.Equals(sku, product.Sku, StringComparison.OrdinalIgnoreCase)
                && await db.Products.AnyAsync(row => row.Sku == sku && row.Id != productId, ct))
            {
                return Results.Conflict(new { message = $"Mã hàng {sku} đã tồn tại." });
            }

            product.Sku = sku;
            product.Barcode = CleanText(request.Barcode);
            product.Name = name;
            product.ItemType = string.IsNullOrWhiteSpace(request.ItemType) ? product.ItemType : request.ItemType.Trim();
            product.CategoryPath = CleanText(request.CategoryPath);
            product.Brand = CleanText(request.Brand);
            product.SalePrice = request.SalePrice;
            product.CostPrice = request.CostPrice;
            product.Stock = request.Stock;
            product.MinStock = request.MinStock;
            product.MaxStock = request.MaxStock;
            product.Position = CleanText(request.Position);
            product.Weight = request.Weight;
            product.Description = CleanText(request.Description);
            product.IsDirectSale = request.IsDirectSale ?? product.IsDirectSale;
            product.UnitsJson = JsonSerializer.Serialize(request.Units ?? []);
            product.AttributesJson = JsonSerializer.Serialize(request.Attributes ?? []);
            product.VariantsJson = JsonSerializer.Serialize(request.Variants ?? []);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDetail(product));
        })
        .WithSummary("Cập nhật hàng hóa")
        .Produces<ProductDetail>();

        group.MapDelete("/{productId:guid}", async (Guid productId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(row => row.Id == productId, ct);
            if (product is null) return Results.NotFound(new { message = "Không tìm thấy hàng hóa." });
            db.Products.Remove(product);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Xóa hàng hóa")
        .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{productId:guid}/images", async (
            Guid productId,
            IFormFile file,
            bool? replace,
            ApplicationDbContext db,
            IObjectStorage storage,
            CancellationToken ct) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(row => row.Id == productId, ct);
            if (product is null) return Results.NotFound(new { message = "Không tìm thấy hàng hóa." });
            if (file.Length is <= 0 or > 5 * 1024 * 1024)
                return Results.BadRequest(new { message = "Ảnh rỗng hoặc vượt quá 5 MB." });

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var contentType = file.ContentType;
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                contentType = extension switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    _ => "image/jpeg",
                };
            }
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(extension))
                return Results.BadRequest(new { message = "Chỉ nhận ảnh JPG, PNG, WEBP hoặc GIF." });

            var objectKey = $"tenants/{product.TenantId:D}/products/{product.Id:D}/{Guid.NewGuid():N}";
            await using (var stream = file.OpenReadStream())
                await storage.PutAsync(objectKey, stream, contentType, ct);

            // Ảnh tải tay thay thế ảnh đại diện cũ (ảnh cũ vẫn nằm trên R2 nhưng không hiển thị).
            if (replace == true)
            {
                await db.ProductImages
                    .Where(row => row.ProductId == product.Id && row.Status == "Stored")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, "Archived"), ct);
            }

            var image = new ProductImage
            {
                TenantId = product.TenantId,
                ProductId = product.Id,
                SourceUrl = $"upload://{Guid.NewGuid():N}",
                ObjectKey = objectKey,
                ContentType = contentType,
                Status = "Stored",
            };
            db.ProductImages.Add(image);

            product.R2ObjectKeysJson = JsonSerializer.Serialize(new[] { objectKey });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/products/{product.Id}/images/{image.Id}", new
            {
                id = image.Id,
                imageId = image.Id,
            });
        })
        .DisableAntiforgery()
        .WithSummary("Tải ảnh đại diện sản phẩm lên R2")
        .Produces(StatusCodes.Status201Created);

        return app;
    }

    private static string? CleanText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProductDetail ToDetail(Product product) => new(
        product.Id,
        product.Sku,
        product.Barcode,
        product.Name,
        product.ItemType,
        product.CategoryPath,
        product.Brand,
        product.SalePrice,
        product.CostPrice,
        product.Stock,
        product.MinStock,
        product.MaxStock,
        product.Position,
        product.Weight,
        product.Description,
        product.IsDirectSale,
        product.UnitsJson,
        product.AttributesJson,
        product.VariantsJson,
        product.Images.Where(image => image.Status == "Stored")
            .OrderBy(image => image.CreatedAt)
            .Select(image => (Guid?)image.Id)
            .FirstOrDefault(),
        product.Images.Count(image => image.Status == "Pending"),
        product.Images.Count(image => image.Status == "Failed"));

    public sealed record ProductDetail(
        Guid Id,
        string Sku,
        string? Barcode,
        string Name,
        string ItemType,
        string? CategoryPath,
        string? Brand,
        decimal? SalePrice,
        decimal? CostPrice,
        decimal? Stock,
        decimal? MinStock,
        decimal? MaxStock,
        string? Position,
        decimal? Weight,
        string? Description,
        bool IsDirectSale,
        string UnitsJson,
        string AttributesJson,
        string VariantsJson,
        Guid? ImageId,
        int ImagesPending,
        int ImagesFailed);

    public sealed record CreateProductRequest(
        string? Sku,
        string? Barcode,
        string? Name,
        string? ItemType,
        string? CategoryPath,
        string? Brand,
        decimal? SalePrice,
        decimal? CostPrice,
        decimal? Stock,
        decimal? MinStock,
        decimal? MaxStock,
        string? Position,
        decimal? Weight,
        string? Description,
        bool? IsDirectSale,
        IReadOnlyList<ProductUnitInput>? Units,
        IReadOnlyList<ProductAttributeInput>? Attributes,
        IReadOnlyList<ProductVariantInput>? Variants);

    public sealed record UpdateProductRequest(
        string? Sku,
        string? Barcode,
        string? Name,
        string? ItemType,
        string? CategoryPath,
        string? Brand,
        decimal? SalePrice,
        decimal? CostPrice,
        decimal? Stock,
        decimal? MinStock,
        decimal? MaxStock,
        string? Position,
        decimal? Weight,
        string? Description,
        bool? IsDirectSale,
        IReadOnlyList<ProductUnitInput>? Units,
        IReadOnlyList<ProductAttributeInput>? Attributes,
        IReadOnlyList<ProductVariantInput>? Variants);

    public sealed record ProductUnitInput(string? Name, decimal? Conversion, decimal? SalePrice, bool? IsDirectSale);

    public sealed record ProductAttributeInput(string? Name, IReadOnlyList<string>? Values);

    public sealed record ProductVariantInput(
        string? Name,
        string? Unit,
        decimal? Conversion,
        string? Sku,
        string? Barcode,
        decimal? CostPrice,
        decimal? SalePrice,
        decimal? Stock);

    private static async Task ProcessImportJobAsync(
        ProductImportJob job,
        byte[] workbook,
        IServiceScopeFactory scopeFactory)
    {
        using var tenantScope = TenantContext.Use(job.TenantId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ProductImport");

        try
        {
            job.Status = "Reading";
            using var stream = new MemoryStream(workbook, writable: false);
            var data = await ReadWorkbookAsync(stream, CancellationToken.None);
            if (data.Count == 0) throw new InvalidOperationException("File không có dòng sản phẩm.");
            if (data.Count > 50_000) throw new InvalidOperationException("Tối đa 50.000 sản phẩm mỗi lần import.");

            job.Total = data.Count;
            job.Status = "Preparing";

            var invalidRow = data.FirstOrDefault(row =>
                string.IsNullOrWhiteSpace(Get(row, "Mã hàng")) || string.IsNullOrWhiteSpace(Get(row, "Tên hàng")));
            if (invalidRow is not null)
                throw new InvalidOperationException("Mỗi dòng cần có Mã hàng và Tên hàng.");

            var rows = data.Select(ParseImportRow).ToList();
            var prepared = new List<PreparedProduct>(rows.Count);
            foreach (var group in rows.GroupBy(row => row.BaseSku, StringComparer.OrdinalIgnoreCase))
            {
                var baseRow = group.FirstOrDefault(row => string.Equals(row.Sku, row.BaseSku, StringComparison.OrdinalIgnoreCase))
                    ?? group.First();
                var imageUrls = group.SelectMany(row => row.ImageUrls)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var unsupportedImage = imageUrls.FirstOrDefault(url => !IsAllowedImageUrl(url));
                if (unsupportedImage is not null)
                {
                    var host = Uri.TryCreate(unsupportedImage, UriKind.Absolute, out var imageUri) ? imageUri.Host : "không hợp lệ";
                    throw new InvalidOperationException($"File có URL ảnh từ máy chủ chưa được cho phép: {host}.");
                }

                var units = new List<PreparedUnit>();
                foreach (var row in group)
                {
                    if (string.IsNullOrWhiteSpace(row.UnitName)) continue;
                    if (units.Any(unit => string.Equals(unit.Name, row.UnitName, StringComparison.OrdinalIgnoreCase))) continue;
                    units.Add(new PreparedUnit(
                        row.UnitName.Trim(),
                        string.Equals(row.Sku, baseRow.Sku, StringComparison.OrdinalIgnoreCase) ? 1 : row.Conversion ?? 1,
                        row.SalePrice,
                        row.IsDirectSale ?? true));
                }
                if (units.Count == 0)
                    units.Add(new PreparedUnit("Cái", 1, baseRow.SalePrice, baseRow.IsDirectSale ?? true));

                var attributeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in group.Where(row => !string.IsNullOrWhiteSpace(row.AttributeRaw)))
                {
                    foreach (Match match in Regex.Matches(row.AttributeRaw!, @"([^:;,\n]+):([^;,\n]+)"))
                    {
                        var name = match.Groups[1].Value.Trim();
                        var value = match.Groups[2].Value.Trim();
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) continue;
                        if (!attributeMap.TryGetValue(name, out var values))
                        {
                            values = [];
                            attributeMap[name] = values;
                        }
                        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
                    }
                }

                var variants = group
                    .Where(row => !string.Equals(row.Sku, baseRow.Sku, StringComparison.OrdinalIgnoreCase))
                    .Select(row => new PreparedVariant(
                        row.AttributeRaw ?? row.UnitName ?? row.Sku,
                        row.UnitName ?? string.Empty,
                        row.Conversion,
                        row.Sku,
                        row.Barcode,
                        row.CostPrice,
                        row.SalePrice,
                        row.Stock))
                    .ToList();

                prepared.Add(new PreparedProduct(
                    baseRow.Sku,
                    baseRow.Barcode,
                    baseRow.Name,
                    baseRow.ItemType,
                    baseRow.CategoryPath,
                    baseRow.Brand,
                    baseRow.SalePrice,
                    baseRow.CostPrice,
                    baseRow.Stock,
                    baseRow.MinStock,
                    baseRow.MaxStock,
                    baseRow.Position,
                    baseRow.Weight,
                    baseRow.Description,
                    baseRow.IsDirectSale,
                    baseRow.CreatedAt,
                    imageUrls,
                    units,
                    attributeMap.Select(pair => new PreparedAttribute(pair.Key, pair.Value)).ToList(),
                    variants,
                    baseRow.SourceDataJson));
            }

            job.Total = prepared.Count;

            var tenantId = job.TenantId;
            var skus = prepared.Select(product => product.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (skus.Length != prepared.Count)
                throw new InvalidOperationException("File có Mã hàng bị trùng.");

            var existing = await db.Products
                .Where(product => skus.Contains(product.Sku))
                .ToDictionaryAsync(product => product.Sku, StringComparer.OrdinalIgnoreCase, CancellationToken.None);

            job.Status = "Importing";
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);

            // Các dòng là đơn vị/biến thể của sản phẩm cha (Mã ĐVT cơ bản) không còn là sản phẩm riêng.
            var childSkus = rows
                .Where(row => !string.Equals(row.Sku, row.BaseSku, StringComparison.OrdinalIgnoreCase))
                .Select(row => row.Sku)
                .Where(sku => !skus.Contains(sku, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (childSkus.Length > 0)
                await db.Products.Where(product => childSkus.Contains(product.Sku)).ExecuteDeleteAsync(CancellationToken.None);

            var created = 0;
            var updated = 0;
            var imageCount = 0;
            var productsInBatch = 0;

            for (var index = 0; index < prepared.Count; index++)
            {
                var item = prepared[index];
                var isNew = !existing.TryGetValue(item.Sku, out var product);
                if (isNew)
                {
                    product = new Product { TenantId = tenantId, Sku = item.Sku };
                    db.Products.Add(product);
                    existing.Add(item.Sku, product);
                    created++;
                }
                else updated++;

                product!.Barcode = item.Barcode;
                product.Name = item.Name;
                product.ItemType = item.ItemType;
                product.CategoryPath = item.CategoryPath;
                product.Brand = item.Brand;
                product.SalePrice = item.SalePrice;
                product.CostPrice = item.CostPrice;
                product.Stock = item.Stock;
                product.MinStock = item.MinStock;
                product.MaxStock = item.MaxStock;
                product.Position = item.Position;
                product.Weight = item.Weight;
                product.Description = item.Description;
                product.IsDirectSale = item.IsDirectSale ?? true;
                if (item.CreatedAt is { } createdAt)
                    product.CreatedAt = createdAt;
                else if (isNew)
                    product.CreatedAt = DateTimeOffset.UtcNow;
                product.UnitsJson = JsonSerializer.Serialize(item.Units);
                product.AttributesJson = JsonSerializer.Serialize(item.Attributes);
                product.VariantsJson = JsonSerializer.Serialize(item.Variants);
                product.SourceImageUrlsJson = JsonSerializer.Serialize(item.ImageUrls);
                product.R2ObjectKeysJson = "[]";
                product.SourceDataJson = item.SourceDataJson;

                if (isNew)
                {
                    foreach (var imageUrl in item.ImageUrls)
                    {
                        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl))).ToLowerInvariant()[..20];
                        db.ProductImages.Add(new ProductImage
                        {
                            TenantId = tenantId,
                            ProductId = product.Id,
                            SourceUrl = imageUrl,
                            ObjectKey = $"tenants/{tenantId:D}/products/{product.Id:D}/{digest}",
                        });
                        imageCount++;
                    }
                }

                job.Processed = index + 1;
                job.Created = created;
                job.Updated = updated;
                job.ImagesQueued = imageCount;

                productsInBatch++;
                if (productsInBatch >= 500)
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                    productsInBatch = 0;
                }
            }

            if (productsInBatch > 0)
                await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);

            job.Processed = job.Total;
            job.Created = created;
            job.Updated = updated;
            job.ImagesQueued = imageCount;
            job.Status = "Completed";
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.ElapsedMs = (long)(job.FinishedAt.Value - job.StartedAt).TotalMilliseconds;
            logger.LogInformation(
                "Import xong {Created} mới, {Updated} cập nhật, {Images} ảnh trong {Elapsed}ms.",
                created, updated, imageCount, job.ElapsedMs);
        }
        catch (Exception exception)
        {
            job.Status = "Failed";
            job.Error = exception.Message;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.ElapsedMs = (long)(job.FinishedAt.Value - job.StartedAt).TotalMilliseconds;
            logger.LogError(exception, "Import sản phẩm thất bại.");
        }
    }

    private static async Task<List<Dictionary<string, string>>> ReadWorkbookAsync(Stream input, CancellationToken ct)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var workbook = await LoadXmlAsync(archive, "xl/workbook.xml", ct);
        var relationships = await LoadXmlAsync(archive, "xl/_rels/workbook.xml.rels", ct);
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var firstSheet = workbook.Descendants().FirstOrDefault(element => element.Name.LocalName == "sheet")
            ?? throw new InvalidDataException("Workbook không có sheet.");
        var relationId = firstSheet.Attribute(relNs + "id")?.Value
            ?? throw new InvalidDataException("Không tìm thấy sheet relationship.");
        var target = relationships.Descendants().FirstOrDefault(element => element.Attribute("Id")?.Value == relationId)?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("Không tìm thấy worksheet.");
        // ZIP entry paths are POSIX-style; resolve relationship targets without touching the filesystem.
        var rawTarget = target.Replace('\\', '/');
        var normalizedPath = rawTarget.StartsWith('/') ? rawTarget.TrimStart('/') : $"xl/{rawTarget}";
        var segments = new List<string>();
        foreach (var segment in normalizedPath.Split('/'))
        {
            if (segment == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); }
            else if (segment is not "" and not ".") segments.Add(segment);
        }
        var sheet = await LoadXmlAsync(archive, string.Join('/', segments), ct);
        var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
        var shared = sharedEntry is null ? [] : await ReadSharedStringsAsync(sharedEntry, ct);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<Dictionary<string, string>>();
        string[]? headers = null;
        foreach (var row in sheet.Descendants(main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(main + "c"))
            {
                var column = ColumnIndex(cell.Attribute("r")?.Value ?? string.Empty);
                var type = cell.Attribute("t")?.Value;
                var value = type == "inlineStr"
                    ? string.Concat(cell.Descendants(main + "t").Select(text => text.Value))
                    : cell.Element(main + "v")?.Value ?? string.Empty;
                if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < shared.Count)
                    value = shared[sharedIndex];
                cells[column] = value;
            }
            if (cells.Count == 0) continue;
            var maxColumn = cells.Keys.Max();
            var values = Enumerable.Range(0, maxColumn + 1).Select(index => cells.GetValueOrDefault(index, string.Empty)).ToArray();
            if (headers is null) { headers = values; continue; }

            var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < Math.Min(headers.Length, values.Length); index++)
            {
                var header = headers[index].Trim();
                if (!string.IsNullOrWhiteSpace(header)) item[Normalize(header)] = values[index];
            }
            if (item.Values.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(item);
        }
        return rows;
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchive archive, string path, CancellationToken ct)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Thiếu thành phần {path}.");
        await using var stream = entry.Open();
        return await XDocument.LoadAsync(stream, LoadOptions.None, ct);
    }

    private static async Task<List<string>> ReadSharedStringsAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si").Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value))).ToList();
    }

    private static int ColumnIndex(string cellReference)
    {
        var result = 0;
        foreach (var character in cellReference.TakeWhile(char.IsLetter))
            result = result * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return Math.Max(0, result - 1);
    }

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
        .ToLowerInvariant().Replace("đ", "d").Replace(" ", string.Empty);

    private static string Get(IReadOnlyDictionary<string, string> row, string header) => row.GetValueOrDefault(Normalize(header), string.Empty);
    private static decimal? ParseDecimal(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static bool IsAllowedImageUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && AllowedImageHosts.Contains(uri.Host);

    private static List<string> ExtractImageUrls(string value) => Regex.Matches(value, "https://[^\\s,;|]+", RegexOptions.IgnoreCase)
        .Select(match => match.Value.Trim().TrimEnd(')', ']')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static ImportRow ParseImportRow(IReadOnlyDictionary<string, string> row)
    {
        var sku = Truncate(Get(row, "Mã hàng").Trim(), 120);
        var baseSku = Truncate(Get(row, "Mã ĐVT Cơ bản").Trim(), 120);
        if (string.IsNullOrWhiteSpace(baseSku)) baseSku = sku;
        var itemType = Truncate(Get(row, "Loại hàng").Trim(), 100);
        if (string.IsNullOrWhiteSpace(itemType)) itemType = "Hàng hóa";

        return new ImportRow(
            sku,
            baseSku,
            ClampText(Get(row, "Mã vạch"), 120),
            Truncate(Get(row, "Tên hàng").Trim(), 500),
            itemType,
            ClampText(Get(row, "Nhóm hàng(3 Cấp)"), 500),
            ClampText(Get(row, "Thương hiệu"), 250),
            ParseDecimal(Get(row, "Giá bán sau thuế")) ?? ParseDecimal(Get(row, "Giá bán trước thuế")) ?? ParseDecimal(Get(row, "Giá bán")),
            ParseDecimal(Get(row, "Giá vốn")),
            ParseDecimal(Get(row, "Tồn kho")),
            ParseDecimal(Get(row, "Tồn nhỏ nhất")),
            ParseDecimal(Get(row, "Tồn lớn nhất")),
            ClampText(Get(row, "ĐVT"), 100),
            ParseDecimal(Get(row, "Quy đổi")),
            ClampText(Get(row, "Thuộc tính"), 500),
            ParseDecimal(Get(row, "Trọng lượng")),
            ClampText(Get(row, "Mô tả"), 4000),
            ClampText(Get(row, "Vị trí"), 200),
            ParseBool(Get(row, "Được bán trực tiếp")),
            ParseExcelDate(Get(row, "Thời gian tạo")),
            ExtractImageUrls(Get(row, "Hình ảnh (url1,url2...)")),
            JsonSerializer.Serialize(row));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? ClampText(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static bool? ParseBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "có" or "co" => true,
        "0" or "false" or "không" or "khong" => false,
        _ => null,
    };

    private static DateTimeOffset? ParseExcelDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial) && serial > 1)
        {
            try
            {
                var date = DateTime.FromOADate((double)serial);
                return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        var formats = new[]
        {
            "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm:ss", "d/M/yyyy H:mm",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        };
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime();

        return null;
    }

    private sealed record ImportRow(
        string Sku,
        string BaseSku,
        string? Barcode,
        string Name,
        string ItemType,
        string? CategoryPath,
        string? Brand,
        decimal? SalePrice,
        decimal? CostPrice,
        decimal? Stock,
        decimal? MinStock,
        decimal? MaxStock,
        string? UnitName,
        decimal? Conversion,
        string? AttributeRaw,
        decimal? Weight,
        string? Description,
        string? Position,
        bool? IsDirectSale,
        DateTimeOffset? CreatedAt,
        List<string> ImageUrls,
        string SourceDataJson);

    private sealed record PreparedUnit(string Name, decimal? Conversion, decimal? SalePrice, bool IsDirectSale);

    private sealed record PreparedAttribute(string Name, List<string> Values);

    private sealed record PreparedVariant(
        string Name,
        string Unit,
        decimal? Conversion,
        string? Sku,
        string? Barcode,
        decimal? CostPrice,
        decimal? SalePrice,
        decimal? Stock);

    private sealed record PreparedProduct(
        string Sku,
        string? Barcode,
        string Name,
        string ItemType,
        string? CategoryPath,
        string? Brand,
        decimal? SalePrice,
        decimal? CostPrice,
        decimal? Stock,
        decimal? MinStock,
        decimal? MaxStock,
        string? Position,
        decimal? Weight,
        string? Description,
        bool? IsDirectSale,
        DateTimeOffset? CreatedAt,
        List<string> ImageUrls,
        List<PreparedUnit> Units,
        List<PreparedAttribute> Attributes,
        List<PreparedVariant> Variants,
        string SourceDataJson);

    private sealed record ProductListItem(Guid Id, string Sku, string? Barcode, string Name, string ItemType,
        string? CategoryPath, string? Brand, decimal? SalePrice, decimal? CostPrice, decimal? Stock,
        DateTimeOffset CreatedAt, Guid? ImageId, int ImagesPending, int ImagesFailed);
}

internal static class ProductStringExtensions
{
    public static string? TrimOrNull(this string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string TrimOrEmpty(this string value) => value.Trim();
}

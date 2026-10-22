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
                    product.Stock, (Guid?)product.Images.Where(image => image.Status == "Stored")
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
            ApplicationDbContext db,
            HttpContext context,
            IOptions<R2Options> r2,
            CancellationToken ct) =>
        {
            if (file.Length is <= 0 or > MaxWorkbookBytes)
                return Results.BadRequest(new { message = "File rỗng hoặc vượt quá giới hạn 20 MB." });
            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "API import hiện nhận file Excel .xlsx." });

            List<Dictionary<string, string>> data;
            try
            {
                await using var stream = file.OpenReadStream();
                data = await ReadWorkbookAsync(stream, ct);
            }
            catch (Exception exception) when (exception is InvalidDataException or XmlException or FormatException)
            {
                return Results.BadRequest(new { message = "Không đọc được workbook Excel. Hãy dùng file .xlsx hợp lệ.", detail = exception.Message });
            }

            if (data.Count == 0) return Results.BadRequest(new { message = "File không có dòng sản phẩm." });
            if (data.Count > 50_000) return Results.BadRequest(new { message = "Tối đa 50.000 sản phẩm mỗi lần import." });

            var invalidRow = data.FirstOrDefault(row => string.IsNullOrWhiteSpace(Get(row, "Mã hàng")) || string.IsNullOrWhiteSpace(Get(row, "Tên hàng")));
            if (invalidRow is not null)
                return Results.BadRequest(new { message = "Mỗi dòng cần có Mã hàng và Tên hàng." });

            var prepared = new List<PreparedProduct>(data.Count);
            foreach (var row in data)
            {
                var imageUrls = ExtractImageUrls(Get(row, "Hình ảnh (url1,url2...)"));
                var unsupportedImage = imageUrls.FirstOrDefault(url => !IsAllowedImageUrl(url));
                if (unsupportedImage is not null)
                {
                    var host = Uri.TryCreate(unsupportedImage, UriKind.Absolute, out var imageUri) ? imageUri.Host : "không hợp lệ";
                    return Results.BadRequest(new { message = $"File có URL ảnh từ máy chủ chưa được cho phép: {host}." });
                }

                prepared.Add(new PreparedProduct(
                    Get(row, "Mã hàng").Trim(), Get(row, "Mã vạch").TrimOrNull(), Get(row, "Tên hàng").Trim(),
                    Get(row, "Loại hàng").TrimOrEmpty(), Get(row, "Nhóm hàng(3 Cấp)").TrimOrNull(),
                    Get(row, "Thương hiệu").TrimOrNull(),
                    ParseDecimal(Get(row, "Giá bán sau thuế")) ?? ParseDecimal(Get(row, "Giá bán trước thuế")) ?? ParseDecimal(Get(row, "Giá bán")),
                    ParseDecimal(Get(row, "Giá vốn")), ParseDecimal(Get(row, "Tồn kho")), imageUrls,
                    JsonSerializer.Serialize(row)));
            }

            var tenantId = TenantContext.RequireCurrent();
            var skus = prepared.Select(product => product.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (skus.Length != prepared.Count)
                return Results.BadRequest(new { message = "File có Mã hàng bị trùng." });
            var existing = await db.Products.Where(product => skus.Contains(product.Sku)).ToDictionaryAsync(product => product.Sku, StringComparer.OrdinalIgnoreCase, ct);
            var existingIds = existing.Values.Select(product => product.Id).ToArray();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (existingIds.Length > 0)
                await db.ProductImages.Where(image => existingIds.Contains(image.ProductId)).ExecuteDeleteAsync(ct);
            var created = 0;
            var updated = 0;
            var imageCount = 0;
            var productsInBatch = 0;

            foreach (var item in prepared)
            {
                if (!existing.TryGetValue(item.Sku, out var product))
                {
                    product = new Product { TenantId = tenantId, Sku = item.Sku };
                    db.Products.Add(product);
                    existing.Add(item.Sku, product);
                    created++;
                }
                else updated++;

                product.Barcode = item.Barcode;
                product.Name = item.Name;
                product.ItemType = item.ItemType;
                product.CategoryPath = item.CategoryPath;
                product.Brand = item.Brand;
                product.SalePrice = item.SalePrice;
                product.CostPrice = item.CostPrice;
                product.Stock = item.Stock;
                product.SourceImageUrlsJson = JsonSerializer.Serialize(item.ImageUrls);
                product.R2ObjectKeysJson = "[]";
                product.SourceDataJson = item.SourceDataJson;

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

                productsInBatch++;
                if (productsInBatch >= 100)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    productsInBatch = 0;
                }
            }

            if (productsInBatch > 0)
                await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new
            {
                created,
                updated,
                imagesQueued = imageCount,
                imageStorageConfigured = r2.Value.IsConfigured,
                message = r2.Value.IsConfigured
                    ? "Sản phẩm đã lưu trong PostgreSQL; ảnh đang được chuyển sang R2 nền."
                    : "Sản phẩm đã lưu trong PostgreSQL; ảnh đang chờ cấu hình R2.",
            });
        })
        .DisableAntiforgery()
        .WithSummary("Import sản phẩm Excel vào PostgreSQL và xếp ảnh vào hàng đợi R2")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
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

    private sealed record PreparedProduct(string Sku, string? Barcode, string Name, string ItemType, string? CategoryPath,
        string? Brand, decimal? SalePrice, decimal? CostPrice, decimal? Stock, List<string> ImageUrls, string SourceDataJson);

    private sealed record ProductListItem(Guid Id, string Sku, string? Barcode, string Name, string ItemType,
        string? CategoryPath, string? Brand, decimal? SalePrice, decimal? CostPrice, decimal? Stock,
        Guid? ImageId, int ImagesPending, int ImagesFailed);
}

internal static class ProductStringExtensions
{
    public static string? TrimOrNull(this string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string TrimOrEmpty(this string value) => value.Trim();
}

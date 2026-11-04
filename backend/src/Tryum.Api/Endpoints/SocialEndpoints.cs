using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tryum.Api.Assistant;
using Tryum.Application.Common.Security;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>Kết nối fanpage và đăng bài trực tiếp từ Tryum qua Meta Graph API.</summary>
public static class SocialEndpoints
{
    private const string GraphBase = "https://graph.facebook.com/v21.0";

    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/social/facebook").WithTags("Social")
            .RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        group.MapGet("", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var tenant = await CurrentTenantAsync(db, ct);
            if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
            return Results.Ok(new
            {
                connected = !string.IsNullOrWhiteSpace(tenant.FacebookPageId)
                    && !string.IsNullOrWhiteSpace(tenant.FacebookPageAccessToken),
                pageId = tenant.FacebookPageId,
                pageName = tenant.FacebookPageName,
                tokenSet = !string.IsNullOrWhiteSpace(tenant.FacebookPageAccessToken),
            });
        });

        group.MapPut("", async (
            ConnectFacebookRequest request,
            ApplicationDbContext db,
            IHttpClientFactory clients,
            CancellationToken ct) =>
        {
            var pageId = request.PageId?.Trim();
            var token = request.AccessToken?.Trim();
            if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { message = "Nhập Page ID và Page Access Token." });

            try
            {
                var page = await ResolvePageAsync(clients, pageId, token, ct);
                var tenant = await CurrentTenantAsync(db, ct);
                if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
                tenant.FacebookPageId = page.Id;
                tenant.FacebookPageName = page.Name;
                tenant.FacebookPageAccessToken = page.AccessToken;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { connected = true, pageId = page.Id, pageName = page.Name });
            }
            catch (FacebookException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).WithSummary("Kết nối fanpage Facebook bằng Page Access Token");

        group.MapPost("/generate", async (
            GeneratePostRequest request,
            IOptions<AiChatOptions> options,
            IHttpClientFactory clients,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var topic = string.IsNullOrWhiteSpace(request.Topic)
                ? "Tryum - phần mềm quản lý bán hàng cho quán ăn và cửa hàng nhỏ"
                : request.Topic.Trim();
            var generated = await GenerateContentAsync(clients, options.Value, loggerFactory.CreateLogger("SocialAI"), topic, ct);
            return Results.Ok(generated);
        }).WithSummary("AI tạo tiêu đề, nội dung và ảnh cho bài fanpage");

        group.MapDelete("", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var tenant = await CurrentTenantAsync(db, ct);
            if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
            tenant.FacebookPageId = null;
            tenant.FacebookPageName = null;
            tenant.FacebookPageAccessToken = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { connected = false });
        }).WithSummary("Ngắt kết nối fanpage");

        group.MapPost("/posts", async (
            CreateFacebookPostRequest request,
            ApplicationDbContext db,
            IHttpClientFactory clients,
            CancellationToken ct) =>
        {
            var message = request.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return Results.BadRequest(new { message = "Nội dung bài đăng không được để trống." });
            if (message.Length > 5_000)
                return Results.BadRequest(new { message = "Nội dung tối đa 5.000 ký tự." });

            var tenant = await CurrentTenantAsync(db, ct);
            if (tenant is null) return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });
            if (string.IsNullOrWhiteSpace(tenant.FacebookPageId) || string.IsNullOrWhiteSpace(tenant.FacebookPageAccessToken))
                return Results.BadRequest(new { message = "Chưa kết nối fanpage Facebook." });

            try
            {
                var postId = await PublishAsync(clients, tenant.FacebookPageId, tenant.FacebookPageAccessToken, message, request.Link, request.ImageUrl, ct);
                return Results.Ok(new { id = postId, message = "Đã đăng bài lên fanpage." });
            }
            catch (FacebookException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).WithSummary("Đăng bài lên fanpage đã kết nối");

        return app;
    }

    /// <summary>
    /// Chấp nhận cả User Access Token và Page Access Token.
    /// User token được đổi thành page token qua /me/accounts trước khi lưu.
    /// </summary>
    private static async Task<GeneratedPost> GenerateContentAsync(
        IHttpClientFactory clients,
        AiChatOptions options,
        ILogger logger,
        string topic,
        CancellationToken ct)
    {
        var fallback = new GeneratedPost(
            "Tryum - Bán hàng nhẹ nhàng hơn",
            "Quán nhỏ cũng cần một hệ thống bán hàng tử tế.\n\nTryum giúp bạn bán hàng tại quầy, quản lý kho, khách hàng, in hóa đơn và tạo vận đơn Goship trong cùng một màn hình.\n\nDùng thử miễn phí tại tryum.vn.",
            ImageUrl("professional commercial advertising photo, hero product shot of a Vietnamese food or drink item, studio lighting, vibrant colors, clean minimal background, no text, no watermark, high detail"));

        if (string.IsNullOrWhiteSpace(options.GroqApiKey))
            return fallback;

        var system = """
            Bạn là copywriter Facebook cho Tryum - phần mềm quản lý bán hàng cho quán ăn và cửa hàng nhỏ tại Việt Nam.
            Hãy viết một bài đăng Facebook tự nhiên, không sáo rỗng, không dùng Markdown.
            Chỉ trả về JSON thuần theo đúng cấu trúc:
            {"title":"tiêu đề ngắn dưới 70 ký tự","content":"nội dung 3-6 đoạn ngắn, có CTA dùng thử tryum.vn","imagePrompt":"một câu tiếng Anh mô tả ảnh quảng cáo chuyên nghiệp: chủ thể sản phẩm/món ăn nổi bật chính giữa, ánh sáng studio, màu sắc tươi, nền sạch tối giản, không có chữ, không watermark, phong cách commercial advertising photography"}
            """;

        var user = $"Chủ đề bài đăng: {topic}. Viết cho fanpage Tryum, giọng thân thiện, đáng tin cậy, hướng tới chủ quán/cửa hàng nhỏ.";
        try
        {
            var client = clients.CreateClient("ai");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.GroqBaseUrl.TrimEnd('/') + "/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = options.GroqModel,
                    messages = new object[]
                    {
                        new { role = "system", content = system },
                        new { role = "user", content = user },
                    },
                    temperature = 0.7,
                    max_tokens = 800,
                }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GroqApiKey);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.UserAgent.ParseAdd("TryumSocial/1.0");

            using var response = await client.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Groq HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}");

            var root = JsonNode.Parse(body);
            var raw = root?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
            raw = raw.Trim();
            if (raw.StartsWith("```"))
            {
                var firstNewline = raw.IndexOf('\n');
                if (firstNewline >= 0) raw = raw[(firstNewline + 1)..];
                if (raw.EndsWith("```")) raw = raw[..^3];
            }

            var json = JsonNode.Parse(raw.Trim());
            var title = json?["title"]?.ToString()?.Trim();
            var content = json?["content"]?.ToString()?.Trim();
            var imagePrompt = json?["imagePrompt"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                return fallback;

            return new GeneratedPost(
                title,
                content,
                ImageUrl(string.IsNullOrWhiteSpace(imagePrompt)
                    ? "professional commercial advertising photo, hero product shot, studio lighting, vibrant colors, clean minimal background, no text, no watermark"
                    : imagePrompt));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI tạo bài Facebook lỗi, dùng nội dung mẫu");
            return fallback;
        }
    }

    private static string ImageUrl(string prompt)
        => $"https://image.pollinations.ai/prompt/{Uri.EscapeDataString(prompt)}?width=1200&height=630&nologo=true&model=flux&seed={Random.Shared.Next(1, 999_999)}";

    private static async Task<ResolvedPage> ResolvePageAsync(
        IHttpClientFactory clients,
        string pageId,
        string token,
        CancellationToken ct)
    {
        var client = clients.CreateClient("social");
        var accountsUrl = $"{GraphBase}/me/accounts?fields=id,name,access_token&access_token={Uri.EscapeDataString(token)}";
        try
        {
            using var response = await client.GetAsync(accountsUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var root = JsonNode.Parse(body);
            if (response.IsSuccessStatusCode)
            {
                var pages = (root?["data"] as JsonArray ?? new JsonArray())
                    .Where(node => node?["access_token"]?.ToString() is { Length: > 20 })
                    .ToList();
                var match = pages.FirstOrDefault(node => node?["id"]?.ToString() == pageId)
                    ?? (pages.Count == 1 ? pages[0] : null);
                if (match is not null)
                {
                    return new ResolvedPage(
                        match["id"]?.ToString() ?? pageId,
                        match["name"]?.ToString() ?? pageId,
                        match["access_token"]!.ToString());
                }
            }
        }
        catch
        {
            // Fall through to page-token lookup below.
        }

        var pageUrl = $"{GraphBase}/{Uri.EscapeDataString(pageId)}?fields=id,name&access_token={Uri.EscapeDataString(token)}";
        using var pageResponse = await client.GetAsync(pageUrl, ct);
        var pageBody = await pageResponse.Content.ReadAsStringAsync(ct);
        var pageRoot = JsonNode.Parse(pageBody);
        if (!pageResponse.IsSuccessStatusCode)
            throw new FacebookException(pageRoot?["error"]?["message"]?.ToString()
                ?? "Token không có quyền với Trang này. Hãy dùng User Token có pages_show_list hoặc Page Token đúng.");
        return new ResolvedPage(
            pageRoot?["id"]?.ToString() ?? pageId,
            pageRoot?["name"]?.ToString() ?? pageId,
            token);
    }

    private sealed record ResolvedPage(string Id, string Name, string AccessToken);

    private sealed class FacebookException(string message) : Exception(message);

    private static async Task<string> PublishAsync(
        IHttpClientFactory clients,
        string pageId,
        string token,
        string message,
        string? link,
        string? imageUrl,
        CancellationToken ct)
    {
        var client = clients.CreateClient("social");
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            using var photoResponse = await client.PostAsync(
                $"{GraphBase}/{Uri.EscapeDataString(pageId)}/photos",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["caption"] = message,
                    ["url"] = imageUrl.Trim(),
                    ["access_token"] = token,
                }),
                ct);
            var photoBody = await photoResponse.Content.ReadAsStringAsync(ct);
            var photoRoot = JsonNode.Parse(photoBody);
            if (!photoResponse.IsSuccessStatusCode)
                throw new FacebookException(photoRoot?["error"]?["message"]?.ToString() ?? "Facebook từ chối đăng ảnh.");
            return photoRoot?["post_id"]?.ToString() ?? photoRoot?["id"]?.ToString() ?? "published";
        }

        var form = new Dictionary<string, string>
        {
            ["message"] = message,
            ["access_token"] = token,
        };
        if (!string.IsNullOrWhiteSpace(link))
            form["link"] = link.Trim();

        using var response = await client.PostAsync(
            $"{GraphBase}/{Uri.EscapeDataString(pageId)}/feed",
            new FormUrlEncodedContent(form),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var root = JsonNode.Parse(body);
        if (!response.IsSuccessStatusCode)
            throw new FacebookException(root?["error"]?["message"]?.ToString() ?? "Facebook từ chối đăng bài.");
        return root?["id"]?.ToString() ?? "published";
    }

    private static async Task<Tryum.Domain.Tenants.Tenant?> CurrentTenantAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var tenantId = TenantContext.RequireCurrent();
        return await db.Tenants.FirstOrDefaultAsync(row => row.Id == tenantId, ct);
    }

    public sealed record ConnectFacebookRequest(string? PageId, string? AccessToken);
    public sealed record CreateFacebookPostRequest(string? Message, string? Link, string? ImageUrl);
    public sealed record GeneratePostRequest(string? Topic);
    public sealed record GeneratedPost(string Title, string Content, string ImageUrl);
}

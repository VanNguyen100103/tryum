using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Tryum.Api.Shipping;

public sealed class GoshipException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Client mỏng cho Goship API v2: địa chỉ, bảng giá và tạo vận đơn.</summary>
public sealed class GoshipClient(HttpClient http, IOptions<GoshipOptions> options, ILogger<GoshipClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GoshipOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.AccessToken) && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<IReadOnlyList<LocationOption>> CitiesAsync(CancellationToken ct)
        => ToLocations(await SendAsync(HttpMethod.Get, "/cities", null, ct));

    public async Task<IReadOnlyList<LocationOption>> DistrictsAsync(string cityId, CancellationToken ct)
        => ToLocations(await SendAsync(HttpMethod.Get, $"/cities/{Uri.EscapeDataString(cityId)}/districts", null, ct));

    public async Task<IReadOnlyList<LocationOption>> WardsAsync(string districtId, CancellationToken ct)
        => ToLocations(await SendAsync(HttpMethod.Get, $"/districts/{Uri.EscapeDataString(districtId)}/wards", null, ct));

    public Task<JsonNode> RatesAsync(object body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/rates", body, ct);

    public Task<JsonNode> CreateShipmentAsync(object body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/shipments", body, ct);

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new GoshipException("Chưa cấu hình Goship. Thêm GOSHIP_ACCESS_TOKEN vào cấu hình API.");

        using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new GoshipException("Không kết nối được Goship: " + ex.Message, ex);
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Goship {Method} {Path} -> {Status}: {Body}", method, path, (int)response.StatusCode, text);
            throw new GoshipException(ErrorMessage(text, (int)response.StatusCode));
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new GoshipException("Goship trả dữ liệu không hợp lệ.", ex);
        }

        if (root is null)
            throw new GoshipException("Goship trả dữ liệu rỗng.");

        var code = root["code"];
        if (code is not null && code.GetValueKind() == JsonValueKind.Number && code.GetValue<int>() != 200)
            throw new GoshipException(root["message"]?.GetValue<string>() ?? "Goship báo lỗi.");

        return root["data"] ?? root;
    }

    private static string ErrorMessage(string body, int status)
    {
        if (string.IsNullOrWhiteSpace(body))
            return $"Goship trả lỗi HTTP {status}.";
        try
        {
            var node = JsonNode.Parse(body);
            return node?["message"]?.GetValue<string>() ?? node?["error"]?.GetValue<string>() ?? body;
        }
        catch
        {
            return body;
        }
    }

    private static IReadOnlyList<LocationOption> ToLocations(JsonNode data)
    {
        var items = data is JsonArray array ? array : new JsonArray();
        return items
            .Select(node => new LocationOption(
                node?["id"]?.ToString() ?? string.Empty,
                node?["name"]?.GetValue<string>() ?? string.Empty))
            .Where(item => item.Id.Length > 0)
            .OrderBy(item => item.Name, StringComparer.Create(new System.Globalization.CultureInfo("vi-VN"), true))
            .ToList();
    }
}

public sealed record LocationOption(string Id, string Name);

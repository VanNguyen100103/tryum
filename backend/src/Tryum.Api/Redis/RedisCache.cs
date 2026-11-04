using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tryum.Api.Redis;

/// <summary>
/// Cache dùng chung Redis (Upstash). Mọi key đều có prefix "tryum:" để không đụng dự án khác.
/// Redis lỗi/chưa cấu hình thì tự fallback về nguồn gốc, không làm hỏng API.
/// </summary>
public sealed class RedisCache(IOptions<RedisOptions> options, ILogger<RedisCache> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RedisOptions _options = options.Value;
    private readonly object _gate = new();
    private IConnectionMultiplexer? _connection;
    private bool _attempted;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    public async Task<T> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var connection = Connection;
        if (connection is null)
            return await factory();

        var cacheKey = _options.Prefix + key;
        try
        {
            var cached = await connection.GetDatabase().StringGetAsync(cacheKey);
            if (cached.HasValue && cached.ToString() is { Length: > 0 } json)
            {
                var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (value is not null) return value;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis đọc cache {Key} lỗi, bỏ qua cache", cacheKey);
        }

        var fresh = await factory();
        try
        {
            await connection.GetDatabase().StringSetAsync(
                cacheKey,
                JsonSerializer.Serialize(fresh, JsonOptions),
                ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis ghi cache {Key} lỗi", cacheKey);
        }
        return fresh;
    }

    private IConnectionMultiplexer? Connection
    {
        get
        {
            if (!IsConfigured) return null;
            if (_connection is not null) return _connection;
            lock (_gate)
            {
                if (_connection is not null || _attempted) return _connection;
                _attempted = true;
                try
                {
                    var configuration = new ConfigurationOptions
                    {
                        EndPoints = { { _options.Host, _options.Port } },
                        Password = _options.Password,
                        Ssl = _options.Ssl,
                        AbortOnConnectFail = false,
                        ConnectTimeout = 3_000,
                        SyncTimeout = 3_000,
                        KeepAlive = 30,
                        DefaultDatabase = 0,
                    };
                    _connection = ConnectionMultiplexer.Connect(configuration);
                    logger.LogInformation("Đã kết nối Redis cache tại {Host}:{Port}", _options.Host, _options.Port);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Không kết nối được Redis cache, API sẽ chạy không cache");
                }
                return _connection;
            }
        }
    }
}

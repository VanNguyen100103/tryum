using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Store Redis dùng chung cho toàn bộ app (1 ConnectionMultiplexer duy nhất – Upstash free chỉ cho 1 kết nối).
/// Mọi key tự thêm prefix "tryum:". Redis lỗi/chưa cấu hình → không ném exception, caller tự fallback.
/// </summary>
public sealed class RedisStore(IOptions<RedisOptions> options, ILogger<RedisStore> logger) : IRedisStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RedisOptions _options = options.Value;
    private readonly object _gate = new();
    private IConnectionMultiplexer? _connection;
    private bool _attempted;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    private IDatabase? Database
    {
        get
        {
            var connection = Connection;
            return connection?.GetDatabase();
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var db = Database;
        if (db is null) return default;
        try
        {
            var value = await db.StringGetAsync(Key(key));
            if (value.HasValue && value.ToString() is { Length: > 0 } json)
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis đọc key {Key} lỗi, bỏ qua", key);
        }
        return default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var db = Database;
        if (db is null) return;
        try
        {
            await db.StringSetAsync(Key(key), JsonSerializer.Serialize(value, JsonOptions), ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis ghi key {Key} lỗi", key);
        }
    }

    public async Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var db = Database;
        if (db is null) return default;
        try
        {
            var value = await db.StringGetDeleteAsync(Key(key));
            if (value.HasValue && value.ToString() is { Length: > 0 } json)
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis GETDEL key {Key} lỗi", key);
        }
        return default;
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = Database;
        if (db is null) return false;
        try
        {
            return await db.KeyDeleteAsync(Key(key));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis xóa key {Key} lỗi", key);
            return false;
        }
    }

    public async Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = Database;
        if (db is null) return 0;
        try
        {
            return await db.StringIncrementAsync(Key(key));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis INCR key {Key} lỗi", key);
            return 0;
        }
    }

    public async Task SetExpiryAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var db = Database;
        if (db is null) return;
        try
        {
            await db.KeyExpireAsync(Key(key), ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis EXPIRE key {Key} lỗi", key);
        }
    }

    public async Task<T> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;

        var fresh = await factory();
        await SetAsync(key, fresh, ttl, cancellationToken);
        return fresh;
    }

    private RedisKey Key(string key) => _options.Prefix + key;

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
                    logger.LogInformation("Đã kết nối Redis tại {Host}:{Port}", _options.Host, _options.Port);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Không kết nối được Redis, API chạy không cache/state");
                }
                return _connection;
            }
        }
    }
}

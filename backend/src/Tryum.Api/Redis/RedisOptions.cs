namespace Tryum.Api.Redis;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 6379;
    public string Password { get; set; } = string.Empty;
    public bool Ssl { get; set; } = true;
    public string Prefix { get; set; } = "tryum:";
}

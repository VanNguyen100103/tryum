using System.Diagnostics;

namespace Tryum.Api.Common;

public static class Telemetry
{
    public const string SourceName = "Tryum.Api";
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}

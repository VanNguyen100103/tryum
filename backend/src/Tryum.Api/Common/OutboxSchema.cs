using Microsoft.EntityFrameworkCore;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Common;

/// <summary>Khởi tạo bảng outbox_messages (idempotent) vì bảng này chưa nằm trong migration.</summary>
public static class OutboxSchema
{
    public static async Task EnsureCreatedAsync(IServiceProvider services)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS outbox_messages (
                    "Id" uuid NOT NULL,
                    "EventType" text NOT NULL,
                    "RoutingKey" text NOT NULL,
                    "Payload" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "PublishedAt" timestamp with time zone NULL,
                    "RetryCount" integer NOT NULL,
                    "Error" text NULL,
                    CONSTRAINT "PK_outbox_messages" PRIMARY KEY ("Id")
                )
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_outbox_messages_PublishedAt"
                ON outbox_messages ("PublishedAt")
                """);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Không tạo được bảng outbox_messages");
        }
    }
}

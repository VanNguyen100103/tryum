# ── Build stage ──────────────────────────────────────────────────────────────
# Oracle Cloud Ampere A1 là ARM nên image được build với --platform linux/arm64
# (multi-arch, chạy được cả linux/amd64 nếu đổi VM).
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Restore trước, copy file csproj riêng để tận dụng cache Docker layer
COPY backend/src/Tryum.Domain/Tryum.Domain.csproj src/Tryum.Domain/
COPY backend/src/Tryum.Application/Tryum.Application.csproj src/Tryum.Application/
COPY backend/src/Tryum.Infrastructure/Tryum.Infrastructure.csproj src/Tryum.Infrastructure/
COPY backend/src/Tryum.Api/Tryum.Api.csproj src/Tryum.Api/
RUN dotnet restore src/Tryum.Api/Tryum.Api.csproj

# Copy source rồi build/publish Release
COPY backend/ ./
RUN dotnet publish src/Tryum.Api/Tryum.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

ENV ASPNETCORE_URL=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1 \
    DOTNET_TieredPMStubs=0

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Image .NET đã có sẵn user "app" (uid 1654) => chạy non-root, an toàn hơn root.
COPY --from=build /app/publish ./
RUN chown -R app:app /app
USER app

EXPOSE 8080

# Health check dùng cho `depends_on: service_healthy` và cho UptimeRobot
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["/app/Tryum.Api"]
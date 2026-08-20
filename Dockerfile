# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# MatSplit - multi stage image
#   build   : dotnet SDK 10.0, restores project files first (layer caching)
#   runtime : dotnet aspnet 10.0, non-root user "app", writable /data
# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_CONFIGURATION=Release
ARG APP_VERSION=local
ARG BUILD_NUMBER=0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip

WORKDIR /src

# 1) project/solution files only -> the restore layer survives source changes.
#    Directory.Build.props is optional (bracket glob) so the build also works
#    before the props file has been added.
COPY MatSplit.sln Directory.Build.prop[s] ./
COPY src/MatSplit.Web/MatSplit.Web.csproj src/MatSplit.Web/
RUN dotnet restore src/MatSplit.Web/MatSplit.Web.csproj

# 2) the rest of the sources
COPY . .

RUN dotnet publish src/MatSplit.Web/MatSplit.Web.csproj \
        --configuration "$BUILD_CONFIGURATION" \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false \
        -p:BuildNumber="$BUILD_NUMBER" \
        -p:InformationalVersion="$APP_VERSION"

# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ARG APP_VERSION=local
ARG VCS_REF=unknown
ARG BUILD_DATE=unknown

LABEL org.opencontainers.image.title="MatSplit" \
      org.opencontainers.image.description="Selfhosted shared expense tracking (Splid alternative)" \
      org.opencontainers.image.source="https://github.com/Real-TTX/MatSplit" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$VCS_REF" \
      org.opencontainers.image.created="$BUILD_DATE"

# HTTP_PORTS is emptied on purpose: the aspnet base image sets it to 8080 and
# Kestrel would log "Overriding HTTP_PORTS ... Binding to values defined by URLS"
# on every start. ASPNETCORE_URLS stays the single source of truth.
ENV ASPNETCORE_URLS=http://+:8080 \
    HTTP_PORTS="" \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    MATSPLIT_DATA_DIR=/data \
    TZ=Europe/Berlin

# curl is required for the container HEALTHCHECK (aspnet image ships without it)
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

# /data is the only writable location at runtime. Creating + chowning it here
# also gives a freshly created named volume the correct ownership, because
# Docker seeds new volumes from the image content.
RUN mkdir -p /data/db /data/config /data/receipts /data/keys /data/logs \
 && chown -R app:app /data \
 && chmod -R 0770 /data

USER app

EXPOSE 8080
VOLUME ["/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "MatSplit.Web.dll"]

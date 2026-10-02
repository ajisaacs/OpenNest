# Build from the repository root: docker build -f OpenNest.Server/Dockerfile -t opennest-server .
# Release builds pass --build-arg VERSION=X.Y.Z and SOURCE_REVISION=<full commit SHA>;
# the defaults mark an unversioned development build. Base images may be pinned by digest.
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY OpenNest.Server/OpenNest.Server.csproj OpenNest.Server/
COPY OpenNest.Data/OpenNest.Data.csproj OpenNest.Data/
COPY OpenNest.Core/OpenNest.Core.csproj OpenNest.Core/
RUN dotnet restore OpenNest.Server/OpenNest.Server.csproj

COPY OpenNest.Server/ OpenNest.Server/
COPY OpenNest.Data/ OpenNest.Data/
COPY OpenNest.Core/ OpenNest.Core/
ARG VERSION=0.0.0-dev
ARG SOURCE_REVISION=unknown
RUN dotnet publish OpenNest.Server/OpenNest.Server.csproj -c Release -o /app --no-restore \
    -p:Version="$VERSION" -p:SourceRevisionId="$SOURCE_REVISION"

FROM ${RUNTIME_IMAGE} AS runtime
# curl is installed only for the HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
# Runtime files stay root-owned and read-only to the app user.
COPY --from=build /app .

# SQLite database + .nest blobs live here; mount a volume at this path. The directory
# belongs to the base image's non-root app user (APP_UID 1654), so a fresh named volume
# inherits that ownership. Existing root-owned data needs a one-time scoped chown.
RUN mkdir -p /app/data && chown "$APP_UID:$APP_UID" /app/data && chmod 0750 /app/data
ENV OPENNEST_DB=/app/data/nests.db
# Listen on the explicit 8090 URL; clear the base image's 8080 port default.
ENV ASPNETCORE_URLS=http://+:8090
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 8090
USER $APP_UID
VOLUME /app/data

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --start-interval=2s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "http://127.0.0.1:8090/healthz"]

ARG VERSION=0.0.0-dev
ARG SOURCE_REVISION=unknown
ARG RUNTIME_IMAGE
LABEL org.opencontainers.image.title="OpenNest.Server" \
      org.opencontainers.image.description="OpenNest nest-storage HTTP service (SQLite)" \
      org.opencontainers.image.source="https://github.com/ajisaacs/OpenNest" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$SOURCE_REVISION" \
      org.opencontainers.image.base.name="$RUNTIME_IMAGE"

ENTRYPOINT ["dotnet", "OpenNest.Server.dll"]

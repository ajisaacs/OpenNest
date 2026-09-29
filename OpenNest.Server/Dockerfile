# Build from the repository root: docker build -f OpenNest.Server/Dockerfile -t opennest-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY OpenNest.Server/OpenNest.Server.csproj OpenNest.Server/
COPY OpenNest.Data/OpenNest.Data.csproj OpenNest.Data/
RUN dotnet restore OpenNest.Server/OpenNest.Server.csproj

COPY OpenNest.Server/ OpenNest.Server/
COPY OpenNest.Data/ OpenNest.Data/
RUN dotnet publish OpenNest.Server/OpenNest.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# SQLite database + .nest blobs live here; mount a volume at this path.
VOLUME /app/data
ENV OPENNEST_DB=/app/data/nests.db
ENV ASPNETCORE_URLS=http://+:8090
EXPOSE 8090

ENTRYPOINT ["dotnet", "OpenNest.Server.dll"]

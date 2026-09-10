# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first (layer cache for restore)
COPY RemoteAdminTool.sln ./
COPY Shared/Shared.csproj Shared/
COPY RelayServer/RelayServer.csproj RelayServer/

RUN dotnet restore RelayServer/RelayServer.csproj

# Copy source and publish
COPY Shared/ Shared/
COPY RelayServer/ RelayServer/

RUN dotnet publish RelayServer/RelayServer.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Render injects PORT env var — our app reads it as RAT_PORT
# Default to 10000 which is Render's expected port
ENV RAT_PORT=10000
ENV RAT_TLS=false

EXPOSE 10000

ENTRYPOINT ["dotnet", "RelayServer.dll"]

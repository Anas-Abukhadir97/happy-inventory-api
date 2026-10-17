FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/HappyInventory.Api/HappyInventory.Api.csproj --locked-mode
RUN dotnet publish src/HappyInventory.Api/HappyInventory.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.opencontainers.image.source="https://github.com/Anas-Abukhadir97/happy-inventory-api"
RUN apt-get update && apt-get install -y --no-install-recommends curl sqlite3 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /out/ ./
USER $APP_UID
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
HEALTHCHECK --interval=20s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null || exit 1
ENTRYPOINT ["dotnet", "HappyInventory.Api.dll"]

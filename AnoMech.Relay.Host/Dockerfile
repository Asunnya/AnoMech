FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG VERSION=0.0.0
WORKDIR /src

COPY AnoMech.Relay/AnoMech.Relay.csproj AnoMech.Relay/
COPY AnoMech.Relay.Host/AnoMech.Relay.Host.csproj AnoMech.Relay.Host/
RUN dotnet restore AnoMech.Relay.Host/AnoMech.Relay.Host.csproj

COPY AnoMech.Relay/ AnoMech.Relay/
COPY AnoMech.Relay.Host/ AnoMech.Relay.Host/
RUN dotnet publish AnoMech.Relay.Host/AnoMech.Relay.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false \
    -p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

USER app
EXPOSE 7890

ENTRYPOINT ["dotnet", "AnoMech.Relay.Host.dll"]

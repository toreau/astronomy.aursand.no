FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Astronomy.Worker/Astronomy.Worker.csproj .
RUN dotnet restore
COPY src/Astronomy.Worker/ .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0
RUN useradd --uid 10002 --create-home appuser
USER appuser
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Astronomy.Worker.dll", "heartbeat"]

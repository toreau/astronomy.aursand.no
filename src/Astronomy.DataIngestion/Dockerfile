FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ .
RUN dotnet restore Astronomy.DataIngestion/Astronomy.DataIngestion.csproj
RUN dotnet publish Astronomy.DataIngestion/Astronomy.DataIngestion.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Astronomy.DataIngestion.dll", "heartbeat"]

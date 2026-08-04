FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Astronomy.Api/Astronomy.Api.csproj .
RUN dotnet restore
COPY src/Astronomy.Api/ .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN useradd --uid 10001 --create-home appuser
USER appuser
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Astronomy.Api.dll"]

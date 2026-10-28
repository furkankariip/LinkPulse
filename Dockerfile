FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/LinkPulse.Api/LinkPulse.Api.csproj src/LinkPulse.Api/
RUN dotnet restore src/LinkPulse.Api/LinkPulse.Api.csproj

COPY src/ src/
RUN dotnet publish src/LinkPulse.Api/LinkPulse.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

RUN mkdir /data && chown $APP_UID /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/linkpulse.db"
VOLUME /data
EXPOSE 8080

COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "LinkPulse.Api.dll"]

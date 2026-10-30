FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY KieuViet.Assets.csproj ./
RUN dotnet restore KieuViet.Assets.csproj
COPY Program.cs Models.cs Rules.cs Security.cs ExcelEndpoints.cs ./
COPY appsettings.json ./
COPY wwwroot ./wwwroot
RUN dotnet publish KieuViet.Assets.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080
ENV ASPNETCORE_HTTP_PORTS=8080
# Vercel terminates TLS before forwarding requests to the container.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "KieuViet.Assets.dll"]

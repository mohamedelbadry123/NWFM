FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/NWFM.Api/NWFM.Api.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /var/nwfm/media /home/app/.aspnet/DataProtection-Keys \
    && chown -R "$APP_UID:$APP_UID" /var/nwfm /home/app/.aspnet
ENV HOME=/home/app
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "NWFM.Api.dll"]

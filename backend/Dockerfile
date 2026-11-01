# One image definition for both hosts: APP=NWFM.Api (tenant API) or APP=NWFM.Platform.Api (control plane).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP=NWFM.Api
WORKDIR /src
COPY . .
RUN dotnet publish src/${APP}/${APP}.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP=NWFM.Api
ENV APP_ASSEMBLY=${APP}.dll
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /var/nwfm/media /var/nwfm/emails \
    && chown -R "$APP_UID:$APP_UID" /var/nwfm
ENV HOME=/home/app
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_ASSEMBLY\""]

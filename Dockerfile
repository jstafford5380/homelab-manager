FROM node:24-alpine AS web
WORKDIR /work/src/homelab-web
COPY src/homelab-web/package*.json ./
RUN npm ci
COPY src/homelab-web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /work
COPY src/Homelab.Api/ src/Homelab.Api/
COPY src/Homelab.ServiceDefaults/ src/Homelab.ServiceDefaults/
COPY --from=web /work/src/Homelab.Api/wwwroot/ src/Homelab.Api/wwwroot/
RUN dotnet publish src/Homelab.Api -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER app
ENV ASPNETCORE_URLS=https://+:8443
ENV Homelab__DataPath=/data
EXPOSE 8443
ENTRYPOINT ["dotnet", "Homelab.Api.dll"]

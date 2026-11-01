FROM node:24-alpine AS web
WORKDIR /work/src/Web
COPY src/Web/package*.json ./
RUN npm ci
COPY src/Web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /work
COPY src/Api/Homelab.Api/ src/Api/Homelab.Api/
COPY src/Api/Homelab.ServiceDefaults/ src/Api/Homelab.ServiceDefaults/
COPY --from=web /work/src/Web/dist/ src/Api/Homelab.Api/wwwroot/
RUN dotnet publish src/Api/Homelab.Api -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER app
ENV ASPNETCORE_URLS=https://+:8443
ENV Homelab__DataPath=/data
EXPOSE 8443
ENTRYPOINT ["dotnet", "Homelab.Api.dll"]

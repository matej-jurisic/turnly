# syntax=docker/dockerfile:1

# 1) Build the frontend (React/Vite) into static assets.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2) Restore + publish the ASP.NET Core API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/Turnly.Core/Turnly.Core.csproj src/Turnly.Core/
COPY src/Turnly.Api/Turnly.Api.csproj src/Turnly.Api/
RUN dotnet restore src/Turnly.Api/Turnly.Api.csproj
COPY src/ src/
RUN dotnet publish src/Turnly.Api/Turnly.Api.csproj -c Release -o /app/publish

# 3) Runtime image: the API also serves the built frontend from wwwroot.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=api /app/publish ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Turnly.Api.dll"]

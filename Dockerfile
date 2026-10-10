FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/ src/
RUN dotnet publish src/FindThatBook.Api -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
LABEL org.opencontainers.image.source="https://github.com/juan-carlos-alvarez-ruiz/find-that-book"
WORKDIR /app
COPY --from=build /out ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FindThatBook.Api.dll"]

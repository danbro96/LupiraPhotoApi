# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy both project files before restore (the host references Core) so layer caching works.
COPY src/LupiraPhotoApi/LupiraPhotoApi.csproj src/LupiraPhotoApi/
COPY src/LupiraPhotoApi.Core/LupiraPhotoApi.Core.csproj src/LupiraPhotoApi.Core/
RUN dotnet restore src/LupiraPhotoApi/LupiraPhotoApi.csproj
COPY . .
RUN dotnet publish src/LupiraPhotoApi/LupiraPhotoApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl: compose healthcheck. ffmpeg: video poster frames + HEIC/AVIF stills the managed decoder can't read.
RUN apt-get update && apt-get install -y --no-install-recommends curl ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LupiraPhotoApi.dll"]

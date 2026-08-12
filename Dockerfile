# syntax=docker/dockerfile:1

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only project files first for better layer caching on restore.
COPY QueueManager.sln .
COPY QueueManager.Domain/QueueManager.Domain.csproj QueueManager.Domain/
COPY QueueManager.Api/QueueManager.Api.csproj QueueManager.Api/
COPY QueueManager.Tests/QueueManager.Tests.csproj QueueManager.Tests/
RUN dotnet restore QueueManager.Api/QueueManager.Api.csproj

# Copy the rest of the source and publish the API.
COPY QueueManager.Domain/ QueueManager.Domain/
COPY QueueManager.Api/ QueueManager.Api/
RUN dotnet publish QueueManager.Api/QueueManager.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for the running container.
RUN useradd --uid 10001 --user-group --create-home appuser
RUN mkdir -p /app/data && chown -R appuser:appuser /app

COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app

USER appuser
ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__QueueDb="Data Source=/app/data/queue.db"
EXPOSE 8080

ENTRYPOINT ["dotnet", "QueueManager.Api.dll"]

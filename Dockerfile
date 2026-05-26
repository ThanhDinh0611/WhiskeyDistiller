FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Copy solution and project files first to restore dependencies
COPY WhiskeyDistiller.slnx ./
COPY WhiskeyDistiller.Core/WhiskeyDistiller.Core.csproj ./WhiskeyDistiller.Core/
COPY WhiskeyDistiller.Api/WhiskeyDistiller.Api.csproj ./WhiskeyDistiller.Api/
COPY WhiskeyDistiller.Mcp/WhiskeyDistiller.Mcp.csproj ./WhiskeyDistiller.Mcp/
RUN dotnet restore

# Copy all source files and compile
COPY WhiskeyDistiller.Core/ ./WhiskeyDistiller.Core/
COPY WhiskeyDistiller.Api/ ./WhiskeyDistiller.Api/
COPY WhiskeyDistiller.Mcp/ ./WhiskeyDistiller.Mcp/
RUN dotnet publish WhiskeyDistiller.Api/WhiskeyDistiller.Api.csproj -c Release -o out

# Generate runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .

# Expose port and configure environment
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV WORKSPACE_PATH=/workspace

ENTRYPOINT ["dotnet", "WhiskeyDistiller.Api.dll"]

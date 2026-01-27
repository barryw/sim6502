# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy only main project for layer caching
COPY sim6502/sim6502.csproj sim6502/

# Restore only main project
RUN dotnet restore sim6502/sim6502.csproj

# Copy main project source
COPY sim6502/ sim6502/

# Publish
RUN dotnet publish sim6502/sim6502.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Sim6502TestRunner.dll"]

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy csproj files first for layer caching
COPY sim6502/sim6502.csproj sim6502/
COPY sim6502tests/sim6502tests.csproj sim6502tests/
COPY sim6502.sln .

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY . .

# Build
RUN dotnet build -c Release --no-restore

# Publish
RUN dotnet publish sim6502/sim6502.csproj -c Release -o /app/publish --no-build

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Sim6502TestRunner.dll"]

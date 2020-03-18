FROM mcr.microsoft.com/dotnet/core/sdk:3.1

WORKDIR /app

COPY . /app/
RUN dotnet build /app/sim6502.sln -c Release
RUN dotnet test /app/sim6502.sln -c Release

ENTRYPOINT ["dotnet","/app/sim6502/bin/Release/netcoreapp3.0/Sim6502TestRunner.dll"]

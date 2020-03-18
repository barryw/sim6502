FROM mcr.microsoft.com/dotnet/core/runtime:3.1-alpine

WORKDIR /app
COPY sim6502/bin/Release/netcoreapp3.0/*.dll /app/
COPY sim6502/bin/Release/netcoreapp3.0/Sim6502TestRunner.runtimeconfig.json /app
COPY sim6502/bin/Release/netcoreapp3.0/nlog.config /app

ENTRYPOINT ["dotnet","/app/Sim6502TestRunner.dll"]

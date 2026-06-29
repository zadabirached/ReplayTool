FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ReplayTool.Domain/ReplayTool.Domain.csproj                 ReplayTool.Domain/
COPY ReplayTool.Application/ReplayTool.Application.csproj       ReplayTool.Application/
COPY ReplayTool.Infrastructure/ReplayTool.Infrastructure.csproj ReplayTool.Infrastructure/
COPY ReplayTool.API/ReplayTool.API.csproj                        ReplayTool.API/
RUN dotnet restore ReplayTool.API/ReplayTool.API.csproj

COPY . .
RUN dotnet publish ReplayTool.API/ReplayTool.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReplayTool.API.dll"]

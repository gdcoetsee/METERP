# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the project files first (better layer caching)
COPY ["METERP.sln", "."]
COPY ["src/METERP.Domain/METERP.Domain.csproj", "src/METERP.Domain/"]
COPY ["src/METERP.Common/METERP.Common.csproj", "src/METERP.Common/"]
COPY ["src/METERP.Application/METERP.Application.csproj", "src/METERP.Application/"]
COPY ["src/METERP.Infrastructure/METERP.Infrastructure.csproj", "src/METERP.Infrastructure/"]
COPY ["src/METERP.Web/METERP.Web.csproj", "src/METERP.Web/"]

# Restore only the Web project (avoids test project errors)
RUN dotnet restore "src/METERP.Web/METERP.Web.csproj"

# Copy the rest of the source code
COPY . .

# Single publish step — avoids read-only bin/Release copy failures from a prior build layer
WORKDIR "/src/src/METERP.Web"
RUN dotnet publish "METERP.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "METERP.Web.dll"]
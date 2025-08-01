# Base image for .NET runtime
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# Copy and restore
COPY ["ResumeScannerFB.csproj", "./"]
RUN dotnet restore "ResumeScannerFB.csproj"

# Build
COPY . .
RUN dotnet publish "ResumeScannerFB.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ResumeScannerFB.dll"]

# Stage 1: Build & Restore
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for optimized Docker layer caching
COPY ["src/Sentravane.Shared/Sentravane.Shared.csproj", "src/Sentravane.Shared/"]
COPY ["src/Sentravane.Server.Web/Sentravane.Server.Web.csproj", "src/Sentravane.Server.Web/"]

RUN dotnet restore "src/Sentravane.Server.Web/Sentravane.Server.Web.csproj"

# Copy source tree and publish Release build
COPY src/ src/
WORKDIR "/src/src/Sentravane.Server.Web"
RUN dotnet publish "Sentravane.Server.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Minimal ASP.NET Core Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__DefaultConnection="Data Source=/data/sentravane_fleet.db"

RUN mkdir -p /data
VOLUME ["/data"]

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Sentravane.Server.Web.dll"]

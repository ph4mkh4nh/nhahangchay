# Dockerfile for ASP.NET Core MVC (.NET 8) on Render
# Put this file at the root of the project, same folder as nhahangchay.csproj

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Render web services use PORT. Program.cs also reads PORT.
EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["nhahangchay.csproj", "./"]
RUN dotnet restore "nhahangchay.csproj"

COPY . .
RUN dotnet publish "nhahangchay.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "nhahangchay.dll"]

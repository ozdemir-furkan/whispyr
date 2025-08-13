# ----- build stage -----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# tüm solution'ı kopyala
COPY . .

# restore & publish (API projesine hedef ver)
RUN dotnet restore src/Whispyr.Api/Whispyr.Api/Whispyr.Api.csproj
RUN dotnet publish src/Whispyr.Api/Whispyr.Api/Whispyr.Api.csproj -c Release -o /app/out

# ----- runtime stage -----
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# published output'u kopyala
COPY --from=build /app/out .

# konteyner içi port
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# prod ortam
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Whispyr.Api.dll"]

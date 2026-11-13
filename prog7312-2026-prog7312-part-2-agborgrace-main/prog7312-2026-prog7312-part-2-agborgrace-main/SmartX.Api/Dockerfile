# ---- Build stage ----
# NOTE: if you changed the TargetFramework to net8.0/net9.0, change the image tags below to match.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SmartX.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:5177
EXPOSE 5177
ENTRYPOINT ["dotnet", "SmartX.Api.dll"]

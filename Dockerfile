FROM mcr.microsoft.com/dotnet/sdk:9.0@sha256:cb9d975bf57fd1b0915858d1db1184bea20f7f746f0536323fcab49673144e8c AS build
WORKDIR /src

COPY ["CoreBackendApi.csproj", "./"]
RUN dotnet restore "CoreBackendApi.csproj"

COPY . .
RUN dotnet publish "CoreBackendApi.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0@sha256:86085bc68dde4a8cdfd8c2342acc3bf843eade855092879a85e3f3adb56e55c7 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CoreBackendApi.dll"]

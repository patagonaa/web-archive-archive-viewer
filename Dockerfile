FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /app/src
COPY src/ /app/src
RUN dotnet publish -c Release WebArchiveArchiveViewer/WebArchiveArchiveViewer.csproj -o /app/build

FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app
COPY --from=build /app/build/ ./
ENTRYPOINT ["dotnet", "WebArchiveArchiveViewer.dll"]
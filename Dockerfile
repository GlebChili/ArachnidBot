FROM mcr.microsoft.com/dotnet/sdk:7.0.201-jammy AS build
WORKDIR /src
COPY . /src
RUN dotnet publish ./ArachnidBot/ArachnidBot.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:7.0.3-jammy
WORKDIR /app
COPY --from=build /publish /app
ENTRYPOINT [ "dotnet", "ArachnidBot.dll" ]
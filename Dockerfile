FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY TelegramVideoBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:6.0 as final
WORKDIR /app
COPY --from=build /app .
RUN apt-get update -y
RUN apt-get install -y ffmpeg python3-pip git
RUN python3 -m pip install --upgrade git+https://github.com/yt-dlp/yt-dlp.git@release
ENTRYPOINT [ "dotnet", "TelegramVideoBot.dll" ]
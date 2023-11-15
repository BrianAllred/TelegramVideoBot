FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TelegramVideoBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --self-contained false

FROM linuxserver/ffmpeg as final
WORKDIR /app
COPY --from=build /app .
RUN apt-get update -y
RUN apt-get install -y python3-pip git aspnetcore-runtime-6.0
RUN python3 -m pip install --force-reinstall git+https://github.com/yt-dlp/yt-dlp.git@release
ENTRYPOINT [ "dotnet", "TelegramVideoBot.dll" ]
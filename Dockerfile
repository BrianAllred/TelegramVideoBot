FROM mcr.microsoft.com/dotnet/sdk:8.0.402 AS build
WORKDIR /src
COPY TelegramVideoBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --self-contained false

FROM linuxserver/ffmpeg AS final
WORKDIR /app
COPY --from=build /app .
COPY ./Scripts/install-dotnet8.sh .
RUN bash install-dotnet8.sh
RUN python3 -m pip install --force-reinstall --break-system-packages git+https://github.com/yt-dlp/yt-dlp.git@release
ENTRYPOINT [ "dotnet", "TelegramVideoBot.dll" ]
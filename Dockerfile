FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
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
RUN python3 -m pip install --force-reinstall --break-system-packages "yt-dlp[default] @ git+https://github.com/yt-dlp/yt-dlp.git@release"
ADD https://deno.land/install.sh install-deno.sh
RUN apt-get update && apt-get install --no-install-recommends -y unzip
RUN bash install-deno.sh -y
ENTRYPOINT [ "dotnet", "TelegramVideoBot.dll" ]
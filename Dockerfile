FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY TelegramVideoBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --self-contained false

FROM linuxserver/ffmpeg AS final
WORKDIR /app
COPY --from=build /app .
COPY --from=mcr.microsoft.com/dotnet/aspnet:11.0 /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
COPY ./Scripts/install-deps.sh .
RUN bash install-deps.sh
RUN python3 -m pip install --force-reinstall --break-system-packages "yt-dlp[default]"
ADD https://deno.land/install.sh install-deno.sh
RUN apt-get update && apt-get install --no-install-recommends -y unzip
RUN bash install-deno.sh -y
ENV PATH="/root/.deno/bin:$PATH"
ENTRYPOINT [ "dotnet", "TelegramVideoBot.dll" ]
# Telegram Video Bot

[![Docker](https://img.shields.io/docker/pulls/brianallred/telegram-video-bot)](https://hub.docker.com/r/brianallred/telegram-video-bot/)

Telegram bot that facilitates downloading videos from various websites and services in order to more easily to distribute them. This bot is mostly intended for shorter videos (for example, TikTok and Twitter). Due to public Telegram API limits, videos bigger than 50MB have to be transcoded to 50MB or less. This means long videos will take a long time to process and will be of (potentially greatly) reduced quality. This can be avoided by providing a private API server base URL.

Uses YT-DLP and FFMpeg under the hood. Supports hardware acceleration, but it will fall back to software transcoding.

A dependency has been added on [Deno](https://deno.com/) (or similar EJS) as per the [YT-DLP documentation](https://github.com/yt-dlp/yt-dlp/wiki/EJS). The Telegram Video Bot Docker image handles this dependency automatically.

## Usage

### Environment Variables

- `TG_BOT_TOKEN`: Bot token obtained from Botfather. Required.
- `TG_BOT_NAME`: Name used in `/help` and `/start` command text. Optional, defaults to `Frozen's Video Bot`.
- `TG_API_SERVER`: Base URL of the Telegram API server to use. Optional, defaults to the public API.
- `UPDATE_YTDLP_ON_START`: Update the local installation of YT-DLP on start. Optional, defaults to false. Highly recommended in container deployments.
- `YTDLP_UPDATE_BRANCH`: The code branch to use when YT-DLP updates on start (if `UPDATE_YTDLP_ON_START` is true). Optional, defaults to `release`.
- `DOWNLOAD_QUEUE_LIMIT`: Number of videos allowed in each user's download queue. Optional, defaults to 5.
- `FILE_SIZE_LIMIT`: File size limit of videos in megabytes. Optional, defaults to 50.
- `TZ`: Timezone. Optional, defaults to UTC.

### Docker Compose

```Docker
version: '3.8'

services:
  video-bot:
    image: brianallred/telegram-video-bot
    container_name: tg-video-bot
    environment:
      - TZ=America/Chicago
      - TG_BOT_TOKEN=<bot token>
      - TG_BOT_NAME=<bot name>
      - UPDATE_YTDLP_ON_START=true
      - DOWNLOAD_QUEUE_LIMIT=5
    deploy:
      resources:
        reservations:
          devices:
            - capabilities: [gpu]
    devices:
      - /dev/dri:/dev/dri
      - /dev/nvidia0:/dev/nvidia0
      - /dev/nvidiactl:/dev/nvidiactl
      - /dev/nvidia-modeset:/dev/nvidia-modeset
      - /dev/nvidia-uvm:/dev/nvidia-uvm
      - /dev/nvidia-uvm-tools:/dev/nvidia-uvm-tools
```

This example enables hardware acceleration via `/dev/dri` (for VAAPI) and via the various nvidia settings (for CUDA). FFMpeg is set to auto detect the best method.

### Docker Run

`docker run -d --name tg-video-bot -e TG_BOT_TOKEN=<bot token>`

For help exposing hardware using `docker run`, refer to [nvidia's documentation](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/user-guide.html).

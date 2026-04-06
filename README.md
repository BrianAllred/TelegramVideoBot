# Telegram Video Bot

[![Docker](https://img.shields.io/docker/pulls/brianallred/telegram-video-bot)](https://hub.docker.com/r/brianallred/telegram-video-bot/)

Telegram bot that facilitates downloading videos from various websites and services in order to more easily to distribute them. This bot is mostly intended for shorter videos (for example, TikTok and Twitter). Due to public Telegram API limits, videos bigger than 50MB have to be transcoded to 50MB or less. This means long videos will take a long time to process and will be of (potentially greatly) reduced quality. This can be avoided by providing a private API server base URL.

Optionally, S3-compatible storage (AWS S3, Garage, MinIO, Backblaze B2, etc.) can be configured so users can retrieve the original, untranscoded video. When a video is transcoded, the bot attaches a "Get original" button to the sent video. Pressing it uploads the original file to S3 and sends back a time-limited presigned download URL.

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

#### S3 Storage (optional)

When configured, users can retrieve the original untranscoded video via a presigned download URL.

- `S3_ENDPOINT`: S3-compatible endpoint URL (e.g. `https://s3.garage.example.com`). Optional, omit for AWS S3.
- `S3_ACCESS_KEY`: S3 access key. Required to enable S3.
- `S3_SECRET_KEY`: S3 secret key. Required to enable S3.
- `S3_BUCKET`: Bucket name for video storage. Required to enable S3.
- `S3_REGION`: Region. Optional, defaults to `us-east-1`.
- `S3_FORCE_PATH_STYLE`: Force path-style addressing. Optional, defaults to `false`. Required for many S3-compatible services (Garage, MinIO, etc.).
- `S3_DISABLE_PAYLOAD_SIGNING`: Disable chunked payload signing for uploads. Optional, defaults to `false`. Required for some S3-compatible services (Garage, MinIO, etc.) that don't support streaming signatures.
- `S3_PRESIGN_EXPIRY_DAYS`: Presigned URL expiry in days. Optional, defaults to `3`.

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
      # Optional: S3 storage for original videos
      # - S3_ENDPOINT=https://s3.garage.example.com
      # - S3_ACCESS_KEY=<access key>
      # - S3_SECRET_KEY=<secret key>
      # - S3_BUCKET=videos
      # - S3_FORCE_PATH_STYLE=true
      # - S3_DISABLE_PAYLOAD_SIGNING=true
      # - S3_PRESIGN_EXPIRY_DAYS=3
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

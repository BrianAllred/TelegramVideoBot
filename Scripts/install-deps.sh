#!/bin/bash

# Set noninteractive
export DEBIAN_FRONTEND=noninteractive

# Update packages
apt-get update -y
apt-get upgrade -y

# Install yt-dlp deps and ICU for the .NET runtime (copied from the aspnet image)
apt-get install -y python3-pip git '^libicu[0-9]+$'

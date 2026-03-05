#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
  echo "Please run as root: sudo bash scripts/setup_ubuntu_ar_env.sh"
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y \
  adb \
  openjdk-17-jdk \
  unzip \
  curl \
  wget

echo "Ubuntu-side dependencies installed: adb, openjdk-17-jdk, unzip, curl, wget"

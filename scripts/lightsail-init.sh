#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# scripts/lightsail-init.sh
# 
# Usage:
#   ./scripts/lightsail-init.sh [INSTANCE_NAME] [KEY_PATH] [BUNDLE_ID]
#
# Defaults:
#   INSTANCE_NAME: balloonmesh-server
#   KEY_PATH:      ~/.ssh/isucon-key.pem (or ~/.ssh/id_rsa)
#   BUNDLE_ID:     small_3_0 ($12/mo, 2GB RAM / 2 vCPU) or micro_3_0 ($7/mo)
# -----------------------------------------------------------------------------

INSTANCE_NAME="${1:-balloonmesh-server}"
KEY_PATH="${2:-${LIGHTSAIL_KEY:-}}"
BUNDLE_ID="${3:-small_3_0}"
REGION="${AWS_DEFAULT_REGION:-ap-northeast-1}"
KEY_NAME="${INSTANCE_NAME}-key"

# SSH 鍵の自動検出
if [[ -z "$KEY_PATH" ]]; then
  for candidate in ~/.ssh/isucon-key.pem ~/.ssh/id_ed25519 ~/.ssh/id_rsa; do
    if [[ -f "$candidate" ]]; then
      KEY_PATH="$candidate"
      break
    fi
  done
fi

if [[ -z "$KEY_PATH" || ! -f "${KEY_PATH/#\~/$HOME}" ]]; then
  echo "Error: Private key not found. Please specify key path: $0 <INSTANCE_NAME> <KEY_PATH>"
  exit 1
fi
KEY_PATH="${KEY_PATH/#\~/$HOME}"

echo "==> 1. Registering SSH public key to Lightsail (${KEY_NAME})..."
PUBKEY=$(ssh-keygen -y -f "$KEY_PATH")
if ! aws lightsail get-key-pair --key-pair-name "$KEY_NAME" >/dev/null 2>&1; then
  aws lightsail import-key-pair --key-pair-name "$KEY_NAME" --public-key-base64 "$PUBKEY" >/dev/null
  echo "Key pair imported."
else
  echo "Key pair already registered."
fi

echo "==> 2. Creating Lightsail instance (${INSTANCE_NAME}, ${BUNDLE_ID})..."
TMP_USERDATA=$(mktemp)
cat << 'EOF' > "$TMP_USERDATA"
#!/bin/bash
apt-get update
apt-get install -y docker.io docker-compose-v2 git rsync
usermod -aG docker ubuntu
EOF

if ! aws lightsail get-instance --instance-name "$INSTANCE_NAME" >/dev/null 2>&1; then
  aws lightsail create-instances \
    --instance-names "$INSTANCE_NAME" \
    --availability-zone "${REGION}a" \
    --blueprint-id ubuntu_24_04 \
    --bundle-id "$BUNDLE_ID" \
    --key-pair-name "$KEY_NAME" \
    --user-data "file://$TMP_USERDATA" >/dev/null
  echo "Instance creation initiated."
else
  echo "Instance already exists."
fi
rm -f "$TMP_USERDATA"

echo "==> 3. Waiting for instance to start..."
while true; do
  STATE=$(aws lightsail get-instance --instance-name "$INSTANCE_NAME" --query "instance.state.name" --output text 2>/dev/null || echo "pending")
  IP=$(aws lightsail get-instance --instance-name "$INSTANCE_NAME" --query "instance.publicIpAddress" --output text 2>/dev/null || echo "None")
  if [[ "$STATE" == "running" && -n "$IP" && "$IP" != "None" ]]; then
    echo "Instance is running! Public IP: $IP"
    break
  fi
  sleep 4
done

echo "==> 4. Opening port 5080..."
aws lightsail open-instance-public-ports \
  --instance-name "$INSTANCE_NAME" \
  --port-info fromPort=5080,toPort=5080,protocol=tcp >/dev/null

echo "==> 5. Waiting for SSH & Docker daemon to become ready..."
while ! ssh -i "$KEY_PATH" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=5 "ubuntu@$IP" "docker --version" >/dev/null 2>&1; do
  sleep 4
done

echo "==> 6. Triggering initial deployment..."
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$DIR/lightsail-deploy.sh" "$IP" "$KEY_PATH"

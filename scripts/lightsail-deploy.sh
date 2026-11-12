#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# scripts/lightsail-deploy.sh
# 
# Usage:
#   ./scripts/lightsail-deploy.sh <HOST_IP> [SSH_KEY_PATH]
# Or:
#   export LIGHTSAIL_HOST="57.xxx.xxx.xxx"
#   export LIGHTSAIL_KEY="~/.ssh/your-key.pem"
#   ./scripts/lightsail-deploy.sh
# -----------------------------------------------------------------------------

HOST="${1:-${LIGHTSAIL_HOST:-}}"
KEY="${2:-${LIGHTSAIL_KEY:-}}"
USER="ubuntu"
PORT="5080"
REMOTE_DIR="~/BalloonMesh"

if [[ -z "$HOST" ]]; then
  echo "Error: Host IP is required."
  echo "Usage: $0 <HOST_IP> [SSH_KEY_PATH]"
  exit 1
fi

SSH_OPTS=(-o "StrictHostKeyChecking=accept-new" -o "ConnectTimeout=10")
if [[ -n "$KEY" ]]; then
  # チルダを展開
  KEY="${KEY/#\~/$HOME}"
  SSH_OPTS+=(-i "$KEY")
fi

echo "==> 1. Syncing project files to ${USER}@${HOST}:${REMOTE_DIR}..."
rsync -avz \
  --delete \
  --exclude='.git' \
  --exclude='.worktrees' \
  --exclude='firmware' \
  --exclude='docs' \
  --exclude='bin' \
  --exclude='obj' \
  -e "ssh ${SSH_OPTS[*]}" \
  ./ "${USER}@${HOST}:${REMOTE_DIR}/"

echo "==> 2. Rebuilding and starting containers on remote host..."
ssh "${SSH_OPTS[@]}" "${USER}@${HOST}" "cd ${REMOTE_DIR} && docker compose up -d --build"

echo "==> 3. Verifying deployment health..."
sleep 3
HEALTH_URL="http://${HOST}:${PORT}/api/health"
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$HEALTH_URL" || echo "000")

if [[ "$HTTP_STATUS" == "200" ]]; then
  echo "=========================================================="
  echo " Deployment Succeeded!"
  echo " Live UI: http://${HOST}:${PORT}/"
  echo "=========================================================="
else
  echo "Warning: Healthcheck returned HTTP $HTTP_STATUS at $HEALTH_URL."
  echo "Check remote logs with: ssh ${SSH_OPTS[*]} ${USER}@${HOST} 'cd ${REMOTE_DIR} && docker compose logs -f server'"
fi

#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# scripts/lightsail-deploy.sh
# 
# Usage:
#   ./scripts/lightsail-deploy.sh <HOST_IP> [SSH_KEY_PATH] [--prod|--demo]
#
# Examples:
#   # デモモード（仮想バルーン50台を自動生成）でデプロイ（デフォルト）
#   ./scripts/lightsail-deploy.sh 57.xxx.xxx.xxx ~/.ssh/key.pem --demo
#
#   # 本番・実機接続モード（デモ停止、実機ESP32 Gatewayの待受状態）でデプロイ
#   ./scripts/lightsail-deploy.sh 57.xxx.xxx.xxx ~/.ssh/key.pem --prod
# -----------------------------------------------------------------------------

HOST="${1:-${LIGHTSAIL_HOST:-}}"
KEY="${2:-${LIGHTSAIL_KEY:-}}"
MODE="${3:-${DEPLOY_MODE:---demo}}"

# 引数の順序の柔軟性（2番目に --prod/--demo が来た場合）
if [[ "$KEY" == "--prod" || "$KEY" == "--demo" ]]; then
  MODE="$KEY"
  KEY=""
fi

if [[ "$MODE" == "--prod" ]]; then
  SIMULATION_ENABLED="false"
  MODE_LABEL="Production / Hardware Mode (Simulation disabled)"
else
  SIMULATION_ENABLED="true"
  MODE_LABEL="Demo Mode (Simulation enabled with 50 virtual balloons)"
fi

USER="ubuntu"
PORT="5080"
REMOTE_DIR="~/BalloonMesh"

if [[ -z "$HOST" ]]; then
  echo "Error: Host IP is required."
  echo "Usage: $0 <HOST_IP> [SSH_KEY_PATH] [--prod|--demo]"
  exit 1
fi

SSH_OPTS=(-o "StrictHostKeyChecking=accept-new" -o "ConnectTimeout=10")
if [[ -n "$KEY" ]]; then
  KEY="${KEY/#\~/$HOME}"
  SSH_OPTS+=(-i "$KEY")
fi

echo "==> Deploy Mode: ${MODE_LABEL}"
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

echo "==> 2. Rebuilding and starting containers (SIMULATION_ENABLED=${SIMULATION_ENABLED})..."
ssh "${SSH_OPTS[@]}" "${USER}@${HOST}" "cd ${REMOTE_DIR} && export SIMULATION_ENABLED=${SIMULATION_ENABLED} && docker compose up -d --build"

echo "==> 3. Verifying deployment health..."
sleep 3
HEALTH_URL="http://${HOST}:${PORT}/api/health"
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$HEALTH_URL" || echo "000")

if [[ "$HTTP_STATUS" == "200" ]]; then
  echo "=========================================================="
  echo " Deployment Succeeded! [Mode: ${MODE_LABEL}]"
  echo " Live UI: http://${HOST}:${PORT}/"
  echo "=========================================================="
else
  echo "Warning: Healthcheck returned HTTP $HTTP_STATUS at $HEALTH_URL."
  echo "Check remote logs with: ssh ${SSH_OPTS[*]} ${USER}@${HOST} 'cd ${REMOTE_DIR} && docker compose logs -f server'"
fi

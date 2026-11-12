#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# scripts/lightsail-destroy.sh
# 
# Usage:
#   ./scripts/lightsail-destroy.sh [INSTANCE_NAME]
# -----------------------------------------------------------------------------

INSTANCE_NAME="${1:-balloonmesh-server}"
KEY_NAME="${INSTANCE_NAME}-key"

echo "==> Deleting Lightsail instance: ${INSTANCE_NAME}..."
if aws lightsail get-instance --instance-name "$INSTANCE_NAME" >/dev/null 2>&1; then
  aws lightsail delete-instance --instance-name "$INSTANCE_NAME" >/dev/null
  echo "Instance ${INSTANCE_NAME} deleted."
else
  echo "Instance ${INSTANCE_NAME} not found."
fi

echo "==> Deleting registered SSH key pair: ${KEY_NAME}..."
if aws lightsail get-key-pair --key-pair-name "$KEY_NAME" >/dev/null 2>&1; then
  aws lightsail delete-key-pair --key-pair-name "$KEY_NAME" >/dev/null
  echo "Key pair ${KEY_NAME} deleted."
fi

echo "=========================================================="
echo " All Lightsail resources destroyed. Billing stopped."
echo "=========================================================="

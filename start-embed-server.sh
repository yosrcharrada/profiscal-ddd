#!/usr/bin/env bash
# Boots the semantic-search embedding server on http://127.0.0.1:8081
# First run downloads the multilingual model (~1GB) and is slow; later runs are fast.
set -e
cd "$(dirname "$0")"

if [ ! -d "venv" ]; then
  echo "→ Creating Python venv…"
  python3 -m venv venv
  ./venv/bin/pip install --quiet --upgrade pip
  ./venv/bin/pip install -r embed_requirements.txt
fi

# Load .env if present (NEO4J_* vars)
if [ -f .env ]; then
  set -a
  # shellcheck disable=SC1091
  . ./.env
  set +a
fi

echo "→ Starting embed server on :8081 (Ctrl+C to stop)…"
./venv/bin/python embed_server.py

#!/usr/bin/env bash
# One-shot local environment setup for pneumatic-sorting-digital-twin
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

echo "==> Project: $ROOT"
echo "==> Required Unity Editor: 6000.6.0f1"

# --- Git LFS ---
if ! command -v git-lfs >/dev/null 2>&1; then
  echo "==> Installing Git LFS via Homebrew..."
  if ! command -v brew >/dev/null 2>&1; then
    echo "Homebrew is required. Install from https://brew.sh then re-run this script."
    exit 1
  fi
  brew install git-lfs
fi

echo "==> Configuring Git LFS for this repo..."
git lfs install --local

echo "==> Pulling LFS CAD assets (*.obj)..."
if ! git lfs pull; then
  cat <<'EOF'

ERROR: git lfs pull failed.
If you see "exceeded its LFS budget", the GitHub repo owner
(Galabavamsi/pneumatic-sorting-digital-twin) must buy more Git LFS
storage/bandwidth, or host the .obj files elsewhere.

EOF
  exit 1
fi

echo "==> Verifying CAD meshes are real files (not LFS pointers)..."
for f in \
  Assets/Models/Kit1/Kit1_components.obj \
  Assets/Models/Kit2/Kit2_components.obj \
  Assets/Models/Kit3/Kit3_components.obj
do
  size=$(wc -c < "$f" | tr -d ' ')
  if head -1 "$f" | grep -q 'git-lfs.github.com'; then
    echo "  FAIL: $f is still an LFS pointer ($size bytes)"
    exit 1
  fi
  echo "  OK: $f ($size bytes)"
done

# --- Unity Hub / Editor ---
HUB="/Applications/Unity Hub.app"
EDITOR_DIR="/Applications/Unity/Hub/Editor/6000.6.0f1"
if [[ ! -d "$HUB" ]]; then
  echo ""
  echo "==> Unity Hub not found."
  echo "    Download: https://unity.com/download"
  exit 1
fi

echo "==> Unity Hub found: $HUB"
if [[ -d "$EDITOR_DIR" ]]; then
  echo "==> Unity Editor 6000.6.0f1 already installed."
else
  cat <<'EOF'

==> Unity Editor 6000.6.0f1 is not installed yet.

In Unity Hub:
  1. Sign in (Personal license is fine)
  2. Installs → Install Editor → archive → 6000.6.0f1
     or open this project and accept the install prompt
  3. Projects → Add → Add project from disk → this folder
  4. Open with 6000.6.0f1
  5. Open Assets/Scenes/Kit1Viewer.unity (or Kit2/Kit3) and press Play

Direct editor install page:
  https://unity.com/releases/editor/whats-new/6000.6.0

EOF
fi

# Cleanup local bootstrap tools if present
if [[ -d "$ROOT/.tools" ]]; then
  echo "==> Removing temporary .tools bootstrap directory..."
  rm -rf "$ROOT/.tools"
fi

echo ""
echo "Environment setup complete for what we can automate."
echo "Next: open this folder in Unity Hub with editor 6000.6.0f1."

#!/usr/bin/env bash
#
# Installs the ServerMonitor agent on this machine as a systemd service.
#
#   sudo ./deploy/agent/install.sh
#   sudo ./deploy/agent/install.sh --reconfigure     # change the address or token
#
# Run it from a clone of the repository. It builds the agent, installs it under
# /opt/servermonitor-agent and starts it. Running it again upgrades in place: the agent's identity
# (agent-state.json) and its configuration are kept.
#
# Settings may come from the environment instead of prompts:
#   SERVERMONITOR_URL    address of the API, for example http://192.168.1.20:7212
#   SERVERMONITOR_TOKEN  the enrollment token, ENROLLMENT_TOKEN in the server's .env

set -euo pipefail

readonly INSTALL_DIR=/opt/servermonitor-agent
readonly ENV_FILE=/etc/servermonitor-agent.env
readonly UNIT_NAME=servermonitor-agent
readonly UNIT_FILE=/etc/systemd/system/${UNIT_NAME}.service
readonly SERVICE_USER=servermonitor

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
readonly SCRIPT_DIR REPO_ROOT

die() {
  echo "error: $*" >&2
  exit 1
}

info() {
  echo "==> $*"
}

[[ $EUID -eq 0 ]] || die "run as root: sudo $0"
[[ -f "$REPO_ROOT/ServerMonitor.Agent/ServerMonitor.Agent.csproj" ]] \
  || die "run this from a clone of the ServerMonitor repository"
command -v systemctl >/dev/null || die "systemd is required"

case "$(uname -m)" in
  x86_64) RID=linux-x64 ;;
  aarch64) RID=linux-arm64 ;;
  *) die "unsupported architecture: $(uname -m)" ;;
esac
readonly RID

BUILD_DIR="$(mktemp -d)"
readonly BUILD_DIR
trap 'rm -rf "$BUILD_DIR"' EXIT

# Self-contained, so the machine being watched needs no .NET runtime. Built inside the SDK image
# when Docker is present, so the SDK does not need installing either: a monitored machine should
# carry as little as possible that is not its job.
info "Building the agent for $RID"
if command -v docker >/dev/null; then
  # The source is mounted read-only and copied inside the container, because publish writes bin
  # and obj next to the project and the checkout should come out of this untouched. Any bin and
  # obj already in the checkout stay behind on the way in: a restore made on another machine
  # records that machine's paths, and publishing on top of it fails in confusing ways.
  docker run --rm \
    -v "$REPO_ROOT":/src:ro \
    -v "$BUILD_DIR":/out \
    mcr.microsoft.com/dotnet/sdk:10.0 \
    sh -c "mkdir /tmp/src && tar -C /src --exclude=.git --exclude=bin --exclude=obj -cf - . | tar -C /tmp/src -xf - && cd /tmp/src && dotnet publish ServerMonitor.Agent -c Release -r $RID --self-contained true -p:PublishSingleFile=true -o /out"
elif command -v dotnet >/dev/null; then
  dotnet publish "$REPO_ROOT/ServerMonitor.Agent" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -o "$BUILD_DIR"
else
  die "neither Docker nor the .NET SDK is installed; install one of them and run again"
fi

[[ -f "$BUILD_DIR/ServerMonitor.Agent" ]] || die "the build did not produce ServerMonitor.Agent"

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  info "Creating the system user $SERVICE_USER"
  useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# Stopped first: overwriting the executable of a running process fails with "text file busy".
systemctl stop "$UNIT_NAME" 2>/dev/null || true

# cp adds and overwrites but never deletes, which is exactly what an upgrade needs: agent-state.json
# holds the key this machine was issued, and losing it would mean registering again and appearing
# in the fleet twice.
info "Installing to $INSTALL_DIR"
install -d -m 0750 -o "$SERVICE_USER" -g "$SERVICE_USER" "$INSTALL_DIR"
cp -r "$BUILD_DIR"/. "$INSTALL_DIR"/
chown -R "$SERVICE_USER":"$SERVICE_USER" "$INSTALL_DIR"
chmod 0755 "$INSTALL_DIR/ServerMonitor.Agent"

if [[ -f "$ENV_FILE" && "${1:-}" != "--reconfigure" ]]; then
  info "Keeping the configuration in $ENV_FILE (pass --reconfigure to replace it)"
else
  url="${SERVERMONITOR_URL:-}"
  token="${SERVERMONITOR_TOKEN:-}"

  if [[ -z "$url" ]]; then
    read -r -p "API address [http://localhost:7212]: " url
    url="${url:-http://localhost:7212}"
  fi

  # Read without echo, so the token does not stay behind in the terminal's scrollback.
  if [[ -z "$token" ]]; then
    read -r -s -p "Enrollment token (ENROLLMENT_TOKEN from the server's .env): " token
    echo
  fi

  [[ -n "$token" ]] || die "an enrollment token is needed for the first registration"

  info "Writing $ENV_FILE"
  # Created with restrictive permissions from the first byte, rather than written and then
  # tightened — in between, the token would be readable by everyone.
  (
    umask 077
    printf 'Agent__ServerUrl=%s\nAgent__EnrollmentToken=%s\n' "$url" "$token" > "$ENV_FILE"
  )
  chown root:root "$ENV_FILE"
fi

info "Installing the systemd unit"
install -m 0644 "$SCRIPT_DIR/${UNIT_NAME}.service" "$UNIT_FILE"
systemctl daemon-reload
systemctl enable --now "$UNIT_NAME"

# Active is not the same as working: an agent retrying against an address that does not answer is
# active too. So wait for the one thing that proves registration succeeded, the state file, or for
# the service to stop, which is what the agent does with a token it cannot use.
info "Waiting for the agent to register"
for _ in $(seq 1 20); do
  [[ -f "$INSTALL_DIR/agent-state.json" ]] && break
  systemctl is-active --quiet "$UNIT_NAME" || break
  sleep 1
done

if ! systemctl is-active --quiet "$UNIT_NAME"; then
  echo "The agent stopped. Its recent log:" >&2
  journalctl -u "$UNIT_NAME" -n 30 --no-pager >&2
  exit 1
fi

if [[ ! -f "$INSTALL_DIR/agent-state.json" ]]; then
  echo "The agent is running but has not registered yet. It keeps retrying on its own;" >&2
  echo "if this lasts, the API address is probably wrong or unreachable. Recent log:" >&2
  journalctl -u "$UNIT_NAME" -n 15 --no-pager >&2
fi

cat <<EOF

The agent is running. It appears in the fleet once its first reading arrives.

  systemctl status $UNIT_NAME        is it running
  journalctl -u $UNIT_NAME -f        follow its log
  sudo $0 --reconfigure              change the address or the token
EOF

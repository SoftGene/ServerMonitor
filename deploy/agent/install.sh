#!/usr/bin/env bash
#
# Installs the ServerMonitor agent on this machine as a systemd service.
#
#   curl -fsSL https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent/install.sh \
#     | sudo bash -s -- --url http://192.168.1.20:7212 --token <token>
#
# The dashboard's "Add a machine" page shows that command with both values filled in. Running it
# again upgrades the agent in place and keeps the machine's identity, agent-state.json.
#
# --help lists the options. The address and the token may also come from SERVERMONITOR_URL and
# SERVERMONITOR_TOKEN, which keeps the token off the command line.

set -euo pipefail

readonly REPO=SoftGene/ServerMonitor
readonly INSTALL_DIR=/opt/servermonitor-agent
readonly ENV_FILE=/etc/servermonitor-agent.env
readonly UNIT_NAME=servermonitor-agent
readonly UNIT_FILE=/etc/systemd/system/${UNIT_NAME}.service
readonly SERVICE_USER=servermonitor

# The exit status for an agent that could not be downloaded, kept apart from every other failure so a
# caller can fall back to building from source. deploy/install.sh does.
readonly EXIT_DOWNLOAD_FAILED=3

usage() {
  cat <<'EOF'
Installs the ServerMonitor agent as a systemd service.

Usage: install.sh --url URL --token TOKEN [options]

  --url URL        address of the API, such as http://192.168.1.20:7212
  --token TOKEN    the enrollment token, ENROLLMENT_TOKEN in the server's .env
  --reconfigure    ask for the address and token again instead of keeping the installed ones
  --version TAG    install a particular release, such as v1.2.0, rather than the latest
  --package FILE   install from an agent archive already on this machine
  --from-source    build the agent from the clone this script is in instead of downloading it
  --uninstall      stop the agent and remove it from this machine
EOF
}

die() {
  echo "error: $*" >&2
  exit 1
}

info() {
  echo "==> $*"
}

has_terminal() {
  { : </dev/tty; } 2>/dev/null
}

# Asks on the terminal even when the script itself arrived on standard input, as it does when piped
# from curl: reading standard input there would read the rest of the script.
ask() {
  local prompt="$1" name="$2" mode="${3:-}" reply=""

  has_terminal || die "no terminal to ask on; pass --url and --token"

  if [[ "$mode" == secret ]]; then
    # Without echo, so the token does not stay behind in the terminal's scrollback.
    read -r -s -p "$prompt" reply </dev/tty
    echo >&2
  else
    read -r -p "$prompt" reply </dev/tty
  fi

  printf -v "$name" '%s' "$reply"
}

download() {
  local url="$1" destination="$2"

  if command -v curl >/dev/null; then
    curl -fsSL --retry 3 --connect-timeout 15 -o "$destination" "$url"
  elif command -v wget >/dev/null; then
    wget -q --tries=3 --timeout=15 -O "$destination" "$url"
  else
    die "curl or wget is needed"
  fi
}

architecture() {
  case "$(uname -m)" in
    x86_64 | amd64) echo linux-x64 ;;
    aarch64 | arm64) echo linux-arm64 ;;
    *) return 1 ;;
  esac
}

# Downloads a release archive to $3 and checks it against the release's SHA256SUMS.
download_release() {
  local version="$1" destination="$2"
  local asset base expected actual
  asset="$(basename "$destination")"

  # SERVERMONITOR_DOWNLOAD_BASE serves the packages from somewhere other than this repository's
  # releases: a fork, a mirror, or a test.
  if [[ -n "${SERVERMONITOR_DOWNLOAD_BASE:-}" ]]; then
    base="${SERVERMONITOR_DOWNLOAD_BASE%/}"
  elif [[ "$version" == latest ]]; then
    base="https://github.com/$REPO/releases/latest/download"
  else
    base="https://github.com/$REPO/releases/download/$version"
  fi

  info "Downloading $asset ($version)"
  if ! download "$base/$asset" "$destination" || ! download "$base/SHA256SUMS" "$destination.sha256sums"; then
    echo "error: could not download the agent from $base" >&2
    echo "If no release has been published yet, --from-source builds it from a clone instead." >&2
    exit "$EXIT_DOWNLOAD_FAILED"
  fi

  # The archive and its checksum come from the same place, so this does not defend against whoever
  # controls that place. What it catches is a download that arrived damaged or cut short, which
  # would otherwise surface later as an agent that inexplicably fails to start.
  expected="$(awk -v name="$asset" '$2 == name || $2 == "*" name { print $1 }' "$destination.sha256sums")"
  [[ -n "$expected" ]] || die "SHA256SUMS in the release does not list $asset"
  actual="$(sha256sum "$destination" | awk '{ print $1 }')"
  [[ "$actual" == "$expected" ]] || die "$asset is damaged: its checksum does not match the release"
}

# Builds the agent from the clone this script sits in, into $1.
build_from_source() {
  local destination="$1" rid="$2" script="${BASH_SOURCE[0]:-}" repo_root

  [[ -n "$script" && -f "$script" ]] \
    || die "--from-source needs the script run from a clone: sudo ./deploy/agent/install.sh --from-source"
  repo_root="$(cd "$(dirname "$script")/../.." && pwd)"
  [[ -f "$repo_root/ServerMonitor.Agent/ServerMonitor.Agent.csproj" ]] \
    || die "--from-source needs the script run from a clone of the repository"

  info "Building the agent for $rid"
  if command -v docker >/dev/null; then
    # Inside the SDK image, so the machine needs no SDK: a monitored machine should carry as little
    # as possible that is not its job. The source goes in through a pipe and the result comes out
    # with docker cp rather than through mounted folders, which Docker installed as a snap cannot
    # reach. bin and obj stay behind: a restore made on another machine records that machine's
    # paths, and publishing on top of it fails in confusing ways.
    local container="servermonitor-agent-build-$$"
    tar -C "$repo_root" --exclude=.git --exclude=.vs --exclude=bin --exclude=obj -cf - . \
      | docker run -i --name "$container" mcr.microsoft.com/dotnet/sdk:10.0 sh -c \
        "mkdir -p /src && tar -C /src -xf - && cd /src && dotnet publish ServerMonitor.Agent -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o /out" \
      || { docker rm -f "$container" >/dev/null 2>&1; die "the build failed; the reason is above"; }
    docker cp "$container:/out/." "$destination/"
    docker rm "$container" >/dev/null
  elif command -v dotnet >/dev/null; then
    dotnet publish "$repo_root/ServerMonitor.Agent" -c Release -r "$rid" --self-contained true \
      -p:PublishSingleFile=true -o "$destination"
  else
    die "building from source needs Docker or the .NET SDK"
  fi

  rm -f "$destination"/*.pdb
  cp "$repo_root/deploy/agent/$UNIT_NAME.service" "$destination/"
}

uninstall() {
  info "Stopping the agent"
  systemctl disable --now "$UNIT_NAME" 2>/dev/null || true
  rm -f "$UNIT_FILE"
  systemctl daemon-reload
  systemctl reset-failed "$UNIT_NAME" 2>/dev/null || true

  info "Removing $INSTALL_DIR and $ENV_FILE"
  rm -rf "$INSTALL_DIR"
  rm -f "$ENV_FILE"

  if id "$SERVICE_USER" >/dev/null 2>&1; then
    userdel "$SERVICE_USER" 2>/dev/null || true
  fi

  cat <<'EOF'

The agent is removed. The machine and its history stay in the fleet until you remove them from the
machine's page on the dashboard.
EOF
}

main() {
  local url="${SERVERMONITOR_URL:-}" token="${SERVERMONITOR_TOKEN:-}"
  local version=latest package="" reconfigure=false from_source=false remove=false

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --url | --token | --version | --package)
        [[ $# -ge 2 ]] || die "$1 needs a value"
        case "$1" in
          --url) url="$2" ;;
          --token) token="$2" ;;
          --version) version="$2" ;;
          --package) package="$2" ;;
        esac
        shift 2
        ;;
      --reconfigure)
        reconfigure=true
        shift
        ;;
      --from-source)
        from_source=true
        shift
        ;;
      --uninstall)
        remove=true
        shift
        ;;
      -h | --help)
        usage
        return 0
        ;;
      *)
        usage >&2
        die "unknown option: $1"
        ;;
    esac
  done

  [[ $EUID -eq 0 ]] || die "run as root, with sudo"
  command -v systemctl >/dev/null || die "this installer needs systemd"

  if $remove; then
    uninstall
    return 0
  fi

  local rid
  rid="$(architecture)" || die "unsupported architecture: $(uname -m)"
  [[ "$version" == latest || "$version" =~ ^v[0-9][0-9A-Za-z.-]*$ ]] \
    || die "--version expects a release tag such as v1.2.0"
  [[ -z "$package" || -f "$package" ]] || die "no such file: $package"

  # The configuration comes first, so a missing token fails before minutes of downloading or
  # building rather than after.
  if [[ -f "$ENV_FILE" ]] && ! $reconfigure; then
    [[ -n "$url" ]] || url="$(sed -n 's/^Agent__ServerUrl=//p' "$ENV_FILE" | tail -n 1)"
    [[ -n "$token" ]] || token="$(sed -n 's/^Agent__EnrollmentToken=//p' "$ENV_FILE" | tail -n 1)"
  fi

  if [[ -z "$url" ]]; then
    ask "API address [http://localhost:7212]: " url
    url="${url:-http://localhost:7212}"
  fi

  [[ -n "$token" ]] || ask "Enrollment token (ENROLLMENT_TOKEN in the server's .env): " token secret

  # Both end up in a systemd environment file, where a newline would start another variable and a
  # quote or a backslash would be interpreted. Nothing legitimate needs them, so they are refused
  # rather than escaped.
  local url_pattern='^https?://[][A-Za-z0-9.:/_%~@+-]+$'
  local token_pattern='^[A-Za-z0-9._~+/=:-]+$'
  [[ "$url" =~ $url_pattern ]] || die "the address should look like http://192.168.1.20:7212"
  [[ -n "$token" ]] || die "an enrollment token is needed for the first registration"
  [[ "$token" =~ $token_pattern ]] || die "the token holds characters an enrollment token does not"

  if ! download "${url%/}/healthz" /dev/null 2>/dev/null; then
    echo "warning: $url does not answer from this machine. Installing anyway: the agent keeps" >&2
    echo "retrying on its own, but check the address and any firewall in between." >&2
  fi

  local work
  work="$(mktemp -d)"
  # shellcheck disable=SC2064 # expanded now on purpose: $work is local, and gone when the trap runs
  trap "rm -rf '$work'" EXIT

  local contents="$work/agent"
  mkdir "$contents"

  if $from_source; then
    build_from_source "$contents" "$rid"
  else
    if [[ -z "$package" ]]; then
      package="$work/servermonitor-agent-$rid.tar.gz"
      download_release "$version" "$package"
    fi
    tar -xzf "$package" -C "$contents" || die "could not unpack $package"
  fi

  [[ -f "$contents/ServerMonitor.Agent" ]] || die "the package holds no ServerMonitor.Agent"
  [[ -f "$contents/$UNIT_NAME.service" ]] || die "the package holds no $UNIT_NAME.service"
  mv "$contents/$UNIT_NAME.service" "$work/"

  if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    info "Creating the system user $SERVICE_USER"
    useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
  fi

  local registered_before=false
  [[ -f "$INSTALL_DIR/agent-state.json" ]] && registered_before=true

  # Stopped first: overwriting the executable of a running process fails with "text file busy".
  systemctl stop "$UNIT_NAME" 2>/dev/null || true

  # cp adds and overwrites but never deletes, which is exactly what an upgrade needs:
  # agent-state.json holds the key this machine was issued, and losing it would mean registering
  # again and appearing in the fleet twice.
  info "Installing to $INSTALL_DIR"
  install -d -m 0750 -o "$SERVICE_USER" -g "$SERVICE_USER" "$INSTALL_DIR"
  cp -r "$contents"/. "$INSTALL_DIR"/
  chown -R "$SERVICE_USER":"$SERVICE_USER" "$INSTALL_DIR"
  chmod 0755 "$INSTALL_DIR/ServerMonitor.Agent"

  info "Writing $ENV_FILE"
  # Created with restrictive permissions from the first byte rather than tightened afterwards: in
  # between, the token would be readable by everyone. Lines other than these two are kept, so a
  # setting added by hand survives an upgrade.
  rm -f "$ENV_FILE.new"
  (
    umask 077
    {
      if [[ -f "$ENV_FILE" ]]; then
        grep -v -e '^Agent__ServerUrl=' -e '^Agent__EnrollmentToken=' "$ENV_FILE" || true
      fi
      printf 'Agent__ServerUrl=%s\nAgent__EnrollmentToken=%s\n' "$url" "$token"
    } >"$ENV_FILE.new"
  )
  chown root:root "$ENV_FILE.new"
  mv -f "$ENV_FILE.new" "$ENV_FILE"

  info "Installing the systemd unit"
  install -m 0644 "$work/$UNIT_NAME.service" "$UNIT_FILE"
  systemctl daemon-reload
  systemctl enable --now "$UNIT_NAME"

  # Active is not the same as working: an agent retrying against an address that does not answer is
  # active too. So wait for the one thing that proves registration succeeded, the state file, or for
  # the service to stop, which is what the agent does with a token it cannot use.
  info "Waiting for the agent to register"
  local waited=0
  while [[ $waited -lt 20 ]]; do
    systemctl is-active --quiet "$UNIT_NAME" || break
    # An upgrade finds the file already there, so the new binary gets a few seconds to show it starts.
    if [[ -f "$INSTALL_DIR/agent-state.json" ]] && { ! $registered_before || [[ $waited -ge 3 ]]; }; then
      break
    fi
    sleep 1
    waited=$((waited + 1))
  done

  if ! systemctl is-active --quiet "$UNIT_NAME"; then
    echo "The agent stopped. Its recent log:" >&2
    journalctl -u "$UNIT_NAME" -n 30 --no-pager >&2 || true
    exit 1
  fi

  if [[ ! -f "$INSTALL_DIR/agent-state.json" ]]; then
    echo "The agent is running but has not registered yet. It keeps retrying on its own; if this" >&2
    echo "lasts, the API address is probably wrong or unreachable. Recent log:" >&2
    journalctl -u "$UNIT_NAME" -n 15 --no-pager >&2 || true
  fi

  cat <<EOF

The agent is running. The machine appears in the fleet with its first reading.

  systemctl status $UNIT_NAME       is it running
  journalctl -u $UNIT_NAME -f       follow its log

To change the address or the token, run the install command again with the new values.
EOF
}

# Everything above only defines functions; this line is where anything happens. Bash reads a script
# as it runs it, so a download cut off halfway would otherwise execute whatever had arrived, and an
# update that rewrote this file mid-run would execute a mixture of two versions.
main "$@"

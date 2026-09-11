#!/usr/bin/env bash
#
# Installs ServerMonitor on this machine: the database, the API and the dashboard in containers, and
# the agent that watches the machine itself.
#
#   curl -fsSL https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/install.sh | sudo bash
#
# Run it again to update: it pulls the latest code and rebuilds, and the data and the secrets stay.
# Written for Ubuntu Server; other apt-based systems should work too. Anywhere else, install Docker
# with its compose plugin and git first, and the script uses them.

set -euo pipefail

readonly REPO_URL="${SERVERMONITOR_REPO:-https://github.com/SoftGene/ServerMonitor.git}"
readonly DEFAULT_DIR=/opt/servermonitor
readonly API_PORT=7212
readonly WEB_PORT=5298

usage() {
  cat <<EOF
Installs or updates ServerMonitor on this machine.

Usage: install.sh [options]

  --telegram-token TOKEN   a bot token from @BotFather, for alerts in Telegram
  --telegram-chat-id ID    the chat the bot reports to; the bot tells you its ID
  --dir DIR                where the server lives (default $DEFAULT_DIR)
  --no-agent               do not install the agent on this machine
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

  has_terminal || die "no terminal to ask on"

  if [[ "$mode" == secret ]]; then
    read -r -s -p "$prompt" reply </dev/tty
    echo >&2
  else
    read -r -p "$prompt" reply </dev/tty
  fi

  printf -v "$name" '%s' "$reply"
}

has_compose() {
  docker compose version >/dev/null 2>&1
}

install_prerequisites() {
  local packages=()

  command -v git >/dev/null || packages+=(git)
  command -v curl >/dev/null || packages+=(curl)
  command -v openssl >/dev/null || packages+=(openssl)

  if ! command -v docker >/dev/null; then
    packages+=(docker.io docker-compose-v2)
  elif ! has_compose; then
    # Docker's own packages name the compose plugin differently from Ubuntu's, and each one conflicts
    # with the other's engine.
    if dpkg -s docker-ce >/dev/null 2>&1; then
      packages+=(docker-compose-plugin)
    else
      packages+=(docker-compose-v2)
    fi
  fi

  if [[ ${#packages[@]} -gt 0 ]]; then
    command -v apt-get >/dev/null \
      || die "install ${packages[*]} and run this again; only on apt-based systems does it install them itself"

    info "Installing ${packages[*]}"
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq "${packages[@]}" \
      || die "could not install ${packages[*]}; https://docs.docker.com/engine/install/ covers Docker"
  fi

  # Enabled, so the containers come back after a reboot: they restart unless stopped, but only once
  # the Docker daemon itself is up.
  if command -v systemctl >/dev/null; then
    systemctl enable --now docker >/dev/null 2>&1 || true
  fi

  docker info >/dev/null 2>&1 || die "Docker is installed but not running"
  has_compose || die "the Docker compose plugin is missing"
}

# The compose file names its containers, so two copies of the server cannot run side by side, and a
# second one must not quietly adopt the first one's database, whose password it does not have.
# Better to stop and say where the other copy lives.
check_for_another_copy() {
  local dir="$1" name owner project

  for name in servermonitor-db servermonitor-api servermonitor-web; do
    # docker container inspect, not docker inspect: compose names its images after the same project,
    # so once the containers are gone a plain inspect finds the image servermonitor-api instead,
    # which has no such label, and a stopped install would be taken for someone else's.
    owner="$(docker container inspect --format '{{ index .Config.Labels "com.docker.compose.project.working_dir" }}' "$name" 2>/dev/null)" \
      || continue
    [[ "$owner" == "$dir" ]] && continue
    [[ -n "$owner" && "$owner" != "<no value>" ]] || owner="outside compose"
    die "a container named $name already exists, from $owner. Stop that copy first (docker compose down in its folder), then run this again."
  done

  # Compose names volumes after the project, and the project after the folder.
  project="$(basename "$dir" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9_-')"

  if [[ ! -f "$dir/.env" ]] && docker volume inspect "${project}_postgres_data" >/dev/null 2>&1; then
    die "a database from an earlier install is still here (volume ${project}_postgres_data), and only that install's .env holds its password. Copy that file to $dir/.env (sudo mkdir -p $dir first) and run this again."
  fi
}

checkout() {
  local dir="$1" contents=""

  if [[ -d "$dir/.git" ]]; then
    info "Updating the code in $dir"
    git -C "$dir" pull --ff-only --quiet \
      || die "could not update $dir; if files there were changed, 'git -C $dir status' shows which"
    return
  fi

  [[ ! -e "$dir" || -d "$dir" ]] || die "$dir exists and is not a folder"
  if [[ -d "$dir" ]]; then
    contents="$(ls -A "$dir")"
  fi

  # An .env on its own is expected: it is how an earlier install's secrets, and with them its
  # database, are carried over. Anything else there belongs to someone, and is left alone.
  if [[ -n "$contents" && "$contents" != ".env" ]]; then
    die "$dir already exists and is not a copy of ServerMonitor; choose another place with --dir"
  fi

  info "Downloading the code to $dir"
  # Cloned beside the folder and then moved into place, because git clones only into an empty one.
  local staging="$dir.download-$$"
  rm -rf "$staging"
  git clone --quiet --depth 1 "$REPO_URL" "$staging" || die "could not download the code from $REPO_URL"
  if [[ -f "$dir/.env" ]]; then
    mv "$dir/.env" "$staging/.env"
  fi
  if [[ -d "$dir" ]]; then
    rmdir "$dir"
  fi
  mv "$staging" "$dir"
}

env_get() {
  local file="$1" key="$2"
  sed -n "s/^${key}=//p" "$file" | tail -n 1
}

env_set() {
  local file="$1" key="$2" value="$3"

  rm -f "$file.new"
  (
    umask 077
    {
      grep -v "^${key}=" "$file" || true
      printf '%s=%s\n' "$key" "$value"
    } >"$file.new"
  )
  mv -f "$file.new" "$file"
}

create_env() {
  local file="$1"

  info "Generating the secrets in $file"
  # Readable by root alone from the first byte. Once the database exists, the password in this file
  # is the only key to it, which makes the file worth a backup.
  (
    umask 077
    cat >"$file" <<EOF
# Generated by deploy/install.sh; .env.example explains each setting.
# Keep a copy: the database password below is the only one that opens the existing database.
POSTGRES_PASSWORD=$(openssl rand -hex 32)
ENROLLMENT_TOKEN=$(openssl rand -hex 32)
SERVICE_KEY=$(openssl rand -hex 32)
TELEGRAM_BOT_TOKEN=
TELEGRAM_CHAT_ID=
RETENTION_DAYS=30
PUBLIC_API_URL=
EOF
  )

  # A failed command substitution inside a here-document does not stop the script, so check what
  # arrived rather than start a database with an empty password.
  [[ "$(env_get "$file" POSTGRES_PASSWORD)" =~ ^[0-9a-f]{64}$ ]] \
    || { rm -f "$file"; die "could not generate the secrets"; }
}

configure_telegram() {
  local file="$1" bot_token="$2" chat_id="$3" offer="$4"

  if [[ -z "$bot_token" && "$offer" == true ]] && has_terminal; then
    echo
    echo "Telegram alerts are optional. To get them, message @BotFather in Telegram, send /newbot,"
    echo "and paste the token it gives you here."
    ask "Bot token (Enter to skip): " bot_token secret
  fi

  if [[ -n "$bot_token" ]]; then
    [[ "$bot_token" =~ ^[0-9]+:[A-Za-z0-9_-]+$ ]] \
      || die "that is not a bot token from @BotFather, which looks like 123456789:AAE..."
    env_set "$file" TELEGRAM_BOT_TOKEN "$bot_token"
  fi

  if [[ -n "$chat_id" ]]; then
    [[ "$chat_id" =~ ^-?[0-9]+$ ]] || die "a chat ID is a number, such as 123456789 or -1001234567890"
    env_set "$file" TELEGRAM_CHAT_ID "$chat_id"
  fi
}

start_containers() {
  local dir="$1"

  info "Building and starting the containers; the first build takes several minutes"
  (cd "$dir" && docker compose up -d --build) || die "the containers did not start; the reason is above"
}

wait_for_api() {
  local dir="$1" waited=0

  info "Waiting for the API to answer"
  until curl -fs -o /dev/null --max-time 3 "http://127.0.0.1:$API_PORT/healthz"; do
    if [[ $waited -ge 180 ]]; then
      (cd "$dir" && docker compose logs --tail 40 api) >&2 || true
      die "the API did not answer within three minutes; its log is above"
    fi
    sleep 3
    waited=$((waited + 3))
  done
}

install_agent() {
  local dir="$1" token status=0
  local url="http://127.0.0.1:$API_PORT"
  token="$(env_get "$dir/.env" ENROLLMENT_TOKEN)"

  info "Installing the agent, so the fleet starts with this machine"
  SERVERMONITOR_URL="$url" SERVERMONITOR_TOKEN="$token" bash "$dir/deploy/agent/install.sh" || status=$?

  # 3 is the agent installer reporting that it could not download a release, which is the situation
  # before the first one is published. The code is right here, so build it instead.
  if [[ $status -eq 3 ]]; then
    info "No agent release to download; building the agent from this copy of the code instead"
    status=0
    SERVERMONITOR_URL="$url" SERVERMONITOR_TOKEN="$token" \
      bash "$dir/deploy/agent/install.sh" --from-source || status=$?
  fi

  if [[ $status -ne 0 ]]; then
    echo "warning: the agent on this machine did not install; the server itself is running." >&2
    echo "Once the problem above is fixed: sudo bash $dir/deploy/agent/install.sh" >&2
  fi
}

# The address other machines on the network reach this one by: the source address of the route out.
primary_address() {
  local address=""

  if command -v ip >/dev/null; then
    address="$(ip -4 route get 1.1.1.1 2>/dev/null \
      | awk '{ for (i = 1; i < NF; i++) if ($i == "src") { print $(i + 1); exit } }')"
  fi

  [[ -n "$address" ]] || address="$(hostname -I 2>/dev/null | awk '{ print $1 }')"
  echo "${address:-this-machine}"
}

summary() {
  local dir="$1" address
  address="$(primary_address)"

  cat <<EOF

ServerMonitor is running.

  Dashboard   http://$address:$WEB_PORT
              the first visit asks you to create the account
  Agents      http://$address:$API_PORT
  Secrets     $dir/.env (keep a copy somewhere safe)

To watch another machine, open the dashboard, choose "Add a machine", and run the command it shows
on that machine.
EOF

  if [[ -n "$(env_get "$dir/.env" TELEGRAM_BOT_TOKEN)" && -z "$(env_get "$dir/.env" TELEGRAM_CHAT_ID)" ]]; then
    cat <<EOF

One step left for Telegram: send your bot any message, and it replies with your chat ID. Then run

  sudo bash $dir/deploy/install.sh --telegram-chat-id <that ID>
EOF
  fi
}

main() {
  local dir="$DEFAULT_DIR" bot_token="" chat_id="" with_agent=true

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --telegram-token | --telegram-chat-id | --dir)
        [[ $# -ge 2 ]] || die "$1 needs a value"
        case "$1" in
          --telegram-token) bot_token="$2" ;;
          --telegram-chat-id) chat_id="$2" ;;
          --dir) dir="$2" ;;
        esac
        shift 2
        ;;
      --no-agent)
        with_agent=false
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
  dir="${dir%/}"
  [[ "$dir" =~ ^/.+ ]] || die "--dir needs an absolute path"

  install_prerequisites
  check_for_another_copy "$dir"
  checkout "$dir"

  local first_install=false
  if [[ ! -f "$dir/.env" ]]; then
    first_install=true
    create_env "$dir/.env"
  fi

  configure_telegram "$dir/.env" "$bot_token" "$chat_id" "$first_install"
  start_containers "$dir"
  wait_for_api "$dir"

  if $with_agent; then
    install_agent "$dir"
  fi

  summary "$dir"
}

# Everything above only defines functions; this line is where anything happens. Bash reads a script
# as it runs it, so a download cut off halfway would otherwise execute whatever had arrived, and the
# update below rewrites this very file while it runs.
main "$@"

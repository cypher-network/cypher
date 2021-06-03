#!/usr/bin/env bash
# shellcheck disable=SC1090

# cypher cypnode
# (c) 2021 Tangram
#
# Install with this command (from your Linux machine):
#
# bash <(curl -sSL https://raw.githubusercontent.com/cypher-network/cypher/master/install/linux/install.sh)

# -e option instructs bash to immediately exit if any command [1] has a non-zero exit status
# We do not want users to end up with a partially working install, so we exit the script
# instead of continuing the installation with something broken
set -e

while test $# -gt 0
do
    case "$1" in
        --help)
          echo "  Install script arguments:"
          echo
          echo "    --config-skip                 : skip the node's configuration wizard (--noninteractive implies --config-skip)"
          echo "    --no-service                  : do not install node as a service"
          echo "    --noninteractive              : use default options without user interaction"
          echo "    --uninstall                   : uninstall node"
          echo
          exit 0
          ;;
        --config-skip)
            IS_SKIP_CONFIG=true
            ;;
        --no-service)
            IS_NO_SERVICE=true
            ;;
        --noninteractive)
            IS_NON_INTERACTIVE=true
            IS_SKIP_CONFIG=true
            ;;
        --uninstall)
            IS_UNINSTALL=true
            ;;
        --*) echo "bad option $1"
            exit 1
            ;;
    esac
    shift
done

######## VARIABLES #########
# For better maintainability, we store as much information that can change in variables
# This allows us to make a change in one place that can propagate to all instances of the variable
# These variables should all be GLOBAL variables, written in CAPS
# Local variables will be in lowercase and will exist only within functions
# It's still a work in progress, so you may see some variance in this guideline until it is complete
if [[ "$OSTYPE" == "darwin"* ]]; then
  IS_MACOS=true
  ARCHITECTURE_UNIFIED="osx-x64"

  CYPHER_CYPNODE_VERSION=$(curl --silent "https://api.github.com/repos/cypher-network/cypher/releases/latest" | grep -w '"tag_name": "v.*"' | cut -f2 -d ":" | cut -f2 -d "\"")
  CYPHER_CYPNODE_GROUP="cypher_cypnode"
  CYPHER_CYPNODE_USER="_cypher_cypnode"

  LAUNCHD_SERVICE_PATH="/Library/LaunchDaemons/"
  CYPHER_CYPNODE_LAUNCHD_SERVICE="cypher-cypnode.plist"
  CYPHER_CYPNODE_LAUNCHD_SERVICE_URL="https://raw.githubusercontent.com/cypher-network/cypher/master/install/macos/${CYPHER_CYPNODE_LAUNCHD_SERVICE}"

elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
  IS_LINUX=true
  DISTRO=$(grep '^ID=' /etc/os-release | cut -d '=' -f 2)
  DISTRO_VERSION=$(grep '^VERSION_ID=' /etc/os-release | cut -d '=' -f 2 | tr -d '"')
  ARCHITECTURE=$(uname -m)

  ARCHITECTURE_ARM=("armv7l")
  ARCHITECTURE_ARM64=("aarch64")
  ARCHITECTURE_X64=("x86_64")

  if [[ " ${ARCHITECTURE_ARM[*]} " =~ " ${ARCHITECTURE} " ]]; then
    ARCHITECTURE_UNIFIED="linux-arm"

  elif [[ " ${ARCHITECTURE_ARM64[*]} " =~ " ${ARCHITECTURE} " ]]; then
    ARCHITECTURE_UNIFIED="linux-arm64"

  elif [[ " ${ARCHITECTURE_X64[*]} " =~ " ${ARCHITECTURE} " ]]; then
    ARCHITECTURE_UNIFIED="linux-x64"
  else
    # Fall back to x64 architecture
    ARCHITECTURE_UNIFIED="linux-x64"
  fi

  if [ -f /etc/debian_version ]; then
    IS_DEBIAN_BASED=true
  fi

  INIT=$(ps --no-headers -o comm 1)

  CYPHER_CYPNODE_VERSION=$(curl --silent "https://api.github.com/repos/cypher-network/cypher/releases/latest" | grep -Po '"tag_name": "\K.*?(?=")')
  CYPHER_CYPNODE_GROUP="cypher-cypnode"
  CYPHER_CYPNODE_USER="cypher-cypnode"

  SYSTEMD_SERVICE_PATH="/etc/systemd/system/"
  CYPHER_CYPNODE_SYSTEMD_SERVICE="cypher-cypnode.service"
  CYPHER_CYPNODE_SYSTEMD_SERVICE_URL="https://raw.githubusercontent.com/cypher-network/cypher/master/install/linux/${CYPHER_CYPNODE_SYSTEMD_SERVICE}"

else
  echo "Unsupported OS type ${OSTYPE}"
  exit 1
fi


CYPHER_CYPNODE_VERSION_SHORT=$(echo "${CYPHER_CYPNODE_VERSION}" | cut -c 2-)
CYPHER_CYPNODE_ARTIFACT_PREFIX="cypher-cypnode_${CYPHER_CYPNODE_VERSION_SHORT}_"
CYPHER_CYPNODE_URL_PREFIX="https://github.com/cypher-network/cypher/releases/download/${CYPHER_CYPNODE_VERSION}/"

CYPHER_CYPNODE_OPT_PATH="/opt/cypher/cypnode/"
CYPHER_CYPNODE_TMP_PATH="/tmp/opt/cypher/cypnode/"


# Check if we are running on a real terminal and find the rows and columns
# If there is no real terminal, we will default to 80x24
if [ -t 0 ] ; then
  screen_size=$(stty size)
else
  screen_size="24 80"
fi
# Set rows variable to contain first number
printf -v rows '%d' "${screen_size%% *}"
# Set columns variable to contain second number
printf -v columns '%d' "${screen_size##* }"


# Divide by two so the dialogs take up half of the screen, which looks nice.
r=$(( rows / 2 ))
c=$(( columns / 2 ))
# Unless the screen is tiny
r=$(( r < 20 ? 20 : r ))
c=$(( c < 70 ? 70 : c ))


# Set these values so the installer can still run in color
COL_NC='\e[0m' # No Color
COL_LIGHT_GREEN='\e[1;32m'
COL_LIGHT_RED='\e[1;31m'
TICK="[${COL_LIGHT_GREEN}✓${COL_NC}]"
CROSS="[${COL_LIGHT_RED}✗${COL_NC}]"
INFO="[i]"
# shellcheck disable=SC2034
DONE="${COL_LIGHT_GREEN} done!${COL_NC}"
OVER="\\r\\033[K"


is_command() {
  # Checks for existence of string passed in as only function argument.
  # Exit value of 0 when exists, 1 if not exists. Value is the result
  # of the `command` shell built-in call.
  local check_command="$1"

  command -v "${check_command}" >/dev/null 2>&1
}


install_info() {
  ARCHIVE="${CYPHER_CYPNODE_ARTIFACT_PREFIX}${ARCHITECTURE_UNIFIED}.tar.gz"
  printf "\n  %b Using installation archive %s\n" "${TICK}" "${ARCHIVE}"
}

install_dependencies() {
  printf "\n  %b Checking dependencies\n" "${INFO}"

  if [ "${IS_DEBIAN_BASED}" = true ]; then
    if dpkg -s libc6-dev &> /dev/null; then
      printf "  %b libc6-dev\n" "${TICK}"
    else
      printf "  %b libc6-dev\n" "${CROSS}"
      printf "  %b Installing libc6-dev\n" "${INFO}"
      sudo apt-get update
      if [ "${IS_NON_INTERACTIVE}" = true ]; then
        sudo DEBIAN_FRONTEND=noninteractive apt-get -yq install libc6-dev
      else
        sudo apt-get install libc6-dev
      fi
    fi
  fi
}

download_archive() {
  printf "\n"
  printf "  %b Checking download utility\n" "${INFO}"
  if is_command curl; then
    printf "  %b curl\n" "${TICK}"
    HAS_CURL=true
  else
    printf "  %b curl\n" "${CROSS}"
    HAS_CURL=false
  fi

  if [ ! "${HAS_CURL}" = true ]; then
    if is_command wget; then
      printf "  %b wget\n" "${TICK}"
    else
      printf "  %b wget\n" "${CROSS}"
      printf "\n"
      printf "      Could not find a utility to download the archive. Please install either curl or wget.\n\n"
      return 1
    fi
  fi

  DOWNLOAD_PATH="/tmp/cypher-cypnode/"
  DOWNLOAD_FILE="${DOWNLOAD_PATH}${ARCHIVE}"
  DOWNLOAD_URL="${CYPHER_CYPNODE_URL_PREFIX}${ARCHIVE}"

  printf "\n";
  printf "  %b Checking archive %s" "${INFO}" "${ARCHIVE}"
  if [ "${HAS_CURL}" = true ]; then
    if curl --silent --fail "${DOWNLOAD_URL}" &> /dev/null; then
      printf " %b  %b Archive %s found\n\n" "${OVER}" "${TICK}" "${ARCHIVE}"
    else
      printf " %b  %b Archive %s cannot be found\n\n" "${OVER}" "${CROSS}" "${ARCHIVE}"
      exit 1
    fi
  else
    if wget -q "${DOWNLOAD_URL}"; then
      printf " %b  %b Archive %s found\n\n" "${OVER}" "${TICK}" "${ARCHIVE}"
    else
      printf " %b  %b Archive %s cannot be found\n\n" "${OVER}" "${CROSS}" "${ARCHIVE}"
      exit 1
    fi
  fi

  printf "  %b Downloading archive %s" "${INFO}" "${ARCHIVE}"

  if [ "${HAS_CURL}" = true ]; then
    curl -s -L --create-dirs -o "${DOWNLOAD_FILE}" "${DOWNLOAD_URL}"
  else
    mkdir -p "${DOWNLOAD_PATH}"
    wget -q -O "${DOWNLOAD_FILE}" "${DOWNLOAD_URL}"
  fi

  printf "%b  %b Downloaded archive %s\n" "${OVER}" "${TICK}" "${ARCHIVE}"
}


install_systemd_service() {
  printf "\n  %b Downloading systemd service file" "${INFO}"

  if [ "${HAS_CURL}" = true ]; then
    curl -s -L -o "/tmp/${CYPHER_CYPNODE_SYSTEMD_SERVICE}" "${CYPHER_CYPNODE_SYSTEMD_SERVICE_URL}"
  else
    wget -q -O "/tmp/${CYPHER_CYPNODE_SYSTEMD_SERVICE}" "${CYPHER_CYPNODE_SYSTEMD_SERVICE_URL}"
  fi

  printf "%b  %b Downloaded systemd service file\n" "${OVER}" "${TICK}"

  printf "  %b Installing systemd service file" "${INFO}"

  sudo install -m 755 -o "${CYPHER_CYPNODE_USER}" -g "${CYPHER_CYPNODE_GROUP}" "/tmp/${CYPHER_CYPNODE_SYSTEMD_SERVICE}" "${SYSTEMD_SERVICE_PATH}${CYPHER_CYPNODE_SYSTEMD_SERVICE}"

  printf "%b  %b Installed systemd service file\n" "${OVER}" "${TICK}"

  printf "  %b Removing temporary systemd service file" "${INFO}"
  rm "/tmp/${CYPHER_CYPNODE_SYSTEMD_SERVICE}"
  printf "%b  %b Removed temporary systemd service file\n" "${OVER}" "${TICK}"

  printf "  %b Reloading systemd daemon" "${INFO}"
  sudo systemctl daemon-reload
  printf "%b  %b Reloading systemd daemon\n" "${OVER}" "${TICK}"

  printf "  %b Enabling systemd service" "${INFO}"
  sudo systemctl enable "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" &> /dev/null
  printf "%b  %b Enabled systemd service\n" "${OVER}" "${TICK}"

  printf "  %b Starting systemd service" "${INFO}"
  sudo systemctl start "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" > /dev/null
  printf "%b  %b Started systemd service\n" "${OVER}" "${TICK}"
}


install_launchd_service() {
  printf "\n  %b Downloading launchd service file" "${INFO}"

  if [ "${HAS_CURL}" = true ]; then
    curl -s -L -o "/tmp/${CYPHER_CYPNODE_LAUNCHD_SERVICE}" "${CYPHER_CYPNODE_LAUNCHD_SERVICE_URL}"
  else
    wget -q -O "/tmp/${CYPHER_CYPNODE_LAUNCHD_SERVICE}" "${CYPHER_CYPNODE_LAUNCHD_SERVICE_URL}"
  fi

  printf "%b  %b Downloaded launchd service file\n" "${OVER}" "${TICK}"

  printf "  %b Installing launchd service file" "${INFO}"

  sudo install -m 755 -o "${CYPHER_CYPNODE_USER}" -g "${CYPHER_CYPNODE_GROUP}" "/tmp/${CYPHER_CYPNODE_LAUNCHD_SERVICE}" "${LAUNCHD_SERVICE_PATH}${CYPHER_CYPNODE_LAUNCHD_SERVICE}"

  printf "%b  %b Installed launchd service file\n" "${OVER}" "${TICK}"

  printf "  %b Removing temporary launchd service file" "${INFO}"
  rm "/tmp/${CYPHER_CYPNODE_LAUNCHD_SERVICE}"
  printf "%b  %b Removed temporary launchd service file\n" "${OVER}" "${TICK}"

  printf "  %b Loading launchd service" "${INFO}"
  sudo launchctl load "${LAUNCHD_SERVICE_PATH}${CYPHER_CYPNODE_LAUNCHD_SERVICE}" &> /dev/null
  printf "%b  %b Loaded launchd service\n" "${OVER}" "${TICK}"

  printf "  %b Starting launchd service" "${INFO}"
  sudo launchctl start "${CYPHER_CYPNODE_LAUNCHD_SERVICE}" > /dev/null
  printf "%b  %b Started launchd service\n" "${OVER}" "${TICK}"
}

stop_service() {
  if [ "${IS_LINUX}" = true ]; then
    if [ "${INIT}" = "systemd" ]; then
      if [ $(systemctl is-active "${CYPHER_CYPNODE_SYSTEMD_SERVICE}") = "active" ]; then
        printf "\n"
        printf "  %b Stopping systemd service" "${INFO}"
        sudo systemctl stop "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" >/dev/null
        printf "%b  %b Stopped systemd service\n" "${OVER}" "${TICK}"
      fi
    fi
  elif [ "${IS_MACOS}" = true ]; then
    if [ -f "${LAUNCHD_SERVICE_PATH}${CYPHER_CYPNODE_LAUNCHD_SERVICE}" ]; then
      if [ $(sudo launchctl list | grep "${CYPHER_CYPNODE_LAUNCHD_SERVICE}") ]; then
        printf "\n"
        printf "  %b Stopping launchd service" "${INFO}"
        sudo launchctl stop "${CYPHER_CYPNODE_LAUNCHD_SERVICE}" >/dev/null
        printf "%b  %b Stopped systemd service\n" "${OVER}" "${TICK}"
      fi
    fi
  fi
}

user_create_linux() {
  echo groupadd "$1"
  sudo groupadd -f "$1" >/dev/null
  sudo adduser --system --gid $(getent group "$1" | cut -d: -f3) --no-create-home "$2" >/dev/null
}

user_create_macos() {
  for (( uid = 500;; --uid )) ; do
    if ! id -u $uid &>/dev/null; then
      if ! dscl /Local/Default -ls Groups gid | grep -q [^0-9]$uid\$ ; then
        sudo dscl /Local/Default -create Groups/"$1" >/dev/null
        sudo dscl /Local/Default -create Groups/"$1" Password \* >/dev/null
        sudo dscl /Local/Default -create Groups/"$1" PrimaryGroupID $uid >/dev/null
        sudo dscl /Local/Default -create Groups/"$1" RealName "$1" >/dev/null
        sudo dscl /Local/Default -create Groups/"$1" RecordName _"$1" "$1" >/dev/null

        sudo dscl /Local/Default -create Users/"$2" >/dev/null
        sudo dscl /Local/Default -create Users/"$2" PrimaryGroupID $uid
        sudo dscl /Local/Default -create Users/"$2" UniqueID $uid >/dev/null
        USER_CREATED=true
        break
      fi
    fi
  done

  if [ ! "${USER_CREATED}" = true ]; then
    printf "\n  %b Could not create user\n\n" "${CROSS}"
    exit 1
  fi
}

user_create() {
  printf "\n  %b Checking if user %s exists" "${INFO}" "${CYPHER_CYPNODE_USER}"

  if [ "${IS_LINUX}" = true ]; then
    if id "${CYPHER_CYPNODE_USER}" &>/dev/null; then
      printf "%b  %b User %s exists\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_USER}"
      USER_EXISTS=true
    fi
  elif [ "${IS_MACOS}" = true ]; then
    if dscl /Local/Default read /Users/"${CYPHER_CYPNODE_USER}" &>/dev/null; then
      printf "%b  %b User %s exists\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_USER}"
      USER_EXISTS=true
    fi
  fi

  if [ ! "${USER_EXISTS}" = true ]; then
    printf "%b  %b User %s does not exist\n" "${OVER}" "${CROSS}" "${CYPHER_CYPNODE_USER}"
    printf "  %b Creating user %s" "${INFO}" "${CYPHER_CYPNODE_USER}"

    if [ "${IS_LINUX}" = true ]; then
      user_create_linux "${CYPHER_CYPNODE_GROUP}" "${CYPHER_CYPNODE_USER}"
    elif [ "${IS_MACOS}" = true ]; then
      user_create_macos "${CYPHER_CYPNODE_GROUP}" "${CYPHER_CYPNODE_USER}"
    fi

    printf "%b  %b Created user %s\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_USER}"
  fi
}

install_archive() {
  printf "\n  %b Installing archive\n" "${INFO}"

  stop_service

  user_create

  printf "  %b Unpacking archive to %s" "${INFO}" "${CYPHER_CYPNODE_TMP_PATH}"
  mkdir -p "${CYPHER_CYPNODE_TMP_PATH}"
  if [ "${IS_LINUX}" = true ]; then
    tar --overwrite -xf "${DOWNLOAD_FILE}" -C "${CYPHER_CYPNODE_TMP_PATH}"
  elif [ "${IS_MACOS}" = true ]; then
    tar -xf "${DOWNLOAD_FILE}" -C "${CYPHER_CYPNODE_TMP_PATH}"
  fi
  printf "%b  %b Unpacked archive to %s\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_TMP_PATH}"

  printf "  %b Installing to %s" "${INFO}" "${CYPHER_CYPNODE_OPT_PATH}"
  sudo mkdir -p "${CYPHER_CYPNODE_OPT_PATH}"
  sudo cp -r "${CYPHER_CYPNODE_TMP_PATH}"* "${CYPHER_CYPNODE_OPT_PATH}"
  sudo chmod -R 755 "${CYPHER_CYPNODE_OPT_PATH}"
  sudo chown -R "${CYPHER_CYPNODE_USER}":"${CYPHER_CYPNODE_GROUP}" "${CYPHER_CYPNODE_OPT_PATH}"

  printf "%b  %b Installed to %s\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_OPT_PATH}"

  if [ "${IS_SKIP_CONFIG}" = true ]; then
    printf "  %b Skipping configuration util\n\n" "${CROSS}"
  else
    printf "  %b Running configuration util" "${INFO}"
    sudo -u "${CYPHER_CYPNODE_USER}" "${CYPHER_CYPNODE_OPT_PATH}"cypnode --configure
    printf "%b  %b Run configuration util\n\n" "${OVER}" "${TICK}"
  fi

  if [ "${IS_NO_SERVICE}" = true ]; then
    printf "  %b Not installing service\n" "${CROSS}"
  else
    if [ "${IS_LINUX}" = true ]; then
      if [ "${INIT}" = "systemd" ]; then
        if [ "${IS_NON_INTERACTIVE}" = true ]; then
          printf "  %b Using default systemd service\n" "${TICK}"
          install_systemd_service
        else
          if whiptail --title "systemd service" --yesno "To run the node as a service, it is recommended to configure the node as a systemd service.\\n\\nWould you like to use the default systemd service configuration provided with cypher-cypnode?" "${7}" "${c}"; then
            printf "  %b Using default systemd service\n" "${TICK}"
            install_systemd_service
          else
            printf "  %b Not using default systemd service%s\n" "${CROSS}"
          fi
        fi
      elif [ "${INIT}" = "init" ]; then
        printf "  %b No cypher-cypnode init script available yet\n" "${CROSS}"

      else
        printf "\n"
        printf "  %b Unknown system %s. Please report this issue on\n" "${CROSS}" "${INIT}"
        printf "      https://github.com/cypher-network/cypher/issues/new"
      fi
    elif [ "${IS_MACOS}" = true ]; then
      if [ "${IS_NON_INTERACTIVE}" = true ]; then
        printf "  %b Using default launchd service\n" "${TICK}"
        install_launchd_service
      else
        if [ $(osascript -e 'button returned of (display dialog "To run the node as a service, it is recommended to configure the node as a launchd service. Would you like to use the default launchd service configuration provided with cypher-cypnode?" buttons {"No", "Yes"})') = 'Yes' ]; then
          printf "  %b Using default launchd service\n" "${TICK}"
          install_launchd_service
        else
          printf "  %b Not using default launchd service%s\n" "${CROSS}"
        fi
      fi
    fi
  fi
}


cleanup() {
  printf "\n"
  printf "  %b Cleaning up files" "${INFO}"
  rm -rf "${DOWNLOAD_PATH}"
  sudo rm -rf "${CYPHER_CYPNODE_TMP_PATH}"
  printf "%b  %b Cleaned up files\n" "${OVER}" "${TICK}"
}

finish() {
  printf "\n\n  %b Installation succesful\n\n" "${DONE}"
}

if [ "${IS_UNINSTALL}" = true ]; then
  printf "  %b Uninstalling\n\n" "${INFO}"

  stop_service

  if [ "${IS_LINUX}" = true ]; then
    if [ "${INIT}" = "systemd" ]; then
      if [ -f "${SYSTEMD_SERVICE_PATH}${CYPHER_CYPNODE_SYSTEMD_SERVICE}" ]; then
        if [ $(systemctl is-enabled "${CYPHER_CYPNODE_SYSTEMD_SERVICE}") = "enabled" ]; then
          printf "  %b Disabling service" "${INFO}"
          sudo systemctl disable "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" >/dev/null 2>&1
          printf "%b  %b Disabled service\n" "${OVER}" "${TICK}"
        fi

        printf "  %b Removing service" "${INFO}"
        sudo rm -f "${SYSTEMD_SERVICE_PATH}${CYPHER_CYPNODE_SYSTEMD_SERVICE}"
        printf "%b  %b Removed service\n" "${OVER}" "${TICK}"

        printf "  %b Reloading systemd daemon" "${INFO}"
        sudo systemctl daemon-reload
        printf "%b  %b Reloading systemd daemon\n" "${OVER}" "${TICK}"
      fi
    fi
  elif [ "${IS_MACOS}" = true ]; then
    if [ -f "${LAUNCHD_SERVICE_PATH}${CYPHER_CYPNODE_LAUNCHD_SERVICE}" ]; then
      if [ sudo launchctl list | grep "${CYPHER_CYPNODE_LAUNCHD_SERVICE}" ]; then
        printf "  %b Unloading service" "${INFO}"
        sudo launchctl unload "${LAUNCHD_SERVICE_PATH}${CYPHER_CYPNODE_LAUNCHD_SERVICE}" >/dev/null 2>&1
        printf "%b  %b Unloaded service\n" "${OVER}" "${TICK}"
      fi

      printf "  %b Removing service" "${INFO}"
      sudo rm -f "${LAUNCHD_SERVICE_PATH}${CYPHER_CYPNODE_LAUNCHD_SERVICE}"
      printf "%b  %b Removed service\n" "${OVER}" "${TICK}"
    fi
  fi

  sudo rm -rf "${CYPHER_CYPNODE_OPT_PATH}"

  if [ "${IS_LINUX}" = true ]; then
    if getent passwd "${CYPHER_CYPNODE_USER}" >/dev/null; then
      printf "  %b Removing user" "${INFO}"
      sudo userdel "${CYPHER_CYPNODE_USER}" > /dev/null
      # group is remove implicitly
      printf "%b  %b Removed user\n" "${OVER}" "${TICK}"
    fi
  elif [ "${IS_MACOS}" = true ]; then
    if dscl /Local/Default read /Users/"${CYPHER_CYPNODE_USER}" &>/dev/null; then
      printf "  %b Removing user" "${INFO}"
      sudo dscl /Local/Default -delete /Users/"${CYPHER_CYPNODE_USER}" >/dev/null
      sudo dscl /Local/Default -delete /Groups/"${CYPHER_CYPNODE_GROUP}" >/dev/null
      printf "%b  %b Removed user\n" "${OVER}" "${TICK}"
    fi
  fi
  printf "\n\n  %b Uninstall succesful\n\n" "${DONE}"

else
  install_info
  install_dependencies

  download_archive
  install_archive

  cleanup
  finish
fi

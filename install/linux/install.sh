#!/usr/bin/env bash
# shellcheck disable=SC1090

# cypher cypnode
# (c) 2021 Tangram
# This script is a shameless adaptation of the work of
# Pi-hole, LLC (https://pi-hole.net)
#
# Using the Pi-hole installation script allows us to benefit from bug fixes
# and feature updates done by the Pi-hole team without reinventing the wheel.
#
# This file is copyright under the latest version of the EUPL.

# Install with this command (from your Linux machine):
#
# curl -sL https://raw.githubusercontent.com/cypher-network/cypher/sk_installer/install.sh | bash

# -e option instructs bash to immediately exit if any command [1] has a non-zero exit status
# We do not want users to end up with a partially working install, so we exit the script
# instead of continuing the installation with something broken
set -e

######## VARIABLES #########
# For better maintainability, we store as much information that can change in variables
# This allows us to make a change in one place that can propagate to all instances of the variable
# These variables should all be GLOBAL variables, written in CAPS
# Local variables will be in lowercase and will exist only within functions
# It's still a work in progress, so you may see some variance in this guideline until it is complete
DISTRO=$(grep '^ID=' /etc/os-release | cut -d '=' -f 2)
DISTRO_VERSION=$(grep '^VERSION_ID=' /etc/os-release | cut -d '=' -f 2 | tr -d '"')
ARCHITECTURE=$(uname -m)

ARCHITECTURE_ARM=("armv7l")
ARCHITECTURE_ARM64=("aarch64")
ARCHITECTURE_X64=("x86_64")

if [[ " ${ARCHITECTURE_ARM[@]} " =~ " ${ARCHITECTURE} " ]]; then
  ARCHITECTURE_UNIFIED="arm"
  ARCHITECTURE_DEB="armhf"

elif [[ " ${ARCHITECTURE_ARM64[@]} " =~ " ${ARCHITECTURE} " ]]; then
  ARCHITECTURE_UNIFIED="arm64"
  ARCHITECTURE_DEB="arm64"

elif [[ " ${ARCHITECTURE_X64[@]} " =~ " ${ARCHITECTURE} " ]]; then
  ARCHITECTURE_UNIFIED="x64"
  ARCHITECTURE_DEB="amd64"
else
  # Fall back to x64 architecture
  ARCHITECTURE_UNIFIED="x64"
  ARCHITECTURE_DEB="amd64"
fi


CYPHER_CYPNODE_VERSION=$(curl --silent "https://api.github.com/repos/stephankempkes/cypher/releases/latest" | grep -Po '"tag_name": "\K.*?(?=")')
#CYPHER_CYPNODE_VERSION=$(curl --silent "https://api.github.com/repos/cypher-network/cypher/releases/latest" | grep -Po '"tag_name": "\K.*?(?=")')
CYPHER_CYPNODE_VERSION_SHORT=$(echo "${CYPHER_CYPNODE_VERSION}" | cut -c 2-)
CYPHER_CYPNODE_ARTIFACT_PREFIX="cypher-cypnode_${CYPHER_CYPNODE_VERSION_SHORT}_"
CYPHER_CYPNODE_URL_PREFIX="https://github.com/stephankempkes/cypher/releases/download/${CYPHER_CYPNODE_VERSION}/"
#CYPHER_CYPNODE_URL_PREFIX="https://github.com/cypher-network/cypher/releases/download/${CYPHER_CYPNODE_VERSION}/"

SYSTEMD_SERVICE_PATH="/etc/systemd/system/"
CYPHER_CYPNODE_SYSTEMD_SERVICE="cypher-cypnode.service"
CYPHER_CYPNODE_SYSTEMD_SERVICE_URL="https://raw.githubusercontent.com/cypher-network/cypher/master/install/linux/${CYPHER_CYPNODE_SYSTEMD_SERVICE}"

CYPHER_CYPNODE_OPT_PATH="/opt/cypher/cypnode/"
CYPHER_CYPNODE_TMP_PATH="/tmp/opt/cypher/cypnode/"
CYPHER_CYPNODE_USER="cypher-cypnode"


if [ -f /etc/debian_version ]; then
  IS_DEBIAN_BASED=true
else
  IS_DEBIAN_BASED=false
fi

INIT=$(ps --no-headers -o comm 1)


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


os_info() {
  if ! whiptail --title "System information" --yesno "The following system was detected:\\n\\nDistribution   : ${DISTRO}\\nVersion        : ${DISTRO_VERSION}\\nDebian based   : ${IS_DEBIAN_BASED}\\n\\nArchitecture   : ${ARCHITECTURE}\\n\nIs this information correct? When unsure, select <Yes>" "${7}" "${c}"; then
    printf "\n"
    printf "  %b Could not detect your system information. Please report this issue on\n" "${CROSS}"
    printf "      https://github.com/cypher-network/cypher/issues/new and include the output\n"
    printf "      of the following command:\n\n"
    printf "        uname -a\n\n"
    return 1
  fi
}


install_info() {
  if [ "${IS_DEBIAN_BASED}" = true ]; then
    printf "\n"
    ARCHIVE="${CYPHER_CYPNODE_ARTIFACT_PREFIX}${ARCHITECTURE_DEB}.deb"
    
    if whiptail --title "Installation archive - .deb" --yesno "You are running a Debian-based system. It is recommended to install cypher-cypnode using a .deb archive.\\n\\nWould you like to install the recommended archive ${ARCHIVE} ?" "${7}" "${c}"; then
      ARCHIVE_TYPE="deb"
      printf "  %b Using installation archive %s\n" "${TICK}" "${ARCHIVE}"
    else
      printf "  %b Not using Debian installation archive on Debian host %s\n" "${CROSS}" "${ARCHIVE}"
	  printf "      Please refer to the cypnode documentation to install the package manually.\n"
	  printf "      DO NOT INSTALL THE DEBIAN ARCHIVE PARALLEL TO A MANUAL INSTALLATION\n\n"
	  return 1
    fi
  fi
  
  if [ -z "${ARCHIVE}" ]; then
    printf "\n"
    ARCHIVE="${CYPHER_CYPNODE_ARTIFACT_PREFIX}linux-${ARCHITECTURE_UNIFIED}.tar.gz"
    if whiptail --title "Installation archive - self-contained .tar.gz" --yesno "Self-contained builds include the .NET runtime environment, which does not require a separate .NET installation at the cost of slightly more disk space.\\n\\nWould you like to install the self-contained archive ${ARCHIVE} ?" "${7}" "${c}"; then
        ARCHIVE_TYPE="self-contained"
        printf "  %b Using installation archive %s\n" "${TICK}" "${ARCHIVE}"
    else
      printf "  %b Not using installation archive %s\n" "${CROSS}" "${ARCHIVE}"
      printf "\n"
      printf "  %b Could not find a suitable installation archive.\n" "${CROSS}"
      printf "      Please refer to https://github.com/cypher-network/cypher for manual installation instructions.\n\n"
      return 1
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
  
  if [ "${HAS_CURL}" = false ]; then
    if is_command wget; then
      printf "  %b wget\n" "${TICK}"
    else
      printf "  %b wget\n" "${CROSS}"
      printf "\n"
      printf "      Could not find a utility to download the archive. Please install either curl or wget.\n\n"
      return 1
    fi
  fi
  
  printf "\n";
  printf "  %b Downloading archive %s" "${INFO}" "${ARCHIVE}"

  DOWNLOAD_PATH="/tmp/cypher-cypnode/"
  DOWNLOAD_FILE="${DOWNLOAD_PATH}${ARCHIVE}"
  DOWNLOAD_URL="${CYPHER_CYPNODE_URL_PREFIX}${ARCHIVE}"
  
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
  
  printf "%b  %b Downloading systemd service file\n" "${OVER}" "${TICK}"

  printf "  %b Installing systemd service file" "${INFO}"
  
  sudo install -m 755 -o "${CYPHER_CYPNODE_USER}" -g "${CYPHER_CYPNODE_USER}" "/tmp/${CYPHER_CYPNODE_SYSTEMD_SERVICE}" "${SYSTEMD_SERVICE_PATH}${CYPHER_CYPNODE_SYSTEMD_SERVICE}"
  
  printf "%b  %b Installing systemd service file\n" "${OVER}" "${TICK}"

  printf "  %b Removing temporary systemd service file" "${INFO}"
  rm "/tmp/${CYPHER_CYPNODE_SYSTEMD_SERVICE}"
  printf "%b  %b Removed temporary systemd service file\n" "${OVER}" "${TICK}"
  
  printf "  %b Reloading systemd daemon" "${INFO}"
  sudo systemctl daemon-reload
  printf "%b  %b Reloading systemd daemon\n" "${OVER}" "${TICK}"
  
  printf "  %b Enabling systemd service" "${INFO}"
  sudo systemctl enable "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" >/dev/null
  printf "%b  %b Enabled systemd service\n" "${OVER}" "${TICK}"
  
  printf "  %b Starting systemd service" "${INFO}"
  sudo systemctl start "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" >/dev/null
  printf "%b  %b Started systemd service\n" "${OVER}" "${TICK}"
}


install_archive() {
  if [ "${ARCHIVE_TYPE}" = "deb" ]; then
    printf "  %b Installing archive\n" "${INFO}"

    sudo dpkg -i "${DOWNLOAD_FILE}"
    
    if [ -x "/usr/share/cypher/cypnode/cypnode" ]; then
      if [ ! -f /usr/share/cypher/cypnode/cypnode/appsettings.json ]; then
        sudo -u cypher-cypnode /usr/share/cypher/cypnode/cypnode --configure || true

        printf "  %b Reloading systemd daemon" "${INFO}"
        sudo systemctl daemon-reload >/dev/null
        printf "%b  %b Reloading systemd daemon\n" "${OVER}" "${TICK}"

        printf "  %b Restarting systemd service" "${INFO}"
        sudo systemctl restart "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" >/dev/null
        printf "%b  %b Restarted systemd service\n" "${OVER}" "${TICK}"
      fi
    fi

  else
    if [ "${INIT}" = "systemd" ]; then
      if [ $(systemctl is-active "${CYPHER_CYPNODE_SYSTEMD_SERVICE}") = "active" ]; then
        printf "\n"
        printf "  %b Stopping systemd service" "${INFO}"
        sudo systemctl stop "${CYPHER_CYPNODE_SYSTEMD_SERVICE}" >/dev/null
        printf "%b  %b Stopped systemd service\n" "${OVER}" "${TICK}"
      fi
    fi

    printf "  %b Checking if user %s exists" "${INFO}" "${CYPHER_CYPNODE_USER}"
    
    # Create user
    if getent passwd "${CYPHER_CYPNODE_USER}" >/dev/null; then
      printf "%b  %b User %s exists\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_USER}"
    else
      printf "%b  %b User %s does not exist\n" "${OVER}" "${CROSS}" "${CYPHER_CYPNODE_USER}"
      printf "  %b Creating user %s" "${INFO}" "${CYPHER_CYPNODE_USER}"
      
	  sudo groupadd -f "${CYPHER_CYPNODE_USER}"
      sudo adduser --system --gid "${CYPHER_CYPNODE_USER}" --no-create-home "${CYPHER_CYPNODE_USER}"
        
      printf "%b  %b Created user %s\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_USER}"
    fi

    printf "  %b Unpacking archive to %s" "${INFO}" "${CYPHER_CYPNODE_TMP_PATH}"
    mkdir -p "${CYPHER_CYPNODE_TMP_PATH}"
    tar --overwrite -xf "${DOWNLOAD_FILE}" -C "${CYPHER_CYPNODE_TMP_PATH}"
    printf "%b  %b Unpacked archive to %s\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_TMP_PATH}"
    
    printf "  %b Installing to %s" "${INFO}" "${CYPHER_CYPNODE_OPT_PATH}"
    sudo mkdir -p "${CYPHER_CYPNODE_OPT_PATH}"
    sudo cp -r "${CYPHER_CYPNODE_TMP_PATH}"* "${CYPHER_CYPNODE_OPT_PATH}"
    sudo chmod 755 -R "${CYPHER_CYPNODE_OPT_PATH}"
    sudo chown -R "${CYPHER_CYPNODE_USER}":"${CYPHER_CYPNODE_USER}" "${CYPHER_CYPNODE_OPT_PATH}"
    printf "%b  %b Installed to %s\n" "${OVER}" "${TICK}" "${CYPHER_CYPNODE_OPT_PATH}"
    
    printf "  %b Running configuration util" "${INFO}"
    sudo -u "${CYPHER_CYPNODE_USER}" "${CYPHER_CYPNODE_OPT_PATH}"cypnode --configure
    printf "%b  %b Run configuration util\n\n" "${OVER}" "${TICK}"

    if [ "${INIT}" = "systemd" ]; then
      if whiptail --title "systemd service" --yesno "To run the node as a service, it is recommended to configure the node as a systemd service.\\n\\nWould you like to use the default systemd service configuration provided with cypher-cypnode?" "${7}" "${c}"; then
        printf "  %b Using default systemd service\n" "${TICK}"
        install_systemd_service
      else
        printf "  %b Not using default systemd service%s\n" "${CROSS}"
      fi
      
    elif [ "${INIT}" = "init" ]; then
      printf "  %b No cypher-cypnode init script available yet\n" "${CROSS}"
      
    else
      printf "\n"
      printf "  %b Unknown system %s. Please report this issue on\n" "${CROSS}" "${INIT}"
      printf "      https://github.com/cypher-network/cypher/issues/new"
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


os_info
install_info

download_archive
install_archive

cleanup
finish
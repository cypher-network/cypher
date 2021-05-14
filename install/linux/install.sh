#!/usr/bin/env bash
# shellcheck disable=SC1090

# Tangram Cypher Node
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
  ARCHITECTURE_DEB="armel"

elif [[ " ${ARCHITECTURE_ARM64[@]} " =~ " ${ARCHITECTURE} " ]]; then
  ARCHITECTURE_UNIFIED="arm64"
  ARCHITECTURE_DEB="arm64"

elif [[ " ${ARCHITECTURE_X64[@]} " =~ " ${ARCHITECTURE} " ]]; then
  ARCHITECTURE_UNIFIED="x64"
  ARCHITECTURE_DEB="amd64"
fi


TANGRAM_CYPNODE_VERSION=$(curl --silent "https://api.github.com/repos/stephankempkes/cypher/releases/latest" | grep -Po '"tag_name": "\K.*?(?=")')
#TANGRAM_CYPNODE_VERSION=$(curl --silent "https://api.github.com/repos/cypher-network/cypher/releases/latest" | grep -Po '"tag_name": "\K.*?(?=")')
TANGRAM_CYPNODE_VERSION_SHORT=$(echo "${TANGRAM_CYPNODE_VERSION}" | cut -c 2-)
TANGRAM_CYPNODE_ARTIFACT_PREFIX="tangram-cypnode_${TANGRAM_CYPNODE_VERSION_SHORT}_"
TANGRAM_CYPNODE_URL_PREFIX="https://github.com/stephankempkes/cypher/releases/download/${TANGRAM_CYPNODE_VERSION}/"
#TANGRAM_CYPNODE_URL_PREFIX="https://github.com/cypher-network/cypher/releases/download/${TANGRAM_CYPNODE_VERSION}/"


if test -f /etc/debian_version; then
  IS_DEBIAN_BASED=true
else
  IS_DEBIAN_BASED=false
fi

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
    printf "  %b Could not detect your system information. Please report this issue on\n" ${CROSS}
    printf "      https://github.com/cypher-network/cypher/issues/new and include the output\n"
    printf "      of the following command:\n\n"
    printf "        uname -a\n\n"
    return 1
  fi
}


install_info() {
  if [ "${IS_DEBIAN_BASED}" = true ]; then
    printf "\n"
    ARCHIVE="${TANGRAM_CYPNODE_ARTIFACT_PREFIX}${ARCHITECTURE_DEB}.deb"
    
    if whiptail --title "Installation archive - .deb" --yesno "You are running a Debian-based system. It is recommended to install tangram-cypnode using a .deb archive.\\n\\nWould you like to install the recommended archive ${ARCHIVE} ?" "${7}" "${c}"; then
      ARCHIVE_TYPE="deb"
      printf "  %b Using installation archive ${ARCHIVE}\n" "${TICK}"
    else
      printf "  %b Not using installation archive ${ARCHIVE}\n" "${CROSS}"
      ARCHIVE=""
    fi
  fi
  
  if [ -z "${ARCHIVE}" ]; then
    printf "\n"
    ARCHIVE="${TANGRAM_CYPNODE_ARTIFACT_PREFIX}linux-${ARCHITECTURE_UNIFIED}.tar.gz"
    if whiptail --title "Installation archive - self-contained .tar.gz" --yesno "Self-contained builds include the .NET runtime environment, which does not require a separate .NET installation at the cost of slightly more disk space.\\n\\nWould you like to install the self-contained archive ${ARCHIVE} ?" "${7}" "${c}"; then
        ARCHIVE_TYPE="self-contained"
        printf "  %b Using installation archive ${ARCHIVE}\n" "${TICK}"
    else
      printf "  %b Not using installation archive ${ARCHIVE}\n" "${CROSS}"
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
  
  printf "\n  %b Downloading archive ${ARCHIVE}\n\n" "${INFO}"

  DOWNLOAD_PATH="/tmp/tangram-cypnode/"
  DOWNLOAD_FILE="${DOWNLOAD_PATH}${ARCHIVE}"
  DOWNLOAD_URL="${TANGRAM_CYPNODE_URL_PREFIX}${ARCHIVE}"
  
  printf "\n  ${DOWNLOAD_URL}\n"
  printf "\n  ${DOWNLOAD_FILE}\n"
  
  if [ "${HAS_CURL}" = true ]; then
    curl -L --create-dirs -o "${DOWNLOAD_FILE}" "${DOWNLOAD_URL}"
  else
    mkdir -p "${DOWNLOAD_PATH}" 
    wget -O "${DOWNLOAD_FILE}" "${DOWNLOAD_URL}"
  fi
}


install_archive() {
  printf "\n\n  %b Installing archive\n\n" "${INFO}"
  
  if [ "${ARCHIVE_TYPE}" = "deb" ]; then
    sudo dpkg -i "${DOWNLOAD_FILE}"
  else
    :
  fi
}


cleanup() {
  printf "  \n\n  %b Cleaning up files\n" "${INFO}"
  rm -rf "${DOWNLOAD_PATH}"
}

finish() {
  printf "\n\n  %b Installation succesful\n\n" "${TICK}"
}


os_info
install_info

download_archive
install_archive

cleanup
finish

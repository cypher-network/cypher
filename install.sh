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
# curl -sSL https://raw.githubusercontent.com/cypher-network/cypher/sk_installer/install.sh | bash

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
DISTRO="grep '^ID=' /etc/os-release | cut -d '=' -f 2"
VERSION="grep '^VERSION_ID=' /etc/os-release | cut -d '=' -f 2 | tr -d '\"'"
MS_PACKAGE_SIGNING_KEY_URL="https://packages.microsoft.com/config/${DISTRO}/${VERSION}/packages-microsoft-prod.deb"


if [ -z "${USER}" ]; then
  USER="$(id -un)"
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

# If the color table file exists,
if [[ -f "${coltable}" ]]; then
    # source it
    source "${coltable}"
# Otherwise,
else
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
fi


is_command() {
    # Checks for existence of string passed in as only function argument.
    # Exit value of 0 when exists, 1 if not exists. Value is the result
    # of the `command` shell built-in call.
    local check_command="$1"

    command -v "${check_command}" >/dev/null 2>&1
}

update_package_cache() {
    # Update package cache on apt based OSes. Do this every time since
    # it's quick and packages can be updated at any time.

    # Local, named variables
    local str="Update local cache of available packages"
    printf "  %b %s..." "${INFO}" "${str}"
    # Create a command from the package cache variable
    if eval "${UPDATE_PKG_CACHE}" &> /dev/null; then
        printf "%b  %b %s\\n" "${OVER}" "${TICK}" "${str}"
    # Otherwise,
    else
        # show an error and exit
        printf "%b  %b %s\\n" "${OVER}" "${CROSS}" "${str}"
        printf "  %bError: Unable to update package cache. Please try \"%s\"%b" "${COL_LIGHT_RED}" "${UPDATE_PKG_CACHE}" "${COL_NC}"
        return 1
    fi
}


# Let user know if they have outdated packages on their system and
# advise them to run a package update at soonest possible.
notify_package_updates_available() {
    # Local, named variables
    local str="Checking ${PKG_MANAGER} for upgraded packages"
    printf "\\n  %b %s..." "${INFO}" "${str}"
    # Store the list of packages in a variable
    updatesToInstall=$(eval "${PKG_COUNT}")

    if [[ -d "/lib/modules/$(uname -r)" ]]; then
        if [[ "${updatesToInstall}" -eq 0 ]]; then
            printf "%b  %b %s... up to date!\\n\\n" "${OVER}" "${TICK}" "${str}"
        else
            printf "%b  %b %s... %s updates available\\n" "${OVER}" "${TICK}" "${str}" "${updatesToInstall}"
            printf "  %b %bIt is recommended to update your OS after installing the Pi-hole!%b\\n\\n" "${INFO}" "${COL_LIGHT_GREEN}" "${COL_NC}"
        fi
    else
        printf "%b  %b %s\\n" "${OVER}" "${CROSS}" "${str}"
        printf "      Kernel update detected. If the install fails, please reboot and try again\\n"
    fi
}



# Compatibility
distro_check() {
# If apt-get is installed, then we know it's part of the Debian family
if is_command apt-get ; then
    # Set some global variables here
    # We don't set them earlier since the family might be Red Hat, so these values would be different
    PKG_MANAGER="apt-get"
    # A variable to store the command used to update the package cache
    UPDATE_PKG_CACHE="${PKG_MANAGER} update"
    # An array for something...
    PKG_INSTALL=("${PKG_MANAGER}" -qq --no-install-recommends install)
    # grep -c will return 1 retVal on 0 matches, block this throwing the set -e with an OR TRUE
    PKG_COUNT="${PKG_MANAGER} -s -o Debug::NoLocking=true upgrade | grep -c ^Inst || true"
    # Update package cache. This is required already here to assure apt-cache calls have package lists available.
    update_package_cache || exit 1
    # Check whether dotnet core runtime is known
    printf "  %b Checking for Microsoft package repository" "${INFO}"
    if apt-cache show dotnet-runtime-5.0 > /dev/null 2>&1; then
        printf "%b  %b Checking for Microsoft package repository\\n" "${OVER}" "${TICK}"
    else
        printf "%b  %b Checking for Microsoft package repository\\n" "${OVER}" "${CROSS}"
        if ! whiptail --defaultno --title "Dependencies Require Update to Allowed Repositories" --yesno "Would you like to add the Microsoft package repository?\\n\\nThis repository is required by the following packages:\\n\\n- dotnet-runtime-5" "${r}" "${c}"; then
            printf "  %b Aborting installation: Dependencies could not be installed.\\n" "${CROSS}"
            exit 1 # exit the installer
        else
            printf "  %b Adding Microsoft package repository" "${INFO}"
            MSPROD=/tmp/packages-microsoft-prod.deb
            wget ${MS_PACKAGE_SIGNING_KEY_URL} -O "${MSPROD}"
            sudo dpkg -i "${MSPROD}"
            rm "${MSPROD}"
            printf "%b  %b Adding Microsoft package repository\\n" "${OVER}" "${INFO}"
        fi
    fi
    # These programs are stored in an array so they can be looped through later
    INSTALLER_DEPS=(curl unzip whiptail)
    # TGMNode itself has several dependencies that also need to be installed
    TGMNODE_DEPS=(apt-transport-https dotnet-runtime-5.0)

    # A function to check...
    test_dpkg_lock() {
        # An iterator used for counting loop iterations
        i=0
        # fuser is a program to show which processes use the named files, sockets, or filesystems
        # So while the command is true
        while fuser /var/lib/dpkg/lock >/dev/null 2>&1 ; do
            # Wait half a second
            sleep 0.5
            # and increase the iterator
            ((i=i+1))
        done
        # Always return success, since we only return if there is no
        # lock (anymore)
        return 0
    }

# If apt-get is not found, check for rpm to see if it's a Red Hat family OS
elif is_command rpm ; then
    printf "  %b Red Hat (family) is not yet supported. Please contact the Tangram developers\\n" "${CROSS}"
    exit

# If neither apt-get or yum/dnf package managers were found
else
    # it's not an OS we can support,
    printf "  %b OS distribution not yet supported. Please contact the Tangramm developers\\n" "${CROSS}"
    # so exit the installer
    exit
fi
}


service_exists() {
    if is_command systemctl ; then
      local n=$1
      if [[ $(systemctl list-units --all -t service --full --no-legend "$n.service" | cut -f1 -d' ') == $n.service ]]; then
          return 0
      else
          return 1
      fi
    else
        return 1
    fi
}

install_service() {
printf "  %b Checking for systemd" "${INFO}"
if [ ! "/run/systemd/system/" ]
then
    printf "%b  %b Checking for systemd\\n" "${OVER}" "${CROSS}"
else
    printf "%b  %b Checking for systemd\\n" "${OVER}" "${TICK}"

    printf "  %b Checking for installed service" "${INFO}"
    if service_exists cypnode; then
        printf "%b  %b Checking for installed service\\n" "${OVER}" "${CROSS}"
    else
        printf "%b  %b Checking for installed service\\n" "${OVER}" "${TICK}"

        if ! whiptail --defaultno --title "systemd TGMNode service" --yesno "Would you like to install TGMNode as a service?\\nWhen TGMNode is installed as a service, it will automatically start, restart and keep on running after you log out." "${r}" "${c}"; then
            printf "  %b Not installing TGMNode as a service\\n" "${CROSS}"
        else
            printf "  %b Installing TGMNode as a service" "${INFO}"
            curl -sL https://raw.githubusercontent.com/cypher-network/cypher/sk_installer/systemd/cypnode.service | sudo tee /etc/systemd/system/cypnode.service &> /dev/null
            printf "%b  %b Installing TGMNode as a service" "${OVER}" "${INFO}"
            enable_service cypnode
        fi
    fi
fi
}

stop_service() {
    # Stop service passed in as argument.
    # Can softfail, as process may not be installed when this is called
    local str="Stopping ${1} service"
    printf "  %b %s..." "${INFO}" "${str}"
    if is_command systemctl ; then
        systemctl stop "${1}" &> /dev/null || true
    else
        service "${1}" stop &> /dev/null || true
    fi
    printf "%b  %b %s...\\n" "${OVER}" "${TICK}" "${str}"
}

# Start/Restart service passed in as argument
restart_service() {
    # Local, named variables
    local str="Restarting ${1} service"
    printf "  %b %s..." "${INFO}" "${str}"
    # If systemctl exists,
    if is_command systemctl ; then
        # use that to restart the service
        systemctl restart "${1}" &> /dev/null
    # Otherwise,
    else
        # fall back to the service command
        service "${1}" restart &> /dev/null
    fi
    printf "%b  %b %s...\\n" "${OVER}" "${TICK}" "${str}"
}

# Enable service so that it will start with next reboot
enable_service() {
    # Local, named variables
    local str="Enabling ${1} service to start on reboot"
    printf "  %b %s..." "${INFO}" "${str}"
    # If systemctl exists,
    if is_command systemctl ; then
        # use that to enable the service
        systemctl enable "${1}" &> /dev/null
    # Otherwise,
    else
        # use update-rc.d to accomplish this
        update-rc.d "${1}" defaults &> /dev/null
    fi
    printf "%b  %b %s...\\n" "${OVER}" "${TICK}" "${str}"
}

# Disable service so that it will not with next reboot
disable_service() {
    # Local, named variables
    local str="Disabling ${1} service"
    printf "  %b %s..." "${INFO}" "${str}"
    # If systemctl exists,
    if is_command systemctl ; then
        # use that to disable the service
        systemctl disable "${1}" &> /dev/null
    # Otherwise,
    else
        # use update-rc.d to accomplish this
        update-rc.d "${1}" disable &> /dev/null
    fi
    printf "%b  %b %s...\\n" "${OVER}" "${TICK}" "${str}"
}

check_service_active() {
    # If systemctl exists,
    if is_command systemctl ; then
        # use that to check the status of the service
        systemctl is-enabled "${1}" &> /dev/null
    # Otherwise,
    else
        # fall back to service command
        service "${1}" status &> /dev/null
    fi
}



install_dependent_packages() {
    # Local, named variables should be used here, especially for an iterator
    # Add one to the counter
    counter=$((counter+1))
    # If it equals 1,
    if [[ "${counter}" == 1 ]]; then
        #
        printf "  %b Installer Dependency checks...\\n" "${INFO}"
    else
        #
        printf "  %b Main Dependency checks...\\n" "${INFO}"
    fi

    # Install packages passed in via argument array
    # No spinner - conflicts with set -e
    declare -a installArray

    # Debian based package install - debconf will download the entire package list
    # so we just create an array of packages not currently installed to cut down on the
    # amount of download traffic.
    # NOTE: We may be able to use this installArray in the future to create a list of package that were
    # installed by us, and remove only the installed packages, and not the entire list.
    if is_command apt-get ; then
        # For each package,
        for i in "$@"; do
            printf "  %b Checking for %s..." "${INFO}" "${i}"
            if dpkg-query -W -f='${Status}' "${i}" 2>/dev/null | grep "ok installed" &> /dev/null; then
                printf "%b  %b Checking for %s\\n" "${OVER}" "${TICK}" "${i}"
            else
                printf "%b  %b Checking for %s (will be installed)\\n" "${OVER}" "${INFO}" "${i}"
                installArray+=("${i}")
            fi
        done
        if [[ "${#installArray[@]}" -gt 0 ]]; then
            test_dpkg_lock
            printf "  %b Processing %s install(s) for: %s, please wait...\\n" "${INFO}" "${PKG_MANAGER}" "${installArray[*]}"
            printf '%*s\n' "$columns" '' | tr " " -;
            "${PKG_INSTALL[@]}" "${installArray[@]}"
            printf '%*s\n' "$columns" '' | tr " " -;
            return
        fi
        printf "\\n"
        return 0
    fi

    # Install Fedora/CentOS packages
    for i in "$@"; do
        printf "  %b Checking for %s..." "${INFO}" "${i}"
        if "${PKG_MANAGER}" -q list installed "${i}" &> /dev/null; then
            printf "%b  %b Checking for %s\\n" "${OVER}" "${TICK}" "${i}"
        else
            printf "%b  %b Checking for %s (will be installed)\\n" "${OVER}" "${INFO}" "${i}"
            installArray+=("${i}")
        fi
    done
    if [[ "${#installArray[@]}" -gt 0 ]]; then
        printf "  %b Processing %s install(s) for: %s, please wait...\\n" "${INFO}" "${PKG_MANAGER}" "${installArray[*]}"
        printf '%*s\n' "$columns" '' | tr " " -;
        "${PKG_INSTALL[@]}" "${installArray[@]}"
        printf '%*s\n' "$columns" '' | tr " " -;
        return
    fi
    printf "\\n"
    return 0
}


# Install base files and web interface
installTGMNode() {
    printf "  %b Fetching latest release\\n" "${INFO}"
    
    printf "  %b Getting release version" "${INFO}"
    TGMNODE_VERSION=$(curl -s https://api.github.com/repos/cypher-network/cypher/releases/latest | grep -Eo "\"tag_name\":\s*\"(.*)\"" | cut -d'"' -f4)
    printf "%b  %b Getting release version\\n" "${OVER}" "${TICK}"

    printf "  %b Downloading cypher node ${TGMNODE_VERSION}" "${INFO}"
    curl -sL https://github.com/cypher-network/cypher/releases/download/"${TGMNODE_VERSION}"/cypher."${TGMNODE_VERSION}".zip > "${TMPDIR}"/cypher.zip
    printf "%b  %b Downloading cypher node ${TGMNODE_VERSION}\\n" "${OVER}" "${TICK}"

    printf "\\n  %b Preparing release\\n" "${INFO}"
    printf "  %b Extracting ${TMPDIR}/cypher.zip" "${INFO}"
    unzip -q -o "${TMPDIR}"/cypher.zip -d "${TMPDIR}"/cypher
    printf "%b  %b Extracting ${TMPDIR}/cypher.zip\\n" "${OVER}" "${TICK}"

    printf "  %b Renaming appsettings.json to appsettings.default.json" "${INFO}"
    mv "${TMPDIR}"/cypher/appsettings.json "${TMPDIR}"/cypher/appsettings.default.json
    printf "%b  %b Renaming appsettings.json to appsettings.default.json\\n" "${OVER}" "${TICK}"

    printf "  %b Checking distribution path $HOME/.cypher" "${INFO}"
    if [ -d "$HOME"/.cypher ]
    then
        printf "%b  %b Checking distribution path $HOME/.cypher\\n" "${OVER}" "${TICK}"
    else
        printf "%b  %b Checking distribution path $HOME/.cypher\\n" "${OVER}" "${CROSS}"
        printf "  %b Creating distribution path $HOME/.cypher" "${INFO}"
        printf "Creating $HOME/.cypher\n"
        mkdir "$HOME"/.cypher
        printf "%b  %b Creating distribution path $HOME/.cypher\\n" "${OVER}" "${TICK}"
    fi
    
    printf "\\n  %b Installing distribution\\n" "${INFO}"
    printf "  %b Copying distribution" "${INFO}"
    cp -fR "${TMPDIR}"/cypher/. "$HOME"/.cypher/dist
    printf "%b  %b Copying distribution" "${OVER}" "${TICK}"

    printf "  %b Checking binary distribution path $HOME/.cypher/bin" "${INFO}"
    if [ -d "$HOME"/.cypher/bin ]
    then
        printf "%b  %b Checking binary distribution path $HOME/.cypher/bin\\n" "${OVER}" "${TICK}"
    else
        printf "%b  %b Checking binary distribution path $HOME/.cypher/bin\\n" "${OVER}" "${CROSS}"
        printf "%b Creating binary distribution path $HOME/.cypher/bin" "${INFO}"
        mkdir "$HOME"/.cypher/bin
        printf "%b  %b Creating binary distribution path $HOME/.cypher/bin\\n" "${OVER}" "${INFO}"
    fi

    printf "  %b Copying cypnode command" "${INFO}"
    cp -fR "$HOME"/.cypher/dist/Runners/cypnode.sh "$HOME"/.cypher/bin/cypnode
    printf "%b  %b Copying cypnode command\\n" "${OVER}" "${TICK}"

    printf "  %b Setting execute permission" "${INFO}"
    chmod +x "$HOME"/.cypher/bin/cypnode
    printf "%b  %b Setting execute permission\\n" "${OVER}" "${TICK}"

    printf "  %b Setting path" "${INFO}"
    if grep -q "$HOME/.cypher/bin" ~/.profile
    then
        :
    else
        echo "" >> ~/.profile
        echo "export PATH=$PATH:$HOME/.cypher/bin" >> ~/.profile
    fi
    printf "%b  %b Setting path\\n" "${OVER}" "${TICK}"

    printf "  %b Cleaning up temporary directory ${TMPDIR}/cypher" "${INFO}"
    rm -rf "$TMPDIR/cypher"
    rm "${TMPDIR}/cypher.zip"
    printf "%b  %b Cleaning up temporary directory ${TMPDIR}/cypher\\n" "${OVER}" "${TICK}"
}



make_temporary_log() {
    # Create a random temporary file for the log
    printf "  %b Creating temporary directory" "${INFO}"
    TMPDIR=$(mktemp -d -t ci-XXXXXXXXXX)
    printf "%b  %b Creating temporary directory: ${TMPDIR}\\n" "${OVER}" "${TICK}"

    TMPLOG="${TMPDIR}/install.log"
}


main() {
    ######## FIRST CHECK ########
    # Must be root to install
    local str="Root user check"
    printf "\\n"

    # If the user's id is zero,
    if [[ "${EUID}" -eq 0 ]]; then
        # they are root and all is good
        printf "  %b %s\\n" "${TICK}" "${str}"
        make_temporary_log
    # Otherwise,
    else
        # They do not have enough privileges, so let the user know
        printf "  %b %s\\n" "${INFO}" "${str}"
        printf "  %b %bScript called with non-root privileges%b\\n" "${INFO}" "${COL_LIGHT_RED}" "${COL_NC}"
        printf "      The installer requires elevated privileges\\n"
        printf "      Please check the installer for any concerns regarding this requirement\\n"
        printf "      Make sure to download this script from a trusted source\\n\\n"
        printf "  %b Sudo utility check" "${INFO}"

        # If the sudo command exists,
        if is_command sudo ; then
            printf "%b  %b Sudo utility check\\n" "${OVER}"  "${TICK}"

            # when run via curl piping
            if [[ "$0" == "bash" ]]; then
                # Download the install script and run it with admin rights
                exec curl -sSL https://raw.githubusercontent.com/cypher-network/cypher/sk_installer/install.sh | sudo bash "$@"
            else
                # when run via calling local bash script
                exec sudo bash "$0" "$@"
            fi

            exit $?
        # Otherwise,
        else
            # Let them know they need to run it as root
            printf "%b  %b Sudo utility check\\n" "${OVER}" "${CROSS}"
            printf "  %b Sudo is needed for the installer\\n\\n" "${INFO}"
            printf "  %b %bPlease re-run this installer as root${COL_NC}\\n" "${INFO}" "${COL_LIGHT_RED}"
            exit 1
        fi
    fi

    # Check for supported distribution
    distro_check

    # Start the installer
    # Notify user of package availability
    notify_package_updates_available

    # Install packages used by this installation script
    install_dependent_packages "${INSTALLER_DEPS[@]}"

    # Install the Core dependencies
    install_dependent_packages "${TGMNODE_DEPS[@]}"

    # Install and log everything to a file
    installTGMNode | tee -a ${TMPLOG}

    printf "\\n  %b TGMNode service intallation\\n" "${INFO}"
    install_service
    restart_service cypnode

    printf "  Installation of TGMNode complete!\\n\\n"
}

if [[ "${TGMNODE_TEST}" != true ]] ; then
    main "$@"
fi

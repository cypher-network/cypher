# License

CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0. 
To view a copy of this license, visit 
https://creativecommons.org/licenses/by-nc-nd/4.0

### Hardware Requirements

|                 | Relay                                                            | Staking                                                          |
|-----------------|------------------------------------------------------------------|------------------------------------------------------------------|
| System          | Windows 10<br/>Ubuntu 18.04/22.04<br/>CentOS 8/9<br/>macOS 11/12 | Windows 10<br/>Ubuntu 18.04/22.04<br/>CentOS 8/9<br/>macOS 11/12 |
| CPU             | Dual core                                                        | Quad core                                                        |
| Memory          | 1G/4G                                                            | 2G/8G                                                            |
| Hard Disk       | 25G SSD hard drive                                               | 50G SSD hard drive                                               | 

**NB - The hardware requirements may change.**

# Cypher

[![Build Cypher node](https://github.com/cypher-network/cypher/workflows/build%20cypher%20node/badge.svg)](https://github.com/cypher-network/cypher/commits/master/)
[![GitHub release](https://img.shields.io/github/release/cypher-network/cypher.svg)](https://GitHub.com/cypher-network/cypher/releases/)

## Installation

### Linux and macOS

For quick installation on Linux and macOS, execute the following command:

`bash <(curl -sSL https://cypherpunks.network/install.sh)`

The following parameters can be supplied:

`--help`
Display help

`--config-skip`
Do not run configuration wizard

`--no-service`
Do not install node as a service

`--noninteractive`
Assume default answers without user interaction.

`--uninstall`
Uninstall node


For example:

`bash <(curl -sSL https://cypherpunks.network/install.sh) --uninstall`

Cypher
===========

[![Build Cypher node](https://github.com/cypher-network/cypher/workflows/build%20cypher%20node/badge.svg)](https://github.com/cypher-network/cypher/commits/master/)
[![GitHub release](https://img.shields.io/github/release/cypher-network/cypher.svg)](https://GitHub.com/cypher-network/cypher/releases/)

## Installation

### .Net 6

Downloads for .Net
https://dotnet.microsoft.com/en-us/download

### Hardware Requirements

|                 | Relay                                                            | Staking                                                          |
|-----------------|------------------------------------------------------------------|------------------------------------------------------------------|
| System          | Windows 10<br/>Ubuntu 18.04/22.04<br/>CentOS 8/9<br/>macOS 11/12 | Windows 10<br/>Ubuntu 18.04/22.04<br/>CentOS 8/9<br/>macOS 11/12 |
| CPU             | Dual core                                                        | Quad core                                                        |
| Memory          | 1G/4G                                                            | 2G/8G                                                            |
| Hard Disk       | 25G SSD hard drive                                               | 50G SSD hard drive                                               | 

**NB - The hardware requirements may change.**

### Linux and macOS

For quick installation on Linux and macOS, execute the following command:

```shell
bash <(curl -sSL https://raw.githubusercontent.com/cypher-network/cypher/master/install/install.sh)
```

The following parameters can be supplied:

#### Linux Only

`install.sh --runasuser <username> --runasgroup users`
Install as the current logged in user

`install.sh --upgrade --runasuser <username> --runasgroup users` Upgrades the node

#### Linux and macOS

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

```shell
bash <(curl -sSL https://raw.githubusercontent.com/cypher-network/cypher/master/install/install.sh) --uninstall
```

## What is Cypher Network
Cypher is the testnet network for the team and community to first and foremost find critical bugs, run tests and experiments in a Tangram-like environment. You are now part of a huge milestone and something very special to the team and community members.

Cypher Network will be used as an early testing ground for things like consensus (messages, states and broadcasting) and gossip protocol (dissemination of members-membership).  Have fun on Cypher and learn more about Tangram! Explore the open-source code, setup your node, and explore the features. The objective here is to get to a point where Cypher will mimic the eventual live environment of the Tangram network. Things may get messy, this is expected. From installation on the user’s end to protocol enhancements and feature add-ons to Cypher, things will progressively get better over time.

## Security Warning
Cypher is the first release with consensus and should be treated as an experiment! There are no guarantees and we can expect flaws. Use it at your own risk.

## Whitepaper
If you’re interested, you can use the whitepaper as a reference.
https://github.com/cypher-network/whitepaper

## Who can participate in Cypher
If you wish to run a node, experiment and support the release by finding bugs or even getting yourself accustomed to the intricacies of what Tangram is about, this is the release is for you! This is the perfect time to start getting to know Tangram and the inner mechanics of its technologies and protocols.

If you wish to participate in the release of Cypher, you can claim $CYP through any of the channels (we recommend Discord, [**here**](https://discord.gg/6DT3yFhXCB)).

## Contribution and Support
If you have questions that need answering or a little more detail, feel free to get in touch through any of Tangram’s channels and our community members and managers can point you in the right direction.
If you'd like to contribute to Tangram Cypher (Node code), please know we're currently accepting issues, forks, fixes, commits and pull requests so that maintainers can review and merge into the main code base. If you wish to submit more complex changes, please check up with the core devs first on [Discord Channel](https://discord.gg/6DT3yFhXCB) to ensure the changes get early feedback which can make both your efforts more effective, and review quick and simple.

Licence
-------
For licence information see the file [LICENCE](LICENSE)

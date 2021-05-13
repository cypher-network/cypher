using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using CYPNode.UI;
using Newtonsoft.Json;

namespace CYPNode.Configuration
{
    public class SerfConfigurationTags
    {
        public string IPv;
        public string APIPort;
    }

    public class SerfConfiguration
    {
        public string node_name;
        public bool disable_coordinates;
        public SerfConfigurationTags tags = new();
        public string bind;
        public string advertise;
        public string profile;
        public string rpc_addr;
    }

    public class Network
    {
        private readonly IUserInterface _userInterface;
        private readonly UserInterfaceChoice _optionCancel = new(string.Empty);
        private readonly IList<IPService> _ipServices = IPServices.Services;

        public class ConfigurationClass
        {
            public string NodeName { get; set; }
            public IPAddress IPAddress { get; set; }
            public ushort ApiPortPublic { get; set; } = 7000;
            public ushort ApiPortLocal { get; set; }
            public ushort SerfPortRpc { get; set; } = 7373;
            public ushort SerfPortPublic { get; set; } = 7946;

            public string GetSerfConfiguration()
            {
                var config = new SerfConfiguration
                {
                    node_name = NodeName,
                    disable_coordinates = true,
                    tags =
                    {
                        IPv = "4",
                        APIPort = ApiPortPublic.ToString()
                    },
                    bind = $"0.0.0.0:{SerfPortPublic}",
                    advertise = $"{IPAddress}:{SerfPortPublic}",
                    profile = "wan",
                    rpc_addr = $"127.0.0.0:{SerfPortRpc}"
                };

                return JsonConvert.SerializeObject(config, Formatting.Indented);
            }
        }

        public ConfigurationClass Configuration { get; } = new();

        public Network(IUserInterface userInterface)
        {
            _userInterface = userInterface.SetTopic("Network");
        }

        public bool Do()
        {
            return StepIntroduction();
        }

        private bool SetPort(string prompt, out ushort port)
        {
            var section = new TextInput<ushort>(
                prompt,
                (string portString) => ushort.TryParse(portString, out _),
                (string portString) => ushort.Parse(portString));

            return _userInterface.Do(section, out port);
        }

        #region Introduction

        private bool StepIntroduction()
        {
            UserInterfaceChoice optionContinue = new("Continue network setup");

            var section = new UserInterfaceSection(
                "Network configuration",
                "Cypher nodes communicate with each other directly over an API interface. In order for nodes " +
                "to form a cluster, an application called \"Serf\" is used. For a proper node setup, the following " +
                "components need to be configured:" + Environment.NewLine +
                Environment.NewLine +
                "- A Serf agent is running. In most cases this agent runs on the same instance as cypnode" + Environment.NewLine +
                "- The Serf agent can communicate with remote Serf instances (IPv4, using both TCP and UDP)" + Environment.NewLine +
                "- Your cypnode can communicate with this Serf agent over its RPC port (TCP)" + Environment.NewLine +
                "- Your cypnode can communicate with remote nodes over their API ports (TCP)" + Environment.NewLine +
                "- Remove cypnodes can communicate with your cypnode over your API port (TCP)" + Environment.NewLine +
                "- Your cypnode can communicate with this Serf instance over its RPC port (TCP)" + Environment.NewLine +
                Environment.NewLine +
                Environment.NewLine +
                "      your node                              remote nodes       " + Environment.NewLine +
                "     ┌───────────────────────────┐          ┌──────────────────┐" + Environment.NewLine +
                "     │  ┌───────────┐            │          │   ┌───────────┐  │" + Environment.NewLine +
                "     │  │  cypnode  ◄────────────┼───────0──┼───►  cypnode  │  │" + Environment.NewLine +
                "     │  │           │<api port>  │       │  │   │           │  │" + Environment.NewLine +
                "     │  └─────┬─────┘            │       │  │   └─────┬─────┘  │" + Environment.NewLine +
                "     │        │<rpc port>        │       │  │         │        │" + Environment.NewLine +
                "     │  ┌─────▼─────┐            │       │  │   ┌─────▼─────┐  │" + Environment.NewLine +
                "     │  │   serf    ◄────────────┼────0──┼──┼───►   serf    │  │" + Environment.NewLine +
                "     │  │           │<serf port> │    │  │  │   └───────────┘  │" + Environment.NewLine +
                "     │  └───────────┘            │    │  │  └──────────────────┘" + Environment.NewLine +
                "     └───────────────────────────┘    │  │  ┌──────────────────┐" + Environment.NewLine +
                "                                      │  │  │   ┌───────────┐  │" + Environment.NewLine +
                "                                      │  └──┼───►  cypnode  │  │" + Environment.NewLine +
                "                                      │     │   └─────┬─────┘  │" + Environment.NewLine +
                "                                      │     │         │        │" + Environment.NewLine +
                "                                      │     │   ┌─────▼─────┐  │" + Environment.NewLine +
                "                                      └─────┼───►   serf    │  │" + Environment.NewLine +
                "                                            │   └───────────┘  │" + Environment.NewLine +
                "                                            └──────────────────┘" + Environment.NewLine,
                new[]
                {
                    optionContinue
                });

            var choice = _userInterface.Do(section);

            if (choice.Equals(optionContinue))
            {
                return StepNodeName();
            }

            return false;
        }

        private bool StepNodeName()
        {
            UserInterfaceChoice optionManualNodeName = new("Manually enter node name");
            UserInterfaceChoice optionGenerateNodeName = new("Automatically generate a node name");

            var section = new UserInterfaceSection(
                "Node name",
                "Your node is identified by a name, which can be a human-readable name or a randomly generated name. The node name must be unique in the network.",
                new[]
                {
                    optionManualNodeName,
                    optionGenerateNodeName
                });

            var choiceNodeName = _userInterface.Do(section);

            if (choiceNodeName.Equals(optionManualNodeName))
            {
                return StepNodeNameManual();
            }

            if (choiceNodeName.Equals(optionGenerateNodeName))
            {
                var bytes = new byte[10];
                new RNGCryptoServiceProvider().GetBytes(bytes);
                Configuration.NodeName = $"cypher-{BitConverter.ToString(bytes).Replace("-", "").ToLower()}";
                return StepIpAddress();
            }

            return false;
        }

        private bool StepNodeNameManual()
        {
            var section = new TextInput<string>(
                "Enter node name (1 - 32 characters, allowed characters: a-z, A-Z, 0-9, \"_\" and \"-\")",
                nodeName =>
                    nodeName.Length >= 1 &&
                    nodeName.Length <= 32 &&
                    nodeName.All(character => char.IsLetterOrDigit(character) ||
                                                      character.Equals('_') ||
                                                      character.Equals('-')),
                nodeName => nodeName);

            var success = _userInterface.Do(section, out var nodeName);
            if (success)
            {
                Configuration.NodeName = nodeName;
                return StepIpAddress();
            }

            return success;
        }

        #endregion

        #region IP address
        private readonly UserInterfaceChoice _optionIpAddressManual = new("Manually enter IP address");
        private readonly UserInterfaceChoice _optionIpAddressAuto = new("Find IP address automatically");
        private UserInterfaceChoice _choiceIpAddress;

        private bool StepIpAddress()
        {
            var section = new UserInterfaceSection(
                "Public IP address",
                "Your node needs to be able to communicate with other nodes on the internet. For this you " +
                "need to broadcast your public IP address, which is in many cases not the same as your local network " +
                "address. Addresses starting with 10.x.x.x, 172.16.x.x and 192.168.x.x are local addresses and " +
                "should not be broadcast to the network. When you do not know your public IP address, you can find " +
                "it by searching for 'what is my ip address'. This does not work if you configure a remote node, " +
                "like for example a VPS. You can also choose to find your public IP address automatically.",
                new[]
                {
                    _optionIpAddressManual,
                    _optionIpAddressAuto
                });

            _choiceIpAddress = _userInterface.Do(section);

            if (_choiceIpAddress.Equals(_optionIpAddressManual))
            {
                return StepIpAddressManual();
            }

            if (_choiceIpAddress.Equals(_optionIpAddressAuto))
            {
                return StepIpAddressAuto();
            }

            return false;
        }

        private bool StepIpAddressManual()
        {
            var section = new TextInput<IPAddress>(
                "Enter IP address (e.g. 123.1.23.123)",
                ipAddress => IPAddress.TryParse(ipAddress, out _),
                ipAddress => IPAddress.Parse(ipAddress));

            var success = _userInterface.Do(section, out var ipAddress);
            if (success)
            {
                Configuration.IPAddress = ipAddress;
                return StepApiPortPublic();
            }

            return success;
        }

        private bool StepIpAddressAuto()
        {
            while (Configuration.IPAddress == null)
            {
                var section = new UserInterfaceSection(
                    _optionIpAddressAuto.Text,
                    "Please choose the service to use for automatic IP address detection.",
                    _ipServices.ToList().Select(service =>
                        new UserInterfaceChoice(service.ToString())).ToArray());

                var choiceIpAddressService = _userInterface.Do(section);
                if (choiceIpAddressService.Equals(_optionCancel))
                {
                    return false;
                }

                try
                {
                    var selectedIpAddressService = _ipServices
                        .First(service => service.ToString() == choiceIpAddressService.Text);
                    Configuration.IPAddress = selectedIpAddressService.Read();
                }
                catch (Exception)
                {
                    // Cannot get IP address; ignore error
                }
            }

            return StepApiPortPublic();
        }
        #endregion IP address

        #region API
        private readonly UserInterfaceChoice _optionApiPortDefault = new("Use default port");
        private readonly UserInterfaceChoice _optionApiPortSame = new("Use same local API port as public API port");
        private readonly UserInterfaceChoice _optionApiPortChange = new("Set API port");

        private bool StepApiPortPublic()
        {
            var section = new UserInterfaceSection(
                "Public API Port",
                "Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                "API listens on a configurable public TCP port, the default port number is " +
                $"{Configuration.ApiPortPublic.ToString()}. You have to make sure that this port is properly " +
                "configured in your firewall or router.",
                new[]
                {
                    _optionApiPortDefault,
                    _optionApiPortChange
                });

            var choicePortApi = _userInterface.Do(section);

            if (choicePortApi.Equals(_optionApiPortDefault))
            {
                // Skip setting local port when default public port is selected
                Configuration.ApiPortLocal = Configuration.ApiPortPublic;
                return SerfPortRpc();
            }

            if (choicePortApi.Equals(_optionApiPortChange))
            {
                return StepApiPortPublicSet();
            }

            return false;
        }

        private bool StepApiPortPublicSet()
        {
            var portSet = SetPort("Enter public API port (e.g. 7000)", out var port);
            if (!portSet) return false;

            Configuration.ApiPortPublic = port;
            return StepApiPortLocal();
        }

        private bool StepApiPortLocal()
        {
            var section = new UserInterfaceSection(
                "Local API Port",
                $"In case your node listens on a different local TCP port than the public port you have " +
                $"just configured ({Configuration.ApiPortPublic}), for example when you have configured a different " +
                "port mapping in your firewall or router, you can set this port here. Most people will want to skip " +
                "this step.",
                new[]
                {
                    _optionApiPortSame,
                    _optionApiPortChange
                });

            var choicePortApi = _userInterface.Do(section);

            if (choicePortApi.Equals(_optionApiPortSame))
            {
                Configuration.ApiPortLocal = Configuration.ApiPortPublic;
                return SerfPortPublic();
            }

            if (choicePortApi.Equals(_optionApiPortChange))
            {
                return StepApiPortLocalSet();
            }

            return false;
        }

        private bool StepApiPortLocalSet()
        {
            var portSet = SetPort("Enter local API port (e.g. 7000)", out var port);
            if (!portSet) return false;

            Configuration.ApiPortLocal = port;
            return SerfPortPublic();
        }
        #endregion API

        #region Serf
        private readonly UserInterfaceChoice _optionPortSerfPublicDefault = new("Use default public Serf port");
        private readonly UserInterfaceChoice _optionPortSerfPublicChange = new("Set public Serf port");
        private bool SerfPortPublic()
        {
            var section = new UserInterfaceSection(
                "Serf Public Port",
                "Node clusters communicate using HashiCorp Serf. Serf agent communicate with other Serf " +
                $"agents over a publicly accessible port. By default, Serf uses port {Configuration.SerfPortPublic.ToString()}.",
                new[]
                {
                    _optionPortSerfPublicDefault,
                    _optionPortSerfPublicChange
                });

            var choicePortPublic = _userInterface.Do(section);

            if (choicePortPublic.Equals(_optionPortSerfPublicDefault))
            {
                return SerfPortRpc();
            }

            if (choicePortPublic.Equals(_optionPortSerfPublicChange))
            {
                return SerfPortPublicSet();
            }

            return false;
        }

        private bool SerfPortPublicSet()
        {
            var portSet = SetPort($"Enter public Serf port (e.g. {Configuration.SerfPortPublic.ToString()})", out var port);
            if (!portSet) return false;

            Configuration.SerfPortPublic = port;
            return true;
        }

        private readonly UserInterfaceChoice _optionPortSerfRpcDefault = new("Use default RPC port");
        private readonly UserInterfaceChoice _optionPortSerfRpcChange = new("Set RPC port");
        private bool SerfPortRpc()
        {
            var section = new UserInterfaceSection(
                "Serf RPC Port",
                "Node clusters communicate using HashiCorp Serf. Your node communicates with a local Serf " +
                "instance over its Remote Procedure Call (RPC) TCP port. This port does not need to be accessible to " +
                $"other nodes. By default, Serf uses port {Configuration.SerfPortRpc.ToString()}.",
                new[]
                {
                    _optionPortSerfRpcDefault,
                    _optionPortSerfRpcChange
                });

            var choicePortRpc = _userInterface.Do(section);

            if (choicePortRpc.Equals(_optionPortSerfRpcDefault))
            {
                return true;
            }

            if (choicePortRpc.Equals(_optionPortSerfRpcChange))
            {
                return SerfPortRpcSet();
            }

            return false;
        }

        private bool SerfPortRpcSet()
        {
            var portSet = SetPort($"Enter Serf API port (e.g. {Configuration.SerfPortRpc.ToString()})", out var port);
            if (!portSet) return false;

            Configuration.SerfPortRpc = port;
            return true;
        }
        #endregion Serf
    }
}
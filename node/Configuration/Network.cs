using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using CypherNetworkNode.UI;

namespace CypherNetworkNode.Configuration
{
    public class SerfConfigurationTags
    {
        public string IPv;
        public string ApiPort;
    }

    public class SerfConfiguration
    {
        public string NodeName;
        public bool DisableCoordinates;
        public readonly SerfConfigurationTags Tags = new();
        public string Bind;
        public string Advertise;
        public string Profile;
        public string RpcAddr;
    }

    public class Network
    {
        private readonly IUserInterface _userInterface;
        private readonly UserInterfaceChoice _optionCancel = new(string.Empty);
        private readonly IList<IPService> _ipServices = IPServices.Services;

        public class ConfigurationClass
        {
            public string NodeName { get; set; }
            public IPAddress IpAddress { get; set; }
            public ushort ApiPortPublic { get; set; } = 48655;
            public ushort ApiPortLocal { get; set; }
            public ushort AdvertisePort { get; set; } = 5146;
            public ushort ListeningPort { get; set; } = 7946;
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
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool StepIntroduction()
        {
            UserInterfaceChoice optionContinue = new("Continue network setup");

            var section = new UserInterfaceSection(
                "Network configuration",
                "Cypher nodes communicate with each other directly over tcp. " +
                "For a proper node setup, the following " +
                "components need to be configured:" + Environment.NewLine +
                Environment.NewLine +
                "      your node                                remote nodes       " + Environment.NewLine +
                "     ┌─────────────────────────────┐          ┌──────────────────┐" + Environment.NewLine +
                "     │  ┌───────────┐              │          │   ┌───────────┐  │" + Environment.NewLine +
                "     │  │  cypnode  ◄──────────────┼───────0──┼───►  cypnode  │  │" + Environment.NewLine +
                "     │  │           │<api port>    │       │  │   │           │  │" + Environment.NewLine +
                "     │  └─────┬─────┘              │       │  │   └─────┬─────┘  │" + Environment.NewLine +
                "     │        │<tcp port>          │       │  │         │        │" + Environment.NewLine +
                "     │  ┌─────▼─────┐              │       │  │   ┌─────▼─────┐  │" + Environment.NewLine +
                "     │  │   gossip  ◄──────────────┼────0──┼──┼───►   gossip  │  │" + Environment.NewLine +
                "     │  │           │<gossip port> │    │  │  │   └───────────┘  │" + Environment.NewLine +
                "     │  └───────────┘              │    │  │  └──────────────────┘" + Environment.NewLine +
                "     └─────────────────────────────┘    │  │  ┌──────────────────┐" + Environment.NewLine +
                "                                        │  │  │   ┌───────────┐  │" + Environment.NewLine +
                "                                        │  └──┼───►  cypnode  │  │" + Environment.NewLine +
                "                                        │     │   └─────┬─────┘  │" + Environment.NewLine +
                "                                        │     │         │        │" + Environment.NewLine +
                "                                        │     │   ┌─────▼─────┐  │" + Environment.NewLine +
                "                                        └─────┼───►   gossip  │  │" + Environment.NewLine +
                "                                              │   └───────────┘  │" + Environment.NewLine +
                "                                              └──────────────────┘" + Environment.NewLine,
                new[]
                {
                    optionContinue
                });

            var choice = _userInterface.Do(section);
            return choice.Equals(optionContinue) && StepNodeName();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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

            if (!choiceNodeName.Equals(optionGenerateNodeName)) return false;
            var bytes = new byte[10];
            var randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(bytes);
            Configuration.NodeName = $"cypher-{BitConverter.ToString(bytes).Replace("-", "").ToLower()}";
            return StepIpAddress();

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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
            if (!success) return success;
            Configuration.NodeName = nodeName;
            return StepIpAddress();
        }
        #endregion

        #region IP address
        private readonly UserInterfaceChoice _optionIpAddressManual = new("Manually enter IP address");
        private readonly UserInterfaceChoice _optionIpAddressAuto = new("Find IP address automatically");
        private UserInterfaceChoice _choiceIpAddress;

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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
            return _choiceIpAddress.Equals(_optionIpAddressAuto) && StepIpAddressAuto();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool StepIpAddressManual()
        {
            var section = new TextInput<IPAddress>(
                "Enter IP address (e.g. 123.1.23.123)",
                ipAddress => IPAddress.TryParse(ipAddress, out _),
                ipAddress => IPAddress.Parse(ipAddress));

            var success = _userInterface.Do(section, out var ipAddress);
            if (!success) return success;
            Configuration.IpAddress = ipAddress;
            return StepApiPortPublic();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool StepIpAddressAuto()
        {
            while (Configuration.IpAddress == null)
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
                    Configuration.IpAddress = selectedIpAddressService.Read();
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

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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
                Configuration.ApiPortLocal = Configuration.ApiPortPublic;
                return GossipAdvertisePortPublic();
            }

            if (choicePortApi.Equals(_optionApiPortChange))
            {
                return StepApiPortPublicSet();
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool StepApiPortPublicSet()
        {
            var portSet = SetPort("Enter public API port (e.g. 48655)", out var port);
            if (!portSet) return false;

            Configuration.ApiPortPublic = port;
            return StepApiPortLocal();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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
                return GossipAdvertisePortPublic();
            }

            if (choicePortApi.Equals(_optionApiPortChange))
            {
                return StepApiPortLocalSet();
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool StepApiPortLocalSet()
        {
            var portSet = SetPort("Enter local API port (e.g. 48655)", out var port);
            if (!portSet) return false;

            Configuration.ApiPortLocal = port;
            return GossipAdvertisePortPublic();
        }
        #endregion API

        #region Gossip
        private readonly UserInterfaceChoice _optionPortSerfPublicDefault = new("Use default public Listening port");
        private readonly UserInterfaceChoice _optionPortSerfPublicChange = new("Set public Listening port");
        
        
        private readonly UserInterfaceChoice _optionPortSerfRpcDefault = new("Use default Advertise port");
        private readonly UserInterfaceChoice _optionPortSerfRpcChange = new("Set Advertise port");
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool GossipAdvertisePortPublic()
        {
            var section = new UserInterfaceSection(
                "Gossip Public Port",
                "Node clusters communicate with other nodes " +
                $"over a publicly accessible port. By default, Gossip uses port {Configuration.AdvertisePort.ToString()}.",
                new[]
                {
                    _optionPortSerfRpcDefault,
                    _optionPortSerfRpcChange
                });

            var choicePortRpc = _userInterface.Do(section);

            if (choicePortRpc.Equals(_optionPortSerfRpcDefault))
            {
                return ListeningPortPublic();
            }

            if (choicePortRpc.Equals(_optionPortSerfRpcChange))
            {
                return GossipAdvertisePortPublicSet();
            }

            return false;
        }

        private bool GossipAdvertisePortPublicSet()
        {
            var portSet = SetPort($"Enter Gossip Advertise port (e.g. {Configuration.AdvertisePort.ToString()})", out var port);
            if (!portSet) return false;

            Configuration.AdvertisePort = port;
            return true;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool ListeningPortPublic()
        {
            var section = new UserInterfaceSection(
                "Listening Public Port",
                "Nodes and Wallets communicate with your node " +
                $"over a publicly accessible port. By default, the Listening uses port {Configuration.ListeningPort.ToString()}.",
                new[]
                {
                    _optionPortSerfPublicDefault,
                    _optionPortSerfPublicChange
                });

            var choicePortPublic = _userInterface.Do(section);

            if (choicePortPublic.Equals(_optionPortSerfPublicDefault))
            {
                return true;
            }
            return choicePortPublic.Equals(_optionPortSerfPublicChange) && GossipListeningPortPublicSet();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool GossipListeningPortPublicSet()
        {
            var portSet = SetPort($"Enter public Listening port (e.g. {Configuration.ListeningPort.ToString()})", out var port);
            if (!portSet) return false;

            Configuration.ListeningPort = port;
            return true;
        }
        #endregion Serf
    }
}
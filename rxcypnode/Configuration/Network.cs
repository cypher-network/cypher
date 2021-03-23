using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using rxcypnode.UI;

namespace rxcypnode.Configuration
{
    public class Network
    {
        private IList<IPService> _ipServices = new List<IPService>()
        {
            new ("ident.me", new Uri("https://v4.ident.me")),
            new ("ipify.org", new Uri("https://api.ipify.org")),
            new ("my-ip.io", new Uri("https://api4.my-ip.io/ip.txt")),
            new ("seeip.org", new Uri("https://ip4.seeip.org"))
        };
        
        private class IPService
        {
            public IPService(string name, Uri uri)
            {
                _name = name;
                _uri = uri;
            }

            private readonly string _name;
            private readonly Uri _uri;

            public override string ToString()
            {
                return $"{_name} ({_uri})";
            }

            public IPAddress Read()
            {
                using var client = new WebClient();
                var response = client.DownloadString(_uri);
                return IPAddress.Parse(response);
            }
        }

        private readonly IUserInterface _userInterface;
        private readonly UserInterfaceChoice _optionCancel = new(string.Empty);

        public class ConfigurationClass
        {
            public IPAddress IPAddress { get; set; }
            public ushort ApiPortPublic { get; set; } = 7000;
            public ushort ApiPortLocal { get; set; }
            public ushort SerfRPCPort { get; set; } = 7373;
        }

        public ConfigurationClass Configuration { get; } = new();
        
        public Network(IUserInterface userInterface)
        {
            _userInterface = userInterface;
        }

        public bool Do()
        {
            return StepIpAddress();
        }

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
                (ipAddress) => IPAddress.TryParse(ipAddress, out _),
                (ipAddress) => IPAddress.Parse(ipAddress));

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
                return StepApiPortLocal();
            }
            
            if (choicePortApi.Equals(_optionApiPortChange))
            {
                return StepApiPortPublicSet();
            }

            return false;
        }

        private bool StepApiPortPublicSet()
        {
            var section = new TextInput<ushort>(
                "Enter public API port (e.g. 7000)",
                (string port) => ushort.TryParse(port, out _),
                (string port) => ushort.Parse(port));

            var success = _userInterface.Do(section, out var port);
            if (success)
            {
                Configuration.ApiPortPublic = port;
                return StepApiPortLocal();
            }

            return success;
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
                return SerfRPCPort();
            }
            
            if (choicePortApi.Equals(_optionApiPortChange))
            {
                return StepApiPortLocalSet();
            }

            return false;
        }
        
        private bool StepApiPortLocalSet()
        {
            var section = new TextInput<ushort>(
                "Enter local API port (e.g. 7000)",
                (string port) => ushort.TryParse(port, out _),
                (string port) => ushort.Parse(port));

            var success = _userInterface.Do(section, out var port);
            if (success)
            {
                Configuration.ApiPortLocal = port;
                return SerfRPCPort();
            }

            return success;
        }
        #endregion API
        
        #region Serf
        private readonly UserInterfaceChoice _optionPortSerfRpcDefault = new("Use default RPC port");
        private readonly UserInterfaceChoice _optionPortSerfChange = new("Set RPC port");

        private bool SerfRPCPort()
        {
            var section = new UserInterfaceSection(
                "Serf RPC Port",
                "Node clusters communicate using HashiCorp Serf. Your node communicates with a local Serf " +
                "instance over its Remote Procedure Call (RPC) TCP port. This port does not need to be accessible to " +
                $"other nodes. By default, Serf uses port {Configuration.SerfRPCPort.ToString()}.",
                new[]
                {
                    _optionPortSerfRpcDefault,
                    _optionPortSerfChange
                });

            var choicePortApi = _userInterface.Do(section);

            if (choicePortApi.Equals(_optionPortSerfRpcDefault))
            {
                return true;
            }
            
            if (choicePortApi.Equals(_optionPortSerfChange))
            {
                return SerfRPCPortSet();
            }

            return false;
        }
        
        private bool SerfRPCPortSet()
        {
            var section = new TextInput<ushort>(
                "Enter Serf API port (e.g. 7373)",
                (string port) => ushort.TryParse(port, out _),
                (string port) => ushort.Parse(port));

            var success = _userInterface.Do(section, out var port);
            if (success)
            {
                Configuration.SerfRPCPort = port;
                return true;
            }

            return success;
        }
        #endregion Serf
    }
}
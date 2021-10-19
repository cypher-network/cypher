// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Net;
using CYPCore.Models;
using McMaster.Extensions.CommandLineUtils;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace CYPNode.Setup
{
    public class Config
    {
        private readonly AppOptions _appConfigurationOptions;
        private readonly string _filePath;
        private readonly JObject _jObject;

        private CommandOption _optionName;
        private CommandOption _optionAutoIp;
        private CommandOption _optionRestApi;
        private CommandOption _optionRestApiPort;
        private CommandOption _optionGossipPort;
        private CommandOption _optionStakingEnable;
        private CommandOption _optionReward;

        private class TextInput<T>
        {
            private readonly Func<string, bool> _validation;
            private readonly Func<string, T> _cast;

            public TextInput(Func<string, bool> validation, Func<string, T> cast)
            {
                _validation = validation;
                _cast = cast;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="value"></param>
            /// <returns></returns>
            public bool IsValid(string value)
            {
                return _validation == null || _validation.Invoke(value);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="input"></param>
            /// <param name="output"></param>
            /// <returns></returns>
            public bool Cast(string input, out T output)
            {
                output = _cast == null
                    ? default
                    : _cast(input);

                return output != null && (_cast == null || !output.Equals(default));
            }
        }

        public Config()
        {
            _appConfigurationOptions = new AppOptions
            {
                Gossip = new GossipOptions(),
                Staking = new StakingOptions()
            };
            _filePath = Path.Combine(AppContext.BaseDirectory, Program.AppSettingsFile);
            try
            {
                _jObject = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(File.ReadAllText(_filePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public int Init(string[] args)
        {
            var app = new CommandLineApplication();
            try
            {
                app.HelpOption();
                _optionName = app.Option("-n|--name <NAME>",
                    "Your node is identified by a name, which can be a human-readable name or a randomly generated name. The node name must be unique in the network.",
                    CommandOptionType.SingleValue);
                _optionAutoIp = app.Option("-a|--autoip <AUTOIP>", "Automatic IP address detection.",
                    CommandOptionType.SingleValue);
                _optionRestApi = app.Option("-w|--webapi <WEBAPI>",
                    "Your node needs to be able to communicate with other nodes on the internet. For this you " +
                    "need to broadcast your public IP address, which is in many cases not the same as your local network " +
                    "address. Addresses starting with 10.x.x.x, 172.16.x.x and 192.168.x.x are local addresses and " +
                    "should not be broadcast to the network. When you do not know your public IP address, you can find " +
                    "it by searching for 'what is my ip address'. This does not work if you configure a remote node, " +
                    "like for example a VPS. You can also choose to find your public IP address automatically.",
                    CommandOptionType.SingleValue);
                _optionRestApiPort = app.Option("-p|--port <PORT>",
                    "Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                    "API listens on a configurable public TCP port", CommandOptionType.SingleValue);
                _optionGossipPort = app.Option("-g|--gossip <GOSSIP>",
                    "Node clusters communicate using swim gossip protocol. Your node communicate with other nodes " +
                    $"over a publicly accessible port", CommandOptionType.SingleValue);
                _optionStakingEnable = app.Option("-s|--staking <STAKING>",
                    "Enable staking if you want to earn rewards. Staking does require that you have some funds available.",
                    CommandOptionType.NoValue);
                _optionReward = app.Option("-r|--reward <WALLET>",
                    "An address that will receive coinbase transactions",
                    CommandOptionType.SingleValue);
                app.OnExecute(Invoke);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }

            return app.Execute(args);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private int Invoke()
        {
            if (_optionName.HasValue())
            {
                var nodeName = _optionName.Value();
                var name = new TextInput<string>(
                    personalName => personalName.Length is >= 1 and <= 32 && personalName.All(character =>
                        char.IsLetterOrDigit(character) || character.Equals('_') || character.Equals('-')),
                    personalName => nodeName);
                if (!name.IsValid(nodeName))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(
                        "[Only 1 - 32 characters, allowed characters: a-z, A-Z, 0-9, \"_\" and \"-\")]");
                    return 1;
                }

                var jTokenName = _jObject.SelectToken("Node.Name");
                jTokenName.Replace(nodeName);
            }

            if (_optionAutoIp.HasValue())
            {
                var autoIp = _optionAutoIp.Value();
                if (bool.TryParse(autoIp, out _))
                {
                    using var client = new WebClient();
                    var response = client.DownloadString("https://v4.ident.me");
                    var address = IPAddress.Parse(response).ToString();
                    _appConfigurationOptions.RestApi = address;
                    var jTokenWebApiAdvertise = _jObject.SelectToken("Node.RestApi");
                    jTokenWebApiAdvertise.Replace(address);
                }
            }

            if (_optionRestApi.HasValue())
            {
                var webAdvertise = _optionRestApi.Value();
                var webapi = new TextInput<IPAddress>(ipAddress => IPAddress.TryParse(ipAddress, out _),
                    IPAddress.Parse);
                if (!webapi.IsValid(webAdvertise))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[Invalid IP address]");
                    return 1;
                }

                _appConfigurationOptions.RestApi = webAdvertise;
                var jTokenWebApiAdvertise = _jObject.SelectToken("Node.RestApi");
                jTokenWebApiAdvertise.Replace(webAdvertise);
            }

            if (_optionRestApiPort.HasValue())
            {
                var webApiPort = _optionRestApiPort.Value();
                if (int.TryParse(webApiPort, out var port))
                {
                    _appConfigurationOptions.RestApi = $"{_appConfigurationOptions.RestApi}:{port}";
                    var jTokenWebApiAdvertise = _jObject.SelectToken("Node.RestApi");
                    jTokenWebApiAdvertise.Replace(_appConfigurationOptions.RestApi);
                }
            }

            if (_optionGossipPort.HasValue())
            {
                var gossipPort = _optionGossipPort.Value();
                if (int.TryParse(gossipPort, out var port))
                {
                    _appConfigurationOptions.Gossip.Listening = $"{_appConfigurationOptions.RestApi}:{port}";
                    var jTokenGossipListening = _jObject.SelectToken("Node.Gossip.Listening");
                    jTokenGossipListening.Replace(_appConfigurationOptions.Gossip.Listening);
                }
            }

            if (!_optionStakingEnable.HasValue()) return 0;
            _appConfigurationOptions.Staking.Enabled = true;
            var jTokenStakeEnabled = _jObject.SelectToken("Node.Staking.Enabled");
            jTokenStakeEnabled.Replace(true);
            if (!_optionReward.HasValue())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Enabled staking requires wallet settings]");
                return 1;
            }

            if (_optionReward.HasValue())
            {
                var rewardAddress = _optionReward.Value();
                var base58CheckEncoder = new Base58CheckEncoder();
                var isBase58 = base58CheckEncoder.IsMaybeEncoded(rewardAddress);
                if (isBase58)
                {
                    _appConfigurationOptions.Staking.RewardAddress = rewardAddress;
                    var jTokenWalletAddress = _jObject.SelectToken("Node.Staking.RewardAddress");
                    jTokenWalletAddress.Replace(rewardAddress);
                }
            }

            File.WriteAllText(_filePath, JToken.FromObject(_jObject).ToString());
            Console.WriteLine("Settings updated");
            return 0;
        }
    }
}
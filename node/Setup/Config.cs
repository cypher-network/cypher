// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using System.Linq;
using System.Net;
using CypherNetwork.Extensions;
using CypherNetwork.Models;
using McMaster.Extensions.CommandLineUtils;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace CypherNetworkNode.Setup;

public class Config
{
    private readonly AppOptions _appConfigurationOptions;
    private readonly string _filePath;
    private readonly JObject _jObject;

    private CommandOption _optionName;
    private CommandOption _optionAutoIp;
    private CommandOption _optionHttpEndPoint;
    private CommandOption _optionHttpEndPointPort;
    private CommandOption _optionHttpsPort;
    private CommandOption _optionGossipListeningPort;
    private CommandOption _optionGossipAdvertisingPort;
    private CommandOption _optionX509CertificatePath;
    private CommandOption _optionX509CertificatePassword;
    private CommandOption _optionX509CertificateThumbprint;
    private CommandOption _optionStakingEnable;
    private CommandOption _optionKeyRingName;
        
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
            _optionHttpEndPoint = app.Option("-w|--webapi <WEBAPI>",
                "Your node needs to be able to communicate with other nodes on the internet. For this you " +
                "need to broadcast your public IP address, which is in many cases not the same as your local network " +
                "address. Addresses starting with 10.x.x.x, 172.16.x.x and 192.168.x.x are local addresses and " +
                "should not be broadcast to the network. When you do not know your public IP address, you can find " +
                "it by searching for 'what is my ip address'. This does not work if you configure a remote node, " +
                "like for example a VPS. You can also choose to find your public IP address automatically.",
                CommandOptionType.SingleValue);
            _optionHttpEndPointPort = app.Option("-p|--port <PORT>",
                "Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                "API listens on a configurable public TCP port.", CommandOptionType.SingleValue);
            _optionHttpsPort = app.Option("-sp|--secureport <SECUREPORT>",
                "Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                "API listens on a configurable public SSL TCP port.", CommandOptionType.SingleValue);
            _optionGossipListeningPort = app.Option("-g|--gossip <GOSSIP>",
                "Node clusters communicate using swim gossip protocol. Your node communicate with other nodes " +
                $"over a publicly accessible port.", CommandOptionType.SingleValue);
            _optionGossipAdvertisingPort = app.Option("-m|--members <MEMBERS>",
                "Node clusters communicate using swim gossip protocol. Your node communicate with other nodes " +
                $"over a publicly accessible port for member dissemination.", CommandOptionType.SingleValue);
            _optionX509CertificatePath =  app.Option("-fcert|--filecert <FILECERT>",
                "The name of a certificate file, relative to the directory that contains the application content files.", CommandOptionType.SingleValue);
            _optionX509CertificatePassword =  app.Option("-pcert|--passcert <PASSCERT>",
                "The password required to access the X.509 certificate data.", CommandOptionType.SingleValue);
            _optionX509CertificateThumbprint =  app.Option("-tcert|--thumbprint <THUMBPRINT>",
                "The thumbprint (as a hex string) of the certificate to resolve.", CommandOptionType.SingleValue);
            _optionStakingEnable = app.Option("-s|--staking <STAKING>",
                "Enable staking if you want to earn rewards. Staking does require that you have some funds available.",
                CommandOptionType.NoValue);
            _optionKeyRingName = app.Option("-rngname|--ringname <KEYRINGNAME>", 
                "Replace the existing key ring name with a new default signing name.", CommandOptionType.SingleValue);
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
                _appConfigurationOptions.HttpEndPoint = address;
                var jTokenWebApiAdvertise = _jObject.SelectToken("Node.RestApi");
                jTokenWebApiAdvertise.Replace(address);
            }
        }

        if (_optionHttpEndPoint.HasValue())
        {
            var webAdvertise = _optionHttpEndPoint.Value();
            var webapi = new TextInput<IPAddress>(ipAddress => IPAddress.TryParse(ipAddress, out _),
                IPAddress.Parse);
            if (!webapi.IsValid(webAdvertise))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Invalid IP address]");
                return 1;
            }

            _appConfigurationOptions.HttpEndPoint = webAdvertise;
            var jTokenWebApiAdvertise = _jObject.SelectToken("Node.HttpEndPoint");
            jTokenWebApiAdvertise.Replace(webAdvertise);
        }

        if (_optionHttpEndPointPort.HasValue())
        {
            var webApiPort = _optionHttpEndPointPort.Value();
            if (int.TryParse(webApiPort, out var port))
            {
                _appConfigurationOptions.HttpEndPoint = $"{_appConfigurationOptions.HttpEndPoint}:{port}";
                var jTokenWebApiAdvertise = _jObject.SelectToken("Node.HttpEndPoint");
                jTokenWebApiAdvertise.Replace(_appConfigurationOptions.HttpEndPoint);
            }
        }
            
        if (_optionHttpsPort.HasValue())
        {
            var sslPort = _optionHttpsPort.Value();
            if (int.TryParse(sslPort, out var port))
            {
                _appConfigurationOptions.HttpsPort = port;
                var jTokenWebApiAdvertise = _jObject.SelectToken("Node.HttpsPort");
                jTokenWebApiAdvertise.Replace(_appConfigurationOptions.HttpEndPoint);
            }
        }

        if (_optionGossipListeningPort.HasValue())
        {
            var gossipPort = _optionGossipListeningPort.Value();
            if (int.TryParse(gossipPort, out var port))
            {
                _appConfigurationOptions.Gossip.Listening = $"{_appConfigurationOptions.HttpEndPoint}:{port}".ToBytes();
                var jTokenGossipListening = _jObject.SelectToken("Node.Gossip.Listening");
                jTokenGossipListening.Replace(_appConfigurationOptions.Gossip.Listening);
            }
        }
            
        if (_optionGossipAdvertisingPort.HasValue())
        {
            var gossipAdvertisePort = _optionGossipAdvertisingPort.Value();
            if (int.TryParse(gossipAdvertisePort, out var port))
            {
                _appConfigurationOptions.Gossip.Advertise = $"{_appConfigurationOptions.HttpEndPoint}:{port}".ToBytes();
                var jTokenGossipAdvertising = _jObject.SelectToken("Node.Gossip.Advertise");
                jTokenGossipAdvertising.Replace(_appConfigurationOptions.Gossip.Advertise);
            }
        }
            
        if (_optionX509CertificatePath.HasValue())
        {
            _appConfigurationOptions.Network.X509Certificate.CertPath = _optionX509CertificatePath.Value();;
            var jTokenCertificateFileName = _jObject.SelectToken("Node.Network.X509Certificate.CertPath");
            jTokenCertificateFileName.Replace(_appConfigurationOptions.Network.X509Certificate.CertPath);
        }
            
        if (_optionX509CertificatePassword.HasValue())
        {
            _appConfigurationOptions.Network.X509Certificate.Password = _optionX509CertificatePassword.Value();;
            var jTokenCertificatePassword= _jObject.SelectToken("Node.Network.X509Certificate.Password");
            jTokenCertificatePassword.Replace(_appConfigurationOptions.Network.X509Certificate.Password);
        }
            
        if (_optionX509CertificateThumbprint.HasValue())
        {
            _appConfigurationOptions.Network.X509Certificate.Thumbprint = _optionX509CertificateThumbprint.Value();;
            var jTokenCertificateThumbprint = _jObject.SelectToken("Node.Network.X509Certificate.Thumbprint");
            jTokenCertificateThumbprint.Replace(_appConfigurationOptions.Network.X509Certificate.Thumbprint);
        }

        if (_optionKeyRingName.HasValue())
        {
            _appConfigurationOptions.Network.SigningKeyRingName = _optionKeyRingName.Value();
            var jTokenKeyRingName= _jObject.SelectToken("Node.Network.SigningKeyRingName");
            jTokenKeyRingName.Replace(_appConfigurationOptions.Network.SigningKeyRingName);

            try
            {
                File.Delete(Path.Combine(CypherNetwork.Helper.Util.EntryAssemblyPath(), _appConfigurationOptions.Data.KeysProtectionPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }
            
        if (!_optionStakingEnable.HasValue()) return 0;
        _appConfigurationOptions.Staking.Enabled = true;
        var jTokenStakeEnabled = _jObject.SelectToken("Node.Staking.Enabled");
        jTokenStakeEnabled.Replace(true);

        File.WriteAllText(_filePath, JToken.FromObject(_jObject).ToString());
        Console.WriteLine("Settings updated");
        return 0;
    }
}
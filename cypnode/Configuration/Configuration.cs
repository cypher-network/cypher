using System;
using System.IO;
using CYPNode.UI;

namespace CYPNode.Configuration
{
    public class Configuration
    {
        private IUserInterface _userInterface;

        public Configuration(IUserInterface userInterface)
        {
            _userInterface = userInterface;

            var networkConfiguration = new Network(userInterface);
            if (!networkConfiguration.Do())
            {
                Cancel();
                return;
            }

            Console.WriteLine("Node name        : " + networkConfiguration.Configuration.NodeName);
            Console.WriteLine("Public IP address: " + networkConfiguration.Configuration.IPAddress);
            Console.WriteLine("Public API port  : " + networkConfiguration.Configuration.ApiPortPublic);
            Console.WriteLine("Local API port   : " + networkConfiguration.Configuration.ApiPortLocal);
            Console.WriteLine("Serf public port : " + networkConfiguration.Configuration.SerfPortPublic);
            Console.WriteLine("Serf RPC port    : " + networkConfiguration.Configuration.SerfPortRpc);
            Console.WriteLine();

            var configTemplate = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "Templates", Program.AppSettingsFile));
            var config = configTemplate
                .Replace("<API_ENDPOINT_BIND>", $"http://0.0.0.0:{networkConfiguration.Configuration.ApiPortLocal.ToString()}")
                .Replace("<API_ENDPOINT_PUBLIC>",
                    $"http://{networkConfiguration.Configuration.IPAddress}:{networkConfiguration.Configuration.ApiPortPublic.ToString()}")
                .Replace("<SERF_ENDPOINT_PUBLIC>",
                    $"{networkConfiguration.Configuration.IPAddress}:{networkConfiguration.Configuration.SerfPortPublic.ToString()}")
                .Replace("<SERF_ENDPOINT_BIND>",
                    $"0.0.0.0:{networkConfiguration.Configuration.SerfPortPublic.ToString()}")
                .Replace("<SERF_ENDPOINT_RPC>",
                    $"127.0.0.1:{networkConfiguration.Configuration.SerfPortRpc.ToString()}")
                .Replace("<SERF_NODE_NAME>", networkConfiguration.Configuration.NodeName);

            var configFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.AppSettingsFile);
            File.WriteAllText(configFileName, config);

            Console.WriteLine($"Configuration written to {configFileName}");
            Console.WriteLine();
        }

        private void Cancel()
        {
            var section = new UserInterfaceSection(
                "Cancel configuration",
                "Configuration cancelled",
                null);

            _userInterface.Do(section);
        }
    }
}

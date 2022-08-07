using System;
using System.IO;
using CypherNetworkNode.UI;

namespace CypherNetworkNode.Configuration
{
    public class Configuration
    {
        private readonly IUserInterface _userInterface;

        public Configuration(IUserInterface userInterface)
        {
            _userInterface = userInterface;
            var networkConfiguration = new Network(userInterface);
            if (!networkConfiguration.Do())
            {
                Cancel();
                return;
            }

            Console.WriteLine("--------------------------------------------------------------------");
            Console.WriteLine("Node name             : " + networkConfiguration.Configuration.NodeName);
            Console.WriteLine("Public IP address     : " + networkConfiguration.Configuration.IpAddress);
            Console.WriteLine("Public API port       : " + networkConfiguration.Configuration.ApiPortPublic);
            Console.WriteLine("Listening public port : " + networkConfiguration.Configuration.ListeningPort);
            Console.WriteLine("Advertise public port : " + networkConfiguration.Configuration.AdvertisePort);
            Console.WriteLine("--------------------------------------------------------------------");
            Console.WriteLine();
            var configTemplate = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration",
                "Templates", Program.AppSettingsFile));
            var config = configTemplate
                .Replace("<HTTP_END_POINT>", $"http://{networkConfiguration.Configuration.IpAddress}:{networkConfiguration.Configuration.ApiPortPublic.ToString()}")
                .Replace("<GOSSIP_LISTENING>", $"tcp://{networkConfiguration.Configuration.IpAddress}:{networkConfiguration.Configuration.ListeningPort.ToString()}")
                .Replace("<GOSSIP_ADVERTISE>", $"tcp://{networkConfiguration.Configuration.IpAddress}:{networkConfiguration.Configuration.AdvertisePort.ToString()}")
                .Replace("<NODE_NAME>", networkConfiguration.Configuration.NodeName);
            var configFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.AppSettingsFile);
            File.WriteAllText(configFileName, config);
            Console.WriteLine($"Configuration written to {configFileName}");
            Console.WriteLine();
        }

        private void Cancel()
        {
            var section = new UserInterfaceSection("Cancel configuration", "Configuration cancelled", null);
            _userInterface.Do(section);
        }
    }
}
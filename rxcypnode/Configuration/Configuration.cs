using System;
using System.IO;
using rxcypnode.UI;

namespace rxcypnode.Configuration
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
            var serfConfigFileName = $"serf.{networkConfiguration.Configuration.NodeName}.conf";
            var serfConfigFile = networkConfiguration.Configuration.GetSerfConfiguration();
            using var sw = new StreamWriter(serfConfigFileName, false);
            sw.WriteLine(serfConfigFile);
            sw.Close();
            Console.WriteLine($"Serf configuration written to {serfConfigFileName}");
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
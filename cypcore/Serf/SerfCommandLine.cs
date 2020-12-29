// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Serf
{
    public static class SerfCommandLine
    {
        public const string Agent = "agent"; // Runs a Serf agent
        public const string Auth = "auth"; // Used to authenticate a client
        public const string Event = "event"; // Send a custom event through the Serf cluster
        public const string ForceLeave = "force-leave"; // Forces a member of the cluster to enter the "left" state
        public const string Info = "info"; // Provides debugging information for operators
        public const string Join = "join"; // Tell Serf agent to join cluster
        public const string Keygen = "keygen"; // Generates a new encryption key
        public const string Keys = "keys"; // Manipulate the internal encryption keyring used by Serf
        public const string Leave = "leave"; // Gracefully leaves the Serf cluster and shuts down
        public const string Members = "members"; // Lists the members of a Serf cluster
        public const string Monitor = "monitor"; // Stream logs from a Serf agent
        public const string Query = "query"; // Send a query to the Serf cluster
        public const string Reachability = "reachability"; // Test network reachability
        public const string RTT = "rtt"; // Estimates network round trip time between nodes
        public const string Tags = "tags"; // Modify tags of a running Serf agent
        public const string Version = "version"; // Prints the Serf version
        public const string Handshake = "handshake"; // Used to initialize the connection, set the version
        public const string InstallKey = "install-key"; // Installs a new encryption key
        public const string UseKey = "use-key"; // Changes the primary key used for encrypting messages
        public const string RemoveKey = "remove-key"; // Removes an existing encryption key
        public const string ListKey = "list-keys"; // Provides a list of encryption keys in use in the cluster
        public const string Stats = "stats"; // Provides a debugging information about the running serf agent
        public const string GetCoordinate = "get-coordinate"; // Returns the network coordinate for a node
    }
}

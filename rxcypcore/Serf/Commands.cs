namespace rxcypcore.Serf
{
    public class Commands
    {
        public enum SerfCommand
        {
            Event,
            Join,
            Leave,
            Members,
            Handshake,
            Stream
        }

        public static string SerfCommandString(SerfCommand? command)
        {
            return command switch
            {
                SerfCommand.Event => "event",
                SerfCommand.Handshake => "handshake",
                SerfCommand.Join => "join",
                SerfCommand.Leave => "leave",
                SerfCommand.Members => "members",
                SerfCommand.Stream => "stream"
            };
        }
    }
}
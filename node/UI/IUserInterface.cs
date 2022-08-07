namespace CypherNetworkNode.UI
{
    public interface IUserInterface
    {
        public UserInterfaceChoice Do(UserInterfaceSection section);
        public bool Do<T>(IUserInterfaceInput<T> input, out T output);
        public IUserInterface SetTopic(string topic);
    }
}
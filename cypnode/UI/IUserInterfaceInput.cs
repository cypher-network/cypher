namespace CYPNode.UI
{
    public interface IUserInterfaceInput<T>
    {
        string Prompt { get; }
        bool IsValid(string value);
        bool Cast(string input, out T output);
    }
}
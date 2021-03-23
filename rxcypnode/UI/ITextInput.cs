using System;

namespace rxcypnode.UI
{
    public interface IUserInterfaceInput<T>
    {
        string Prompt { get; }
        bool IsValid(string value);
        bool Cast(string input, out T output);
    };
    
    public class TextInput<T> : IUserInterfaceInput<T>
    {
        public string Prompt { get; }
        private readonly Func<string, bool> _validation;
        private readonly Func<string, T> _cast;

        public TextInput(string prompt, Func<string, bool> validation, Func<string, T> cast)
        {
            Prompt = prompt;
            _validation = validation;
            _cast = cast;
        }

        public bool IsValid(string value)
        {
            return _validation == null || _validation.Invoke(value);
        }

        public bool Cast(string input, out T output)
        {
            output = _cast == null
                ? default
                : _cast(input);

            return _cast == null || !output.Equals(default);
        }
    }
}
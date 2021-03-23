using System.Collections.Generic;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace rxcypnode.UI
{
    public class UserInterfaceChoice
    {
        public UserInterfaceChoice(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override int GetHashCode()
        {
            return Text.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UserInterfaceChoice);
        }

        public bool Equals(UserInterfaceChoice otherChoice)
        {
            return Text == otherChoice.Text;
        }
    }

    public class UserInterfaceSection
    {
        public UserInterfaceSection(string title, string description, UserInterfaceChoice[] choices)
        {
            Title = title;
            Description = description;
            Choices = choices;
        }

        public string Title { get; }
        public string Description { get; }
        public UserInterfaceChoice[] Choices { get; }
    }
}
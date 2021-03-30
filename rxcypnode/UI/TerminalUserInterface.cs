using System;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text.RegularExpressions;
using Autofac;

namespace rxcypnode.UI
{
    public class TerminalUserInterface : IUserInterface
    {
        private const int Indent = 4;

        public override UserInterfaceChoice Do(UserInterfaceSection section)
        {
            while (true)
            {
                Console.Clear();
                PrintHeader(section.Title);
                Print(section.Description, Indent);

                Console.WriteLine();
                if (section.Choices == null)
                {
                    return null;
                }

                for (var choiceIndex = 0; choiceIndex < section.Choices.Length; ++choiceIndex)
                {
                    Print($"{(choiceIndex + 1).ToString()}: {section.Choices[choiceIndex].Text}", 4);
                }
                Print($"{(section.Choices.Length + 1).ToString()}: Cancel", 4);

                Console.WriteLine();


                Console.Write(GetIndentString(Indent));
                var choiceStr = Console.ReadLine();
                if (int.TryParse(choiceStr, out var choiceInt))
                {
                    if (choiceInt > 0 && choiceInt <= section.Choices.Length)
                    {
                        return section.Choices[choiceInt - 1];
                    }

                    return new UserInterfaceChoice(string.Empty);
                }
            }
        }

        public override bool Do<T>(IUserInterfaceInput<T> input, out T output)
        {
            output = default;
            var validInput = false;
            while (!validInput)
            {
                Console.WriteLine();
                Console.Write($"{GetIndentString(Indent)}{input.Prompt}: ");

                var inputString = Console.ReadLine();
                if (input.IsValid(inputString))
                {
                    validInput = input.Cast(inputString, out output);
                }
            }

            return true;
        }

        private void PrintHeader(string header)
        {
            Console.WriteLine($"{_topic} | {header}");
            Console.WriteLine();
        }

        private static void Print(string text, int indent = 0)
        {
            var pattern = $@"(?<line>.{{1,{(Console.WindowWidth - indent).ToString()}}})(?<!\s)(\s+|$)|(?<line>.+?)(\s+|$)";
            var lines = Regex.Matches(text, pattern).Select(m => m.Groups["line"].Value);
            foreach (var line in lines)
            {
                Console.Out.WriteLine($"{GetIndentString(Indent)}{line}");
            }
        }

        private static string GetIndentString(int indent)
        {
            var indentString = string.Empty;
            for (var indentIndex = 0; indentIndex < indent; indentIndex++)
            {
                indentString += ' ';
            }

            return indentString;
        }
    }
}
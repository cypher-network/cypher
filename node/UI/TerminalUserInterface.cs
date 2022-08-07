using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CypherNetworkNode.UI
{
    public class TerminalUserInterface : UserInterfaceBase
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
            var lineWidth = Console.WindowWidth - indent;
            foreach (var line in text.Split(Environment.NewLine))
            {
                var pattern = $@"(?<line>.{{1,{(Console.WindowWidth - indent).ToString()}}})(?<!\s)(\s+|$)|(?<line>.+?)(\s+|$)";
                var lines = Regex.Matches(line, pattern).Select(m => m.Groups["line"].Value);
                if (lines.Any())
                {
                    foreach (var indentLine in lines)
                    {
                        Console.WriteLine($"{GetIndentString(Indent)}{indentLine}");

                    }
                }
                else
                {
                    Console.WriteLine();
                }
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
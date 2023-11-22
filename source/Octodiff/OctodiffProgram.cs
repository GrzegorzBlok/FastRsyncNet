using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Octodiff.CommandLine;
using Octodiff.CommandLine.Support;

namespace Octodiff
{
    public class OctodiffProgram
    {
        static int Main(string[] args)
        {
            string[] commandArguments;
            var commandName = ExtractCommand(args, out commandArguments);
            var locator = new CommandLocator();
            var command = locator.Find(commandName);
            
            if (command == null)
            {
                locator.Create(locator.Find("help")).Execute(commandArguments);
                return 4;
            }

            try
            {
                var exitCode = locator.Create(command).Execute(commandArguments);
                return exitCode;
            }
            catch (OptionException ex)
            {
                WriteError(ex);
                locator.Create(locator.Find("help")).Execute(new[] {commandName});
                return 4;
            }
            catch (ArgumentException ex)
            {
                WriteError(ex);
                return 4;
            }
            catch (FileNotFoundException ex)
            {
                WriteError(ex);
                return 4;
            }
            catch (InvalidDataException ex)
            {
                WriteError(ex);
                return 2;
            }
            catch (IOException ex)
            {
                WriteError(ex, details: true);
                return 1;
            }
            catch (Exception ex)
            {
                WriteError(ex, details: true);
                return 3;
            }
        }

        static void WriteError(Exception ex, bool details = false)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Error: " + ex.Message);
            Console.ResetColor();
            if (details)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static string ExtractCommand(ICollection<string> args, out string[] remaining)
        {
            remaining = args.Count <= 1 ? new string[0] : args.Skip(1).ToArray();
            return (args.FirstOrDefault() ?? string.Empty).ToLowerInvariant();
        }
    }
}

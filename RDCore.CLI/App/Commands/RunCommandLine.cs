using CommandLine;
using System.Reflection;

namespace RDCore.CLI.App.Commands;

// rdc run's command line, split between rdc and the program: rdc's options come first, then the path, and everything after the path is the program's command
// line. A -- ends rdc's part wherever it is: right after the path, it is skipped; with no path, the program's command line follows it, and the current folder
// runs. Without a path or a --, nothing is the program's: rdc run --seed 42 is rdc's, and an error of its parser.
internal static class RunCommandLine
{
    private const string Separator = "--";

    // rdc run's options that take a value, as they are written on the command line: the token after one is the value, and not the path.
    private static readonly HashSet<string> OptionsWithAValue = typeof(RunCommandOptions).GetProperties()
        .Where(property => property.PropertyType != typeof(bool))
        .Select(property => property.GetCustomAttribute<OptionAttribute>())
        .OfType<OptionAttribute>()
        .SelectMany(option => new[] { string.IsNullOrEmpty(option.ShortName) ? null : $"-{option.ShortName}", string.IsNullOrEmpty(option.LongName) ? null : $"--{option.LongName}" })
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    public static (IReadOnlyList<string> Own, IReadOnlyList<string> Program) Split(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg == Separator)
            {
                return ([.. args.Take(index)], [.. args.Skip(index + 1)]);
            }

            if (arg.Length > 1 && arg[0] == '-')
            {
                if (OptionsWithAValue.Contains(arg))
                {
                    index++;
                }

                continue;
            }

            var program = args.Skip(index + 1).ToList();
            if (program.Count > 0 && program[0] == Separator)
            {
                program.RemoveAt(0);
            }

            return ([.. args.Take(index + 1)], program);
        }

        return (args, []);
    }
}

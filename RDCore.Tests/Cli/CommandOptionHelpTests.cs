using System.Globalization;
using System.Reflection;
using CommandLine;
using CommandLine.Text;
using RDCore.CLI;
using RDCore.CLI.App.Commands;

namespace RDCore.Tests.Cli;

/// <summary>
/// What <c>rdc compose --help</c>, <c>rdc build --help</c> and <c>rdc run --help</c> say of each option is a string of rdc's resources, in English and in
/// French: none is written in the code.
/// </summary>
[TestClass]
public sealed class CommandOptionHelpTests
{
    // each command, with its options and the help CommandLineParser writes for its --help.
    private static readonly Dictionary<string, (Type Options, Func<string> Help)> Helped = new()
    {
        ["compose"] = (typeof(ComposeCommandOptions), Help<ComposeCommandOptions>),
        ["build"] = (typeof(BuildCommandOptions), Help<BuildCommandOptions>),
        ["run"] = (typeof(RunCommandOptions), Help<RunCommandOptions>),
    };

    public static IEnumerable<object[]> Commands => Helped.Keys.Select(command => new object[] { command });

    [TestMethod]
    [DynamicData(nameof(Commands))]
    public void TheHelpOfEachOption_IsAResource_InEnglishAndInFrench(string command)
    {
        var (options, help) = Helped[command];
        // the attribute as written: its HelpText property reads the resource it names.
        var described = options.GetProperties()
            .SelectMany(property => property.GetCustomAttributesData()
                .Where(attribute => typeof(BaseAttribute).IsAssignableFrom(attribute.AttributeType))
                .Select(attribute => (property.Name, Arguments: attribute.NamedArguments.ToDictionary(argument => argument.MemberName,
                    argument => argument.TypedValue.Value))))
            .ToArray();
        Assert.IsNotEmpty(described, command);

        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("fr") })
        {
            var shown = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = culture;
            try
            {
                var strings = Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
                Assert.IsNotNull(strings, culture.Name);
                var written = help();

                foreach (var (name, arguments) in described)
                {
                    Assert.AreEqual(typeof(Resources), arguments.GetValueOrDefault(nameof(BaseAttribute.ResourceType)),
                        $"{command} {name}: its help is written in the code");
                    var resource = (string)arguments[nameof(BaseAttribute.HelpText)]!;
                    var wording = strings.GetString(resource);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(wording), $"{command} {name} ({culture.Name}): {resource} is not in these resources");
                    StringAssert.Contains(written, wording, $"{command} {name} ({culture.Name})");
                }
            }
            finally
            {
                CultureInfo.CurrentUICulture = shown;
            }
        }
    }

    // on lines wide enough that no option's help is wrapped.
    private static string Help<TOptions>()
    {
        using var parser = new CommandLine.Parser(settings => settings.HelpWriter = null);
        var parsed = parser.ParseArguments<TOptions>(["--help"]);
        return HelpText.AutoBuild(parsed, help => help, example => example, verbsIndex: false, maxDisplayWidth: 10_000).ToString();
    }
}

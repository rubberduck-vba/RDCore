using Microsoft.Extensions.DependencyInjection;
using RDCore.Parsing;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Parser;

/// <summary>
/// The module parser the parse server is given, as its container makes it: OmniSharp's, which looks at every constructor of a service, the internal ones too.
/// </summary>
[TestClass]
public sealed class ModuleParserServiceTests
{
    // the module rdc.exe's shell runs a program as.
    private const string Program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Debug.Print MsgBox(\"Continue?\", vbYesNo)\r\nEnd Sub\r\n";

    [TestMethod]
    public void TheModuleParserOfTheParseServer_ReadsAModule_AsOneMadeInProcessDoes()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var server = OmniSharpLanguageServer.Create(options => options
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            // as the parse server registers it (RDCoreParserApp.ConfigureServices).
            .WithServices(services => services.AddSingleton<IModuleParser, ModuleParser>()));

        var result = server.GetRequiredService<IModuleParser>().Parse(TestUri.TestModuleUri(), Program);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
    }

    // a ceiling no parse reads to is refused when the parser is made, and not found out when every module it reads is too deep.
    [TestMethod]
    [DataRow(0, DisplayName = "none")]
    [DataRow(-1, DisplayName = "below none")]
    [DataRow(32_001, DisplayName = "past the deepest the parse reads")]
    public void AModuleParser_RefusesACeilingNoParseReadsTo(int ceiling)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ModuleParser { RuleDepthCeiling = ceiling });
}

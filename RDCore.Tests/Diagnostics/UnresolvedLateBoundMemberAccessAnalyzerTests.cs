using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11010</c>: a late-bound member access that no class the object can be of answers (MS-VBAL §5.6.12), which raises run-time error 438 whenever it
/// runs: reported at Warning severity, from the members the host states the access may reach.
/// </summary>
[TestClass]
public sealed class UnresolvedLateBoundMemberAccessAnalyzerTests
{
    private static readonly (string, string) Holder = ("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit",
        "Public Tag As Long",
        "Private Secret As Long"));

    private static readonly (string, string) Other = ("Other", ModuleWorkspace.ClassModule("Other", "Option Explicit",
        "Public Nope As Long"));

    private static readonly (string, string) Shape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Option Explicit",
        "Public Function Area() As Double", "End Function"));

    private static readonly (string, string) Square = ("Square", ModuleWorkspace.ClassModule("Square", "Option Explicit", "Implements IShape",
        "Private Function IShape_Area() As Double", "End Function"));

    private static readonly (string, string) Open = ("Open", ModuleWorkspace.ClassModule("Open", "Attribute VB_Extensible = True", "Option Explicit",
        "Public Tag As Long"));

    // the body's lines, separated by '|', follow the three lines that open the module and its procedure
    private const int FirstBodyLine = 3;

    private static async Task<AnalyzerFinding[]> FindingsAsync(string parameters, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n"
            + $"{string.Join("\r\n", body.Split('|'))}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([Holder, Other, Shape, Square, Open], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new UnresolvedLateBoundMemberAccessAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Dim x As Variant|Set o = New Holder|x = o.Nope", "Nope", DisplayName = "a member read")]
    [DataRow("Set o = New Holder|o.Nope 1", "Nope", DisplayName = "a call statement")]
    [DataRow("Set o = New Holder|o.Nope = 1", "Nope", DisplayName = "the target of an assignment")]
    [DataRow("Dim x As Variant|Set o = New Holder|x = o.Secret", "Secret", DisplayName = "a Private member")]
    [DataRow("Dim x As Variant|Set o = New Square|x = o.Area", "Area", DisplayName = "a member of the interface the class implements")]
    [DataRow("Dim x As Variant|If x Then Set o = New Holder Else Set o = New Square|x = o.Tally", "Tally", DisplayName = "two classes, neither with the member")]
    public async Task AMemberThatNoClassTheObjectCanBeOfHas_IsError438(string body, string member)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(string.Empty, $"Dim o As Object|{body}"));

        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{member}'");
        StringAssert.Contains(finding.Message, "438");
        var lines = $"Dim o As Object|{body}".Split('|');
        Assert.AreEqual(FirstBodyLine + Array.FindIndex(lines, line => line.Contains($".{member}", StringComparison.Ordinal)), finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("", "Dim o As Object|Dim x As Variant|Set o = New Holder|x = o.Tag", DisplayName = "a member the class has")]
    [DataRow("", "Dim o As Object|Dim x As Variant|If x Then Set o = New Holder Else Set o = New Other|x = o.Nope",
        DisplayName = "a member one of the classes has")]
    [DataRow("ByVal o As Object", "Dim x As Variant|x = o.Nope", DisplayName = "an object given as an argument can be of any class")]
    [DataRow("", "Dim o As Object|Dim x As Variant|Set o = Nothing|x = o.Nope", DisplayName = "Nothing, which raises error 91")]
    [DataRow("", "Dim x As Variant|x = Err.Nope", DisplayName = "a member the host already states the error 438 of")]
    public async Task AnAccessThatCanSucceedOrIsAlreadyReported_IsNotReported(string parameters, string body)
        => Assert.IsEmpty(await FindingsAsync(parameters, body));

    // the standard library's classes are extensible, and the platform's: an object of one has the members its class declares and no other.
    [TestMethod]
    [DataRow("", "Dim c As Collection|Dim x As Variant|Set c = New Collection|x = c.Nope", DisplayName = "a variable of the class")]
    [DataRow("", "Dim c As New Collection|c.Nope 1", DisplayName = "an auto-instantiated variable, in a call statement")]
    [DataRow("", "Dim c As New Collection|Dim x As Variant|With c|x = .Nope|End With", DisplayName = "the object of a With block")]
    [DataRow("ByVal c As Collection", "Dim x As Variant|x = c.Nope", DisplayName = "a parameter, which can only be of the class")]
    public async Task AMemberThatAStandardLibraryClassDoesNotDeclare_IsError438(string parameters, string body)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(parameters, body));

        StringAssert.Contains(finding.Message, "'Nope'");
        StringAssert.Contains(finding.Message, "438");
    }

    [TestMethod]
    [DataRow("Dim c As New Collection|Dim x As Variant|x = c.Count", DisplayName = "a member a class of the VBA library declares")]
    [DataRow("Dim b As New Open|Dim x As Variant|x = b.Nope", DisplayName = "a member an extensible class module does not declare")]
    public async Task AMemberThatAnExtensibleClassDeclaresOrCanHave_IsNotReported(string body)
        => Assert.IsEmpty(await FindingsAsync(string.Empty, body));
}

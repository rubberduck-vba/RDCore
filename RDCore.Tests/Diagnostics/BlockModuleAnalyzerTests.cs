using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The analyzers of RDCore.Diagnostics, each given the module of a block (<strong>RD-VBAL §5.1</strong>) as the language server sends it: addressed under its
/// file, with its own part of the file's parse, and what the host says of it. A block's module is of its header's kind whatever its file is, and its code
/// starts on the line after its header.
/// </summary>
[TestClass]
public sealed class BlockModuleAnalyzerTests
{
    private static readonly Uri File = new(Path.Combine(ModuleWorkspace.Root, "Shapes.cls"));

    // a standard module, as the host describes one: no class, no document.
    private static ModuleSemanticsDto StandardModule(Uri uri) => new(uri, true, [], [], []);

    private static ModuleAnalysisContext ContextOf(string source, string block, Func<Uri, ModuleSemanticsDto>? semantics = null)
    {
        // a .bas has blocks in a workspace that allows statements outside procedures.
        var parse = FileLevelStatementsOn.Parser().Parse(File, source);
        Assert.IsEmpty(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var uri = RDCoreDocument.BlockUri(File, block);
        return new ModuleAnalysisContext(uri, new RDCoreDocument(File, parse).ParseOf(uri)!, (semantics ?? StandardModule)(uri));
    }

    [TestMethod]
    public void AnEmptyBlock_IsReportedOnItsFirstLine_ByTheNameItsHeaderWrites()
    {
        var context = ContextOf("Public Sub Main()\r\nEnd Sub\r\nModule Helpers\r\nOption Explicit\r\n' notes\r\nEnd Module\r\n", "Helpers");

        var finding = Assert.ContainsSingle(new EmptyModuleAnalyzer().Analyze(context));

        Assert.AreEqual(new SourceRange(new SourcePosition(3, 0), new SourcePosition(4, 0)), finding.Range);
        StringAssert.Contains(finding.Message, "'Helpers'");
    }

    // the option goes on the block's first line, which is where a reader of the block looks for it; in a block that has no code, too.
    [TestMethod]
    [DataRow("Public Sub Run()\r\nEnd Sub\r\n", DisplayName = "a block with a procedure")]
    [DataRow("' notes\r\n", DisplayName = "a block with a comment only")]
    public void ABlockWithoutOptionExplicit_IsReportedOnItsFirstLine_AndTheFixPutsTheOptionThere(string body)
    {
        var source = $"Option Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\nModule Helpers\r\n{body}End Module\r\n";
        var context = ContextOf(source, "Helpers", uri => StandardModule(uri) with { OptionExplicit = false });

        var finding = Assert.ContainsSingle(new OptionExplicitAnalyzer().Analyze(context));
        var made = finding.Fixes!.Edits.Single().Edits.ApplyTo(source);

        Assert.AreEqual(4, finding.Range.Start.Line);
        Assert.AreEqual($"Option Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\nModule Helpers\r\nOption Explicit\r\n{body}End Module\r\n", made);
    }

    // a public variable of a standard module may be made a Long: no object of a class has it as a member. A Module block of a .cls file is a standard module.
    [TestMethod]
    [DataRow(false, true, DisplayName = "a Module block, which the host says is no class")]
    [DataRow(true, false, DisplayName = "a module the host says is a class")]
    public void APublicIntegerOfABlock_IsMadeALong_OnlyInAStandardModule(bool isClass, bool fixes)
    {
        const string source = "Public Sub Main()\r\nEnd Sub\r\nModule Helpers\r\nPublic Count As Integer\r\nEnd Module\r\n";
        var context = ContextOf(source, "Helpers", uri => StandardModule(uri) with
        {
            Class = isClass ? ClassModuleSemanticFlags.None : null,
            Declarations = [new DeclarationFactDto(new Uri(uri, "#Count"), "Count", DeclarationKind.Variable, AccessModifier.Public, false,
                new SourceLocation(File, new SourceRange(new SourcePosition(3, 7), new SourcePosition(3, 12))), new DeclarationReferences(0, 0, 0))],
        });

        var finding = Assert.ContainsSingle(new IntegerDataTypeAnalyzer().Analyze(context));

        Assert.AreEqual(fixes, finding.Fixes is { Edits: [_, ..] });
    }

    // a reference is located in the file of its module, and the file's own module is another module than its blocks': a member it calls is not made Private.
    [TestMethod]
    [DataRow(7, 4, 7, 15, true, DisplayName = "called by a procedure of its block")]
    [DataRow(2, 6, 2, 17, false, DisplayName = "called by the file's own module")]
    public void AnImplementationInAClassBlock_IsMadePrivate_OnlyWhenNothingOutsideItsBlockCallsIt(
        int line, int character, int endLine, int endCharacter, bool fixes)
    {
        var source = "Public Sub Main()\r\n    Dim s As New Implementer\r\n    s.IShape_Draw\r\nEnd Sub\r\n"
            + "Class Implementer\r\nImplements IShape\r\nPublic Sub Other()\r\n    IShape_Draw\r\nEnd Sub\r\n"
            + "Public Sub IShape_Draw()\r\nEnd Sub\r\nEnd Class\r\n";
        var written = new SourceRange(new SourcePosition(line, character), new SourcePosition(endLine, endCharacter));
        var site = new DeclarationReference(new SourceLocation(File, written), Procedure: null, DeclarationReferenceKind.Read);
        var context = ContextOf(source, "Implementer", uri => StandardModule(uri) with
        {
            Class = ClassModuleSemanticFlags.None,
            Declarations = [new DeclarationFactDto(new Uri(uri, "#IShape_Draw"), "IShape_Draw", DeclarationKind.Procedure, AccessModifier.Public, false,
                new SourceLocation(File, new SourceRange(new SourcePosition(9, 11), new SourcePosition(9, 22))), new DeclarationReferences(1, 0, 0),
                DeclarationRole.InterfaceImplementation, ReferencedFrom: [site])],
        });

        var finding = Assert.ContainsSingle(new ImplementationsShouldBePrivateAnalyzer().Analyze(context));

        Assert.AreEqual(fixes, finding.Fixes is { Edits: [_, ..] });
        if (fixes)
        {
            StringAssert.Contains(finding.Fixes!.Edits.Single().Edits.ApplyTo(source), "\r\nPrivate Sub IShape_Draw()\r\n");
        }
    }
}

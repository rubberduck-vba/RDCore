using RDCore.LanguageServer.Services.Refactoring;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Diagnostics;
using RDCore.Tests.Platform;
using System.IO.Abstractions;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The code of a Visual Basic 6.0 UserControl and PropertyPage. The VB library's <c>VB.UserControl</c> and <c>VB.PropertyPage</c> declare no <c>Left</c>,
/// <c>Top</c> or designer setting (<c>CanGetFocus</c>, <c>InvisibleAtRuntime</c> and the rest), so a bare <c>Left</c> is VBA's function. The base class's
/// members are names in scope in the root's code, unqualified or through the base class's name (<c>UserControl.BackColor</c>), and no members of the root's
/// class: Me has only the class's own. A declaration of the root's code shadows the base member of its name. The base class's name is no value.
/// </summary>
[TestClass]
public sealed class VisualBasicUserControlLanguageTests
{
    private const string UserControlRoot = "VB.UserControl";
    private const string PropertyPageRoot = "VB.PropertyPage";

    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    private static ModuleWorkspace.WorkspaceLibraries Libraries => new([], new DirectoryLibrarySource(new FileSystem(), Symbols));

    private static string NameOf(string root) => root == UserControlRoot ? "Ctl" : "Page1";

    // Ctl, a UserControl, or Page1, a PropertyPage: its declarations, a Public property of its own (Answer), its other members, and the body in a procedure.
    private static string Declared(string root, string[] declarations, string[] members, params string[] body)
        => "VERSION 5.00\r\n" +
           $"Begin {root} {NameOf(root)} \r\n" +
           "   ClientHeight    =   500\r\n" +
           "   ClientLeft      =   0\r\n" +
           "   ClientTop       =   0\r\n" +
           "   ClientWidth     =   1500\r\n" +
           "   ScaleHeight     =   500\r\n" +
           "   ScaleWidth      =   1500\r\n" +
           "End\r\n" +
           $"Attribute VB_Name = \"{NameOf(root)}\"\r\n" +
           "Attribute VB_GlobalNameSpace = False\r\n" +
           "Attribute VB_Creatable = True\r\n" +
           "Attribute VB_PredeclaredId = False\r\n" +
           "Attribute VB_Exposed = False\r\n" +
           "Option Explicit\r\n" +
           string.Concat(declarations.Select(line => line + "\r\n")) +
           "Public Property Get Answer() As Long\r\n" +
           "    Answer = 42\r\n" +
           "End Property\r\n" +
           string.Concat(members.Select(line => line + "\r\n")) +
           "Public Sub Probe()\r\n" +
           "    Dim s As String, o As Object\r\n" +
           string.Join("\r\n", body.Select(line => "    " + line)) + "\r\n" +
           "End Sub\r\n";

    private static string Root(string root, params string[] body) => Declared(root, [], [], body);

    private static async Task<SemanticsPayload> SemanticsAsync(string root, string source, string program = "")
        => await ModuleWorkspace.SemanticsAsync(
            [], $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{program}\r\n", language: SupportedLanguages.VB6, libraries: Libraries,
            forms: [(NameOf(root), source, FormDesigner.Read(source)!)]);

    private static async Task<IReadOnlyList<string>> ErrorsOfAsync(string root, string source, string program = "")
    {
        var payload = await SemanticsAsync(root, source, program);
        return [.. payload.Modules.SelectMany(module => module.DeclarationErrors.Concat(module.Procedures.SelectMany(procedure => procedure.CompileErrors)))
            .Select(error => $"{error.Id} {error.Location.Range.Start.Line}: {error.Verbose}")];
    }

    private static Task<IReadOnlyList<string>> ErrorsAsync(string root, string line, string program = "") => ErrorsOfAsync(root, Root(root, line), program);

    private static async Task AssertErrorAsync(string expected, string root, string line)
    {
        var errors = await ErrorsAsync(root, line);
        StringAssert.StartsWith(Assert.ContainsSingle(errors, string.Join(" | ", errors)), expected, string.Join(" | ", errors));
    }

    private static async Task AssertNoErrorAsync(string root, string line)
    {
        var errors = await ErrorsAsync(root, line);
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    [DataRow(UserControlRoot, DisplayName = "a UserControl")]
    [DataRow(PropertyPageRoot, DisplayName = "a PropertyPage")]
    public async Task LeftWithItsArguments_IsVBAsLeft(string root)
        => await AssertNoErrorAsync(root, "s = Left(\"ab\", 1)");

    [TestMethod]
    [Ignore("a function named with no argument list is not checked for its required parameters: `s = Left` is no error in any module")]
    [DataRow(UserControlRoot, DisplayName = "a UserControl")]
    [DataRow(PropertyPageRoot, DisplayName = "a PropertyPage")]
    public async Task ABareLeft_IsVBAsLeft_WithoutItsArguments(string root)
        => await AssertErrorAsync(nameof(VBCompileErrorId.ArgumentNotOptional), root, "Debug.Print Left");

    [TestMethod]
    [DataRow(UserControlRoot, DisplayName = "a UserControl")]
    [DataRow(PropertyPageRoot, DisplayName = "a PropertyPage")]
    public async Task ABareTop_IsNoName(string root)
        => await AssertErrorAsync(nameof(VBCompileErrorId.VariableNotDefined), root, "s = Top");

    [TestMethod]
    [DataRow("s = Me.BackColor", DisplayName = "Me.BackColor")]
    [DataRow("Me.BackColor = &HFF&", DisplayName = "Me.BackColor =")]
    [DataRow("s = Me.hWnd", DisplayName = "Me.hWnd")]
    [DataRow("s = Me.CanGetFocus", DisplayName = "Me.CanGetFocus")]
    [DataRow("s = Me.Left", DisplayName = "Me.Left")]
    [DataRow("s = Me.Extender.Name", DisplayName = "Me.Extender")]
    public async Task MeInAUserControl_IsTheClassesOwnInterface(string line)
        => await AssertErrorAsync(nameof(VBCompileErrorId.MethodOrDataMemberNotFound), UserControlRoot, line);

    [TestMethod]
    [DataRow("Me.Changed = True", DisplayName = "Me.Changed =")]
    [DataRow("s = Me.Caption", DisplayName = "Me.Caption")]
    public async Task MeInAPropertyPage_IsTheClassesOwnInterface(string line)
        => await AssertErrorAsync(nameof(VBCompileErrorId.MethodOrDataMemberNotFound), PropertyPageRoot, line);

    [TestMethod]
    [DataRow(UserControlRoot, "s = BackColor", DisplayName = "BackColor, unqualified")]
    [DataRow(UserControlRoot, "BackColor = &HFF&", DisplayName = "BackColor =, unqualified")]
    [DataRow(UserControlRoot, "s = hWnd", DisplayName = "hWnd, unqualified")]
    [DataRow(UserControlRoot, "s = Extender.Name", DisplayName = "Extender, unqualified")]
    [DataRow(UserControlRoot, "s = Me.Answer", DisplayName = "Me.Answer")]
    [DataRow(PropertyPageRoot, "Changed = True", DisplayName = "Changed =, unqualified")]
    [DataRow(PropertyPageRoot, "s = Caption", DisplayName = "Caption, unqualified")]
    public async Task WhatTheBaseClassHas_IsInScope(string root, string line)
        => await AssertNoErrorAsync(root, line);

    [TestMethod]
    public async Task ASettingUnqualified_IsNoName()
        => await AssertErrorAsync(nameof(VBCompileErrorId.VariableNotDefined), UserControlRoot, "s = CanGetFocus");

    [TestMethod]
    [DataRow(new[] { "Private BackColor As Long" }, new string[0], "BackColor = 5", DisplayName = "Private BackColor As Long")]
    [DataRow(new string[0], new[] { "Public Sub Refresh()", "End Sub" }, "Refresh", DisplayName = "Public Sub Refresh()")]
    [DataRow(new string[0], new[] { "Private Function hWnd() As Long", "    hWnd = 7", "End Function" }, "s = hWnd", DisplayName = "Private Function hWnd()")]
    public async Task ADeclarationNamedLikeABaseMember_ShadowsIt(string[] declarations, string[] members, string line)
    {
        var source = Declared(UserControlRoot, declarations, members, line);
        var errors = await ErrorsOfAsync(UserControlRoot, source);
        Assert.IsEmpty(errors, string.Join(" | ", errors));

        var context = Composed(source);
        var name = line.Split(' ').First(word => word is "BackColor" or "Refresh" or "hWnd");
        var target = SymbolNavigation.DefinitionsAt(context, ModuleUri, At(source, line, name));
        Assert.IsNotNull(target);
        Assert.IsNotEmpty(target.Sites, "the root's own declaration, which is the name's");
    }

    [TestMethod]
    public async Task ABaseMemberUnused_IsNoFindingOfAnyAnalyzer()
    {
        var source = Root(UserControlRoot, "s = \"x\"");
        var payload = await SemanticsAsync(UserControlRoot, source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Ctl");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Ctl.ctl"));
        var parse = new ModuleParser().Parse(uri, source) with { Designer = FormDesigner.Read(source) };

        var findings = CodeActionFixture.FindingsOf(new ModuleAnalysisContext(uri, parse, model));

        var baseMembers = new[] { "BackColor", "hWnd", "Extender", "Refresh", "Ambient", "PropertyChanged" };
        // a declaration the designer makes has no place in the source: a finding about one is at none
        var about = findings.Where(finding => finding.Range == SourceRange.Empty
            || baseMembers.Any(member => finding.Message.Contains(member, StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.IsEmpty(about, string.Join(" | ", about.Select(finding => $"{finding.Code}: {finding.Message}")));
    }

    // ---- the editor ----

    private static readonly Uri RootUri = Wire.FileUri(ModuleWorkspace.Root);

    private static Uri ModuleUri => new UriBuilder(RootUri) { Fragment = "Ctl" }.Uri;

    private static RenameContext Composed(string source)
    {
        var parse = new ModuleParser().Parse(ModuleUri, source) with { Designer = FormDesigner.Read(source) };
        var libraries = new LibrarySymbolProvider(RootUri, LibrarySymbolProvider.Load(Libraries.Source, [], SupportedLanguages.VB6)).ProvideSymbols().ToList();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(RootUri, [(ModuleUri, ModuleType.ClassModule, parse)], new IntrinsicSymbolResolver(), libraries: libraries);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(ModuleUri, parse.SyntaxTree!, source)]);
    }

    // the position of the first `text` on the first line that has `line`
    private static SourcePosition At(string source, string line, string text)
    {
        var lines = source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        return new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    [DataRow("s = BackColor", "BackColor", DisplayName = "BackColor")]
    [DataRow("s = hWnd", "hWnd", DisplayName = "hWnd")]
    public void ABaseMembersHover_IsTheVBUserControls(string line, string name)
    {
        var source = Root(UserControlRoot, line);
        var target = SymbolNavigation.DefinitionsAt(Composed(source), ModuleUri, At(source, line, name));
        Assert.IsNotNull(target);

        var hover = SymbolHover.Of(target.Symbol);

        Assert.AreEqual("VB.UserControl", hover.Container, hover.ToPlainText(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsFalse(hover.Declaration.StartsWith("Private", StringComparison.Ordinal), hover.Declaration);
    }

    [TestMethod]
    public void ABaseMember_HasNoDefinitionInTheWorkspace()
    {
        var source = Root(UserControlRoot, "s = BackColor");
        var target = SymbolNavigation.DefinitionsAt(Composed(source), ModuleUri, At(source, "s = BackColor", "BackColor"));

        Assert.IsEmpty(target?.Sites ?? [], "a member of the VB library, as VB.UserControl's is");
    }

    [TestMethod]
    public void ABaseMember_IsNotRenamed()
    {
        var source = Root(UserControlRoot, "s = BackColor");

        var preparation = SymbolRename.Prepare(Composed(source), ModuleUri, At(source, "s = BackColor", "BackColor"));

        Assert.IsNull(preparation.Target);
        Assert.AreEqual(RenameRefusalKind.NotDeclaredByWorkspace, preparation.Refusal?.Kind);
    }
}

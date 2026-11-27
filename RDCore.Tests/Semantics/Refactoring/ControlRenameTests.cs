using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A control is a field of its form's class that its form's designer declares: renamed, it is renamed where its designer block names it, where the code
/// refers to it, and in the procedures bound to its events (MS-VBAL §5.3.1.8).
/// </summary>
[TestClass]
public sealed class ControlRenameTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // Ok; the control array Choice; Text1, whose name Label1's DataSource holds.
    private static string Picker(params string[] code) => Lines([
        "VERSION 5.00",
        "Begin VB.Form Picker ",
        "   Caption         =   \"Pick\"",
        "   Begin VB.CommandButton Ok ",
        "      Caption         =   \"OK\"",
        "   End",
        "   Begin VB.CommandButton Choice ",
        "      Index           =   0",
        "   End",
        "   Begin VB.CommandButton Choice ",
        "      Index           =   1",
        "   End",
        "   Begin VB.TextBox Text1 ",
        "      Text            =   \"\"",
        "   End",
        "   Begin VB.Label Label1 ",
        "      DataSource      =   \"Text1\"",
        "   End",
        "End",
        "Attribute VB_Name = \"Picker\"",
        "Option Explicit",
        .. code]);

    private static readonly string Code = Picker(
        "Private Sub Ok_Click()",
        "    Ok.Caption = \"Done\"",
        "    Choice(1).Caption = Text1.Text",
        "End Sub",
        "Private Sub Choice_Click(Index As Integer)",
        "    Choice(Index).Caption = \"x\"",
        "End Sub");

    private static string Dialog(params string[] code) => Lines([
        "VERSION 5.00",
        "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Dialog ",
        "   Caption         =   \"Dialog\"",
        "   OleObjectBlob   =   \"Dialog.frx\":0000",
        "End",
        "Attribute VB_Name = \"Dialog\"",
        "Option Explicit",
        .. code]);

    private static readonly FormDescriptor DialogDesigner = new(
        "Dialog", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Dialog" },
        [new FormControlDescriptor("Ok", new FormControlClass("MSForms", "CommandButton"), TabIndex: 0, Index: null, [])]);

    private static readonly string DialogCode = Dialog("Private Sub Ok_Click()", "    Ok.Caption = \"Done\"", "End Sub");

    // the form, as a Visual Basic 6.0 project composes it (VBRUN, VB), or a VBA project that references Microsoft Forms.
    private static RenameContext Compose(string name, string source, FormDescriptor? designer = null)
    {
        var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.frm"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        parse = parse with { Designer = designer ?? FormDesigner.Read(source) };
        var libraries = designer is null
            ? new LibrarySymbolProvider(Root, LibrarySymbolProvider.Load(ShippedLibrariesTests.Platform, [], SupportedLanguages.VB6)).ProvideSymbols().ToList()
            : new LibrarySymbolProvider(Root, ReferencedLibraries.Load(ShippedLibrariesTests.Platform, ["MSForms"], ["VBA"])).ProvideSymbols().ToList();
        var uri = ModuleUri(name);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [(uri, ModuleType.ClassModule, parse)], new IntrinsicSymbolResolver(), libraries: libraries);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(uri, parse.SyntaxTree!, source, parse.Designer)]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // the position of the first `text` on the first line that has `line`.
    private static SourcePosition At(string source, string line, string text)
    {
        var lines = source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        return new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal) + 1);
    }

    private static RenamePreparation Prepare(RenameContext context, string name, string line, string text)
        => SymbolRename.Prepare(context, ModuleUri(name), At(context.Modules.Single().Source, line, text));

    // the text of the form once the rename's edits are made.
    private static string Renamed(RenameContext context, string name, string line, string text, string newName)
    {
        var preparation = Prepare(context, name, line, text);
        Assert.IsNotNull(preparation.Target, preparation.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        var plan = SymbolRename.Plan(context, preparation.Target, newName);
        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));

        var lines = context.Modules.Single().Source.Split("\r\n");
        foreach (var edit in plan.Edits.OrderByDescending(edit => edit.Range.Start))
        {
            Assert.AreEqual(edit.Range.Start.Line, edit.Range.End.Line, "an edit of a name is on one line");
            var at = lines[edit.Range.Start.Line];
            lines[edit.Range.Start.Line] = at[..edit.Range.Start.Character] + edit.NewText + at[edit.Range.End.Character..];
        }

        return string.Join("\r\n", lines);
    }

    [TestMethod]
    [DataRow("    Ok.Caption", "Ok", DisplayName = "where the code names it")]
    [DataRow("Private Sub Ok_Click", "Ok", DisplayName = "in the part of a handler's name that is its")]
    public void AVisualBasic6Control_IsRenamedOnItsBeginLine_WhereTheCodeNamesIt_AndInItsHandlers(string line, string text)
    {
        var context = Compose("Picker", Code);

        Assert.AreEqual(
            Code.Replace("CommandButton Ok ", "CommandButton Accept ").Replace("Ok_Click", "Accept_Click").Replace("Ok.Caption", "Accept.Caption"),
            Renamed(context, "Picker", line, text, "Accept"));
    }

    // a bare graphics statement of a Visual Basic 6.0 form draws on the form, so the names its points hold are references the static pass binds.
    [TestMethod]
    public void AVisualBasic6Control_IsRenamedInTheBareGraphicsStatementsOfItsForm()
    {
        var code = Picker("Private Sub Ok_Click()", "    PSet (Ok.Left, Ok.Top), vbRed", "End Sub");
        var context = Compose("Picker", code);

        Assert.AreEqual(
            code.Replace("CommandButton Ok ", "CommandButton Accept ").Replace("Ok_Click", "Accept_Click")
                .Replace("(Ok.Left, Ok.Top)", "(Accept.Left, Accept.Top)"),
            Renamed(context, "Picker", "Private Sub Ok_Click", "Ok", "Accept"));
    }

    [TestMethod]
    public void AVisualBasic6ControlArray_IsRenamedOnTheBeginLineOfEachElement()
    {
        var context = Compose("Picker", Code);

        Assert.AreEqual(Code.Replace("Choice", "Answer"), Renamed(context, "Picker", "    Choice(Index)", "Choice", "Answer"));
    }

    [TestMethod]
    public void AVisualBasic6Control_IsNotRenamedToTheNameOfAnotherControlOfItsForm()
    {
        var context = Compose("Picker", Code);
        var preparation = Prepare(context, "Picker", "    Ok.Caption", "Ok");

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, "Choice").Refusal?.Kind);
    }

    // what renaming the control does to a property that holds its name is not known: whether Visual Basic 6.0 edits it is not measured.
    [TestMethod]
    public void AVisualBasic6Control_WhoseNameAPropertyOfItsFormHolds_IsNotRenamed_ForThatReason()
    {
        var context = Compose("Picker", Code);

        var refusal = Prepare(context, "Picker", "Choice(1).Caption = Text1", "Text1").Refusal;

        Assert.AreEqual(RenameRefusalKind.ControlNamedByProperty, refusal?.Kind);
        Assert.AreEqual("Label1.DataSource", refusal?.Other);
    }

    [TestMethod]
    [Ignore("Not supported yet: renaming a control that a property of its form names (Label1.DataSource names Text1); VB6's own behavior is not measured.")]
    public void AVisualBasic6Control_WhoseNameAPropertyOfItsFormHolds_IsRenamedThereToo()
    {
        var context = Compose("Picker", Code);

        Assert.AreEqual(Code.Replace("Text1", "Entry"), Renamed(context, "Picker", "Choice(1).Caption = Text1", "Text1", "Entry"));
    }

    [TestMethod]
    [DataRow("    Ok.Caption", "Ok", DisplayName = "where the code names it")]
    [DataRow("Private Sub Ok_Click", "Ok", DisplayName = "in the part of a handler's name that is its")]
    public void AUserFormControl_IsRenamedInItsFormsFrx_WhichThePlanNames(string line, string text)
    {
        var context = Compose("Dialog", DialogCode, DialogDesigner);
        var preparation = Prepare(context, "Dialog", line, text);

        Assert.IsTrue(SymbolRename.IsUserFormControl(context, preparation.Target!));
        Assert.AreEqual((ModuleUri("Dialog"), "Ok"), SymbolRename.Plan(context, preparation.Target!, "Accept").Control);
    }

    [TestMethod]
    public void AVisualBasic6Control_IsNotAUserFormControl_ThePlanNamesNoFrx()
    {
        var context = Compose("Picker", Code);
        var preparation = Prepare(context, "Picker", "    Ok.Caption", "Ok");

        Assert.IsFalse(SymbolRename.IsUserFormControl(context, preparation.Target!));
        Assert.IsNull(SymbolRename.Plan(context, preparation.Target!, "Accept").Control);
    }

    // Rubberduck renames a UserForm's control through the VBE, which writes the form's .frx: the code and the handlers are renamed with it.
    [TestMethod]
    public void AUserFormControl_IsRenamedWhereTheCodeNamesIt_AndInItsHandlers()
    {
        var context = Compose("Dialog", DialogCode, DialogDesigner);

        Assert.AreEqual(DialogCode.Replace("Ok_Click", "Accept_Click").Replace("Ok.Caption", "Accept.Caption"),
            Renamed(context, "Dialog", "    Ok.Caption", "Ok", "Accept"));
    }
}

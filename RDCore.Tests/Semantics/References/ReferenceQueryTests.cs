using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.References;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.Runtime.LanguageCore.References;

namespace RDCore.Tests.Semantics.References;

/// <summary>
/// The references of a symbol are the places that name it, bound as a rename binds them, each with how it names the symbol: its form and its use, the argument
/// list it is called with, the call it is an argument of and whether that call may take it by reference, and the owner of a member.
/// </summary>
[TestClass]
public sealed class ReferenceQueryTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static readonly string Class1 = Lines(
        "VERSION 1.0 CLASS",
        "BEGIN",
        "  MultiUse = -1  'True",
        "END",
        "Attribute VB_Name = \"Class1\"",
        "Option Explicit",
        "Private mValue As String",
        "Public Field As Long",
        "Public Property Get Value() As String",
        "    Value = mValue",
        "End Property",
        "Public Property Let Value(ByVal v As String)",
        "    mValue = v",
        "End Property");

    private static readonly string Helpers = Module("Helpers",
        "Option Explicit",
        "Public Total As Long",
        "Public Sub Add(ByVal amount As Long)",
        "    Total = Total + amount",
        "End Sub",
        "Public Sub Bump(ByRef value As Long)",
        "    value = value + 1",
        "End Sub",
        "Public Sub Many(ParamArray values() As Variant)",
        "End Sub");

    private static readonly string Program = Module("Program",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim count As Long",
        "    count = 1",
        "    Add count",
        "    Call Add(count + 1)",
        "    Bump count",
        "    Bump (count)",
        "    Bump value:=count",
        "    Many 1, count",
        "    Bump Total",
        "    Helpers.Bump Helpers.Total",
        "    Dim c As Class1",
        "    Set c = New Class1",
        "    c.Value = \"x\"",
        "    Debug.Print c.Value",
        "    With c",
        "        .Value = \"y\"",
        "    End With",
        "    Bump c.Field",
        "    Dim o As Object",
        "    o.Value = 1",
        "    Dim n As Object",
        "    Set n = Nothing",
        "    n.Value = 2",
        "    CallByName o, \"Bump\", VbMethod",
        "    CallByName o, \"Other.Bump\", VbMethod",
        "End Sub");

    private static readonly string Other = Module("Other",
        "Option Explicit",
        "Private Sub Bump()",
        "End Sub");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Module(string name, params string[] lines) => Lines([$"Attribute VB_Name = \"{name}\"", .. lines]);

    // the workspace of the modules, referencing the standard library: a .cls when the source is a class's.
    private static RenameContext Workspace()
    {
        (string Name, string Source)[] modules = [("Class1", Class1), ("Helpers", Helpers), ("Program", Program), ("Other", Other)];
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var libraries = new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver(),
            libraries: [.. libraries]);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // where the `occurrence`th `text` begins, on the first line that has `line`, in the module.
    private static SourcePosition Start(RenameContext context, string module, string line, string text, int occurrence = 1)
    {
        var lines = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        var character = -1;
        for (var found = 0; found < occurrence; found++)
        {
            character = lines[index].IndexOf(text, character + 1, StringComparison.Ordinal);
        }

        Assert.IsTrue(character >= 0, $"'{line}' has no {occurrence} '{text}'");
        return new SourcePosition(index, character);
    }

    private static SymbolReferences ReferencesAt(RenameContext context, string module, string line, string text)
    {
        var start = Start(context, module, line, text);
        var references = ReferenceQuery.At(context, ModuleUri(module), new SourcePosition(start.Line, start.Character + 1));
        Assert.IsNotNull(references, $"'{text}' on '{line}' names no symbol");
        return references;
    }

    // the reference that begins where the `occurrence`th `text` of the line does.
    private static SymbolReference Single(RenameContext context, IReadOnlyList<SymbolReference> references, string module, string line, string text, int occurrence = 1)
    {
        var start = Start(context, module, line, text, occurrence);
        var found = references.Where(reference => reference.Module.AbsoluteUri == ModuleUri(module).AbsoluteUri && reference.Range.Start == start).ToList();
        Assert.HasCount(1, found, $"'{text}' on '{line}' is {found.Count} references");
        return found[0];
    }

    [TestMethod]
    public void AVariable_IsReferencedByItsDeclaration_AndByEachNameThatAssignsOrReadsIt()
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Program", "Dim count", "count").References;

        Assert.HasCount(8, references);
        Assert.AreEqual((ReferenceForm.Declaration, ReferenceUse.None), FormAndUse(Single(context, references, "Program", "Dim count", "count")));
        Assert.AreEqual((ReferenceForm.Name, ReferenceUse.Let), FormAndUse(Single(context, references, "Program", "count = 1", "count")));
        Assert.AreEqual((ReferenceForm.Name, ReferenceUse.Read), FormAndUse(Single(context, references, "Program", "Add count", "count")));
    }

    [TestMethod]
    public void AnArgument_GivenToAByValParameter_IsNotPassedByReference()
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Program", "Dim count", "count").References;
        var argument = Single(context, references, "Program", "Add count", "count");

        Assert.IsInstanceOfType<CallStatementNode>(argument.ArgumentOf);
        Assert.IsFalse(argument.MayBePassedByRef);
    }

    [TestMethod]
    [DataRow("Bump count", DisplayName = "positional")]
    [DataRow("Bump value:=count", DisplayName = "named")]
    [DataRow("Many 1, count", DisplayName = "to a ParamArray")]
    public void AnArgument_GivenToAByRefParameter_MayBePassedByReference(string line)
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Program", "Dim count", "count").References;
        var argument = Single(context, references, "Program", line, "count");

        Assert.IsInstanceOfType<CallStatementNode>(argument.ArgumentOf);
        Assert.IsTrue(argument.MayBePassedByRef);
    }

    [TestMethod]
    [DataRow("Bump (count)", DisplayName = "in parentheses")]
    [DataRow("Call Add(count + 1)", DisplayName = "in a larger expression")]
    public void AName_ThatIsNoWholeArgument_IsNoArgument(string line)
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Program", "Dim count", "count").References;
        var name = Single(context, references, "Program", line, "count");

        Assert.IsNull(name.ArgumentOf);
        Assert.IsFalse(name.MayBePassedByRef);
    }

    [TestMethod]
    public void AProcedure_IsCalledWithTheArgumentListWrittenAfterIt()
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Program", "Add count", "Add").References;

        Assert.AreEqual(ReferenceForm.Declaration, Single(context, references, "Helpers", "Public Sub Add", "Add").Form);
        var statement = Single(context, references, "Program", "Add count", "Add");
        Assert.IsInstanceOfType<CallStatementNode>(statement.Call);
        Assert.HasCount(1, ((CallStatementNode)statement.Call).Arguments);
        var explicitCall = Single(context, references, "Program", "Call Add(count + 1)", "Add");
        Assert.IsInstanceOfType<IndexExpressionNode>(explicitCall.Call);
        Assert.HasCount(1, ((IndexExpressionNode)explicitCall.Call).Arguments);
    }

    [TestMethod]
    public void AMember_IsReferencedWithWhatItIsAMemberOf()
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Helpers", "Public Total", "Total").References;

        var qualified = Single(context, references, "Program", "Helpers.Bump Helpers.Total", "Total");
        Assert.AreEqual(ReferenceForm.Member, qualified.Form);
        Assert.AreEqual(ModuleUri("Helpers").AbsoluteUri, qualified.Owner?.Uri.AbsoluteUri);
        Assert.IsTrue(qualified.MayBePassedByRef);

        var bare = Single(context, references, "Program", "Bump Total", "Total");
        Assert.AreEqual(ReferenceForm.Name, bare.Form);
        Assert.IsNull(bare.Owner);
        Assert.IsTrue(bare.MayBePassedByRef);

        Assert.AreEqual(ReferenceUse.Let, Single(context, references, "Helpers", "Total = Total + amount", "Total").Use);
        Assert.AreEqual(ReferenceUse.Read, Single(context, references, "Helpers", "Total = Total + amount", "Total", 2).Use);
    }

    [TestMethod]
    public void AProperty_IsOneSymbolWithItsProcedures_AndEachReferenceSaysHowItUsesIt()
    {
        var context = Workspace();
        var found = ReferencesAt(context, "Program", "Debug.Print c.Value", "Value");
        var references = found.References;

        Assert.HasCount(2, found.Symbols);
        var let = Single(context, references, "Program", "c.Value = \"x\"", "Value");
        Assert.AreEqual((ReferenceForm.Member, ReferenceUse.Let), FormAndUse(let));
        Assert.IsNotNull(let.Owner);
        Assert.AreEqual((ReferenceForm.Member, ReferenceUse.Read), FormAndUse(Single(context, references, "Program", "Debug.Print c.Value", "Value")));
        var with = Single(context, references, "Program", ".Value = \"y\"", "Value");
        Assert.AreEqual((ReferenceForm.WithMember, ReferenceUse.Let), FormAndUse(with));
        Assert.IsNull(with.Owner);
        Assert.AreEqual(ReferenceUse.Result, Single(context, references, "Class1", "Value = mValue", "Value").Use);
        Assert.IsTrue(references.All(reference => reference.Symbol is { } symbol && found.Symbols.Contains(symbol)));
    }

    [TestMethod]
    public void AMemberAccessedLateBound_IsAnUnknownReference()
    {
        var context = Workspace();
        var unknown = ReferencesAt(context, "Program", "Debug.Print c.Value", "Value").Unknown;

        var late = Single(context, unknown, "Program", "o.Value = 1", "Value");
        Assert.AreEqual(ReferenceForm.Member, late.Form);
        Assert.IsNull(late.Symbol);
    }

    // an object that the code only ever Sets to Nothing is of no class: what is accessed on it is no unknown reference of a class's member.
    [TestMethod]
    public void AMemberAccessedLateBound_OnAnObjectOfNoClass_IsNoUnknownReference()
    {
        var context = Workspace();
        var unknown = ReferencesAt(context, "Program", "Debug.Print c.Value", "Value").Unknown;

        var start = Start(context, "Program", "n.Value = 2", "Value");
        Assert.IsFalse(unknown.Any(reference => reference.Range.Start == start));
        Assert.IsTrue(unknown.Any(reference => reference.Range.Start == Start(context, "Program", "o.Value = 1", "Value")), "an object of any class still is");
    }

    [TestMethod]
    public void AnObjectsVariable_GivenToAByRefParameter_IsACopy_AndNotPassedByReference()
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Class1", "Public Field", "Field").References;
        var argument = Single(context, references, "Program", "Bump c.Field", "Field");

        Assert.IsInstanceOfType<CallStatementNode>(argument.ArgumentOf);
        Assert.IsFalse(argument.MayBePassedByRef);
    }

    [TestMethod]
    public void AClass_IsReferencedByTheTypeNamesAndTheNewThatNameIt()
    {
        var context = Workspace();
        var references = ReferencesAt(context, "Program", "Dim c As Class1", "Class1").References;

        Assert.AreEqual(ReferenceForm.TypeName, Single(context, references, "Program", "Dim c As Class1", "Class1").Form);
        Assert.AreEqual(ReferenceForm.Name, Single(context, references, "Program", "Set c = New Class1", "Class1").Form);
    }

    [TestMethod]
    public void AStringALibraryCallsAProcedureBy_IsAnUnknownReference_UnlessItNamesAnotherModule()
    {
        var context = Workspace();
        var found = ReferencesAt(context, "Helpers", "Public Sub Bump", "Bump");

        var named = Single(context, found.Unknown, "Program", "CallByName o, \"Bump\"", "Bump");
        Assert.AreEqual((ReferenceForm.String, ReferenceUse.None), FormAndUse(named));
        Assert.IsFalse(found.Unknown.Any(reference => reference.Range.Start == Start(context, "Program", "\"Other.Bump\"", "Bump")));
    }

    private static (ReferenceForm, ReferenceUse) FormAndUse(SymbolReference reference) => (reference.Form, reference.Use);
}

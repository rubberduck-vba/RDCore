using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A navigation from a name leads to where its symbol is defined (<strong>LSP 3.17</strong> §Goto Definition Request): in any module of the workspace, the name
/// bound as a rename binds it.
/// </summary>
[TestClass]
public sealed class SymbolNavigationTests
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
        "End Sub");

    private static readonly string Program = Module("Program",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim count As Long",
        "    count = 1",
        "    Add count",
        "    Debug.Print Total",
        "    Dim c As Class1",
        "    Set c = New Class1",
        "    c.Value = \"x\"",
        "    Debug.Print c.Value",
        "    Dim o As Object",
        "    o.Value = 1",
        "    MsgBox Total",
        "End Sub");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Module(string name, params string[] lines) => Lines([$"Attribute VB_Name = \"{name}\"", .. lines]);

    // the workspace of the modules, by name: a .cls when the source is a class's.
    private static RenameContext Compose(params (string Name, string Source)[] modules)
    {
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree, [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static RenameContext Workspace() => Compose(("Class1", Class1), ("Helpers", Helpers), ("Program", Program));

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // the position of the `occurrence`th `text` on the first line that has `line`, in the module.
    private static SourcePosition At(RenameContext context, string module, string line, string text, int occurrence = 1)
    {
        var lines = Source(context, module);
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        var character = -1;
        for (var found = 0; found < occurrence; found++)
        {
            character = lines[index].IndexOf(text, character + 1, StringComparison.Ordinal);
        }

        Assert.IsTrue(character >= 0, $"'{line}' has no {occurrence} '{text}'");
        return new SourcePosition(index, character + 1);
    }

    private static string[] Source(RenameContext context, string module)
        => context.Modules.Single(candidate => candidate.Module.Fragment == "#" + module).Source.Split("\r\n");

    // what the site selects, as it is written in the module that declares it.
    private static string Selected(RenameContext context, SymbolSite site)
    {
        var line = Source(context, site.Module.Fragment.TrimStart('#'))[site.SelectionRange.Start.Line];
        return line[site.SelectionRange.Start.Character..site.SelectionRange.End.Character];
    }

    private static SymbolSite SingleSite(RenameContext context, string module, string line, string text, int occurrence = 1)
    {
        var target = SymbolNavigation.DefinitionsAt(context, ModuleUri(module), At(context, module, line, text, occurrence));
        Assert.IsNotNull(target, $"'{text}' on '{line}' leads nowhere");
        return target.Sites.Single();
    }

    [TestMethod]
    public void ALocalVariable_IsDefinedByItsDim()
    {
        var context = Workspace();
        var site = SingleSite(context, "Program", "count = 1", "count");

        Assert.AreEqual("#Program", site.Module.Fragment);
        Assert.AreEqual(At(context, "Program", "Dim count", "count").Line, site.SelectionRange.Start.Line);
        Assert.AreEqual("count", Selected(context, site));
    }

    [TestMethod]
    public void AProcedureOfAnotherModule_IsDefinedThere_FromItsFirstLineToItsEnd()
    {
        var context = Workspace();
        var site = SingleSite(context, "Program", "Add count", "Add");

        Assert.AreEqual("#Helpers", site.Module.Fragment);
        Assert.AreEqual("Add", Selected(context, site));
        var lines = Source(context, "Helpers");
        Assert.StartsWith("Public Sub Add", lines[site.Range.Start.Line]);
        Assert.AreEqual("End Sub", lines[site.Range.End.Line].Trim());
    }

    [TestMethod]
    public void AVariableOfAnotherModule_IsDefinedThere()
    {
        var context = Workspace();
        var site = SingleSite(context, "Program", "Debug.Print Total", "Total");

        Assert.AreEqual("#Helpers", site.Module.Fragment);
        Assert.AreEqual("Total", Selected(context, site));
    }

    [TestMethod]
    public void AParameter_IsDefinedInTheSignature()
    {
        var context = Workspace();
        var site = SingleSite(context, "Helpers", "Total = Total + amount", "amount");

        Assert.AreEqual(At(context, "Helpers", "Public Sub Add", "amount").Line, site.SelectionRange.Start.Line);
        Assert.AreEqual("amount", Selected(context, site));
    }

    // a property is defined by each of its procedures: what an access calls is not known from the name alone.
    [TestMethod]
    [DataRow("c.Value = \"x\"", DisplayName = "assigned")]
    [DataRow("Debug.Print c.Value", DisplayName = "read")]
    public void APropertyOfAClass_IsDefinedByEachOfItsProcedures(string line)
    {
        var context = Workspace();
        var target = SymbolNavigation.DefinitionsAt(context, ModuleUri("Program"), At(context, "Program", line, "Value"));

        Assert.IsNotNull(target);
        Assert.IsTrue(target.Sites.All(site => site.Module.Fragment == "#Class1"));
        CollectionAssert.AreEqual(new[] { "Public Property Get Value() As String", "Public Property Let Value(ByVal v As String)" },
            target.Sites.Select(site => Source(context, "Class1")[site.Range.Start.Line]).ToArray());
        Assert.IsTrue(target.Sites.All(site => Selected(context, site) == "Value"));
    }

    [TestMethod]
    [DataRow("Dim c As Class1", DisplayName = "a declared type")]
    [DataRow("Set c = New Class1", DisplayName = "a New")]
    public void AClass_IsDefinedByItsModule(string line)
    {
        var context = Workspace();
        var site = SingleSite(context, "Program", line, "Class1");

        Assert.AreEqual("#Class1", site.Module.Fragment);
        Assert.AreEqual(default, site.SelectionRange.Start);
    }

    [TestMethod]
    public void ADeclaration_IsItsOwnDefinition()
    {
        var context = Workspace();
        var site = SingleSite(context, "Program", "Dim count", "count");

        Assert.AreEqual(At(context, "Program", "Dim count", "count").Line, site.SelectionRange.Start.Line);
    }

    [TestMethod]
    public void TheNavigation_StartsFromTheNameAsItIsWritten()
    {
        var context = Workspace();
        var position = At(context, "Program", "Debug.Print Total", "Total");
        var target = SymbolNavigation.DefinitionsAt(context, ModuleUri("Program"), position)!;

        Assert.AreEqual(new SourceRange(position.Line, position.Character - 1, position.Line, position.Character + 4), target.Origin);
    }

    // a member accessed late-bound is not known to be any symbol's.
    [TestMethod]
    public void ALateBoundMember_LeadsNowhere()
    {
        var context = Workspace();

        Assert.IsNull(SymbolNavigation.DefinitionsAt(context, ModuleUri("Program"), At(context, "Program", "o.Value = 1", "Value")));
    }

    [TestMethod]
    public void AMemberOfTheLibrary_IsNoPlaceOfTheWorkspace()
    {
        var context = Workspace();
        var target = SymbolNavigation.DefinitionsAt(context, ModuleUri("Program"), At(context, "Program", "MsgBox Total", "MsgBox"));

        Assert.IsEmpty(target?.Sites ?? []);
    }

    [TestMethod]
    public void NoName_LeadsNowhere()
    {
        var context = Workspace();

        Assert.IsNull(SymbolNavigation.DefinitionsAt(context, ModuleUri("Program"), At(context, "Program", "count = 1", "=")));
    }

    // MS-VBAL 3.4: a name declared in each branch of an #If is defined by each branch the precompiler has not excluded.
    [TestMethod]
    public void ANameDeclaredInEachBranchOfAnIf_IsDefinedByEach()
    {
        var context = Compose(("Module1", Module("Module1",
            "#If VBA7 Then",
            "Private Value As LongPtr",
            "#Else",
            "Private Value As Long",
            "#End If",
            "Public Sub Main()",
            "    Value = 1",
            "End Sub")));

        var target = SymbolNavigation.DefinitionsAt(context, ModuleUri("Module1"), At(context, "Module1", "    Value = 1", "Value"));

        Assert.IsNotNull(target);
        CollectionAssert.AreEqual(new[] { 2, 4 }, target.Sites.Select(site => site.SelectionRange.Start.Line).ToArray());
    }

    // --- textDocument/declaration: the one place that declares the symbol, the same whichever branch is compiled ---

    [TestMethod]
    public void ANameDeclaredInEachBranchOfAnIf_IsDeclaredByTheFirst()
    {
        var context = Compose(("Module1", Module("Module1",
            "#If VBA7 Then",
            "Private Value As LongPtr",
            "#Else",
            "Private Value As Long",
            "#End If",
            "Public Sub Main()",
            "    Value = 1",
            "End Sub")));

        var target = SymbolNavigation.DeclarationAt(context, ModuleUri("Module1"), At(context, "Module1", "    Value = 1", "Value"));

        Assert.IsNotNull(target);
        Assert.AreEqual(2, target.Sites.Single().SelectionRange.Start.Line);
    }

    [TestMethod]
    public void AProperty_IsDeclaredByItsFirstProcedure()
    {
        var context = Workspace();
        var site = SymbolNavigation.DeclarationAt(context, ModuleUri("Program"), At(context, "Program", "c.Value = \"x\"", "Value"))!.Sites.Single();

        Assert.AreEqual("#Class1", site.Module.Fragment);
        Assert.AreEqual("Public Property Get Value() As String", Source(context, "Class1")[site.Range.Start.Line]);
        Assert.AreEqual("Value", Selected(context, site));
    }

    [TestMethod]
    [DataRow("count = 1", "count", "Program", DisplayName = "a local")]
    [DataRow("Add count", "Add", "Helpers", DisplayName = "a procedure")]
    [DataRow("Dim c As Class1", "Class1", "Class1", DisplayName = "a class")]
    public void ASymbolDeclaredOnce_IsDeclaredWhereItIsDefined(string line, string text, string module)
    {
        var context = Workspace();
        var position = At(context, "Program", line, text);
        var declaration = SymbolNavigation.DeclarationAt(context, ModuleUri("Program"), position)!;
        var definition = SymbolNavigation.DefinitionsAt(context, ModuleUri("Program"), position)!;

        Assert.AreEqual("#" + module, declaration.Sites.Single().Module.Fragment);
        Assert.AreEqual(definition.Sites.Single(), declaration.Sites.Single());
        Assert.AreEqual(definition.Origin, declaration.Origin);
    }

    [TestMethod]
    public void AMemberOfTheLibrary_IsDeclaredNowhereInTheWorkspace()
    {
        var context = Workspace();

        Assert.IsEmpty(SymbolNavigation.DeclarationAt(context, ModuleUri("Program"), At(context, "Program", "MsgBox Total", "MsgBox"))?.Sites ?? []);
        Assert.IsNull(SymbolNavigation.DeclarationAt(context, ModuleUri("Program"), At(context, "Program", "o.Value = 1", "Value")));
    }

    // --- textDocument/references: every name that refers to the symbol, in every module ---

    // each site of a reference, as module:line, in the order answered.
    private static string[] Referenced(RenameContext context, SymbolNavigationTarget? target)
    {
        Assert.IsNotNull(target);
        Assert.IsTrue(target.Sites.All(site => site.Range == site.SelectionRange && Selected(context, site).Length > 0), "a reference is the name");
        return [.. target.Sites.Select(site => $"{site.Module.Fragment.TrimStart('#')}:{Source(context, site.Module.Fragment.TrimStart('#'))[site.Range.Start.Line].Trim()}")];
    }

    [TestMethod]
    public void AVariableOfAModule_IsReferredTo_InEveryModule()
    {
        var context = Workspace();
        var position = At(context, "Program", "Debug.Print Total", "Total");

        CollectionAssert.AreEqual(new[]
        {
            "Helpers:Public Total As Long", "Helpers:Total = Total + amount", "Helpers:Total = Total + amount",
            "Program:Debug.Print Total", "Program:MsgBox Total",
        }, Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Program"), position)));

        Assert.HasCount(4, SymbolNavigation.ReferencesAt(context, ModuleUri("Program"), position, includeDeclaration: false)!.Sites);
    }

    [TestMethod]
    public void ALocal_IsReferredTo_InItsProcedure()
        => CollectionAssert.AreEqual(new[] { "Program:Dim count As Long", "Program:count = 1", "Program:Add count" },
            Referenced(Workspace(), SymbolNavigation.ReferencesAt(Workspace(), ModuleUri("Program"), At(Workspace(), "Program", "count = 1", "count"))));

    // each procedure of a property is referred to by the names that call it; a member accessed late-bound is not known to be it.
    [TestMethod]
    public void AnAssignmentOfAProperty_RefersToItsLet_AndNotByALateBoundOne()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Class1:Public Property Let Value(ByVal v As String)", "Program:c.Value = \"x\"" },
            Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Program"), At(context, "Program", "c.Value = \"x\"", "Value"))));
    }

    [TestMethod]
    public void AReadOfAProperty_RefersToItsGet_AndNotTheResultItsGetAssigns()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Class1:Public Property Get Value() As String", "Program:Debug.Print c.Value" },
            Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Program"), At(context, "Program", "Debug.Print c.Value", "Value"))));
        CollectionAssert.AreEqual(new[] { "Program:Debug.Print c.Value" },
            Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Class1"), At(context, "Class1", "Property Get Value", "Value"), includeDeclaration: false)),
            "from the name the Get declares");
    }

    [TestMethod]
    public void EveryNameOfAProperty_IsWhatARenameRenames_WhicheverProcedureItCalls()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[]
        {
            "Class1:Public Property Get Value() As String", "Class1:Value = mValue", "Class1:Public Property Let Value(ByVal v As String)",
            "Program:c.Value = \"x\"", "Program:Debug.Print c.Value",
        }, Referenced(context, SymbolNavigation.OccurrencesAt(context, ModuleUri("Program"), At(context, "Program", "c.Value = \"x\"", "Value"))));
    }

    [TestMethod]
    public void ASetAssignmentOfAProperty_RefersToItsSet_ALetAssignmentToItsLet()
    {
        var bag = Lines(
            "VERSION 1.0 CLASS",
            "BEGIN",
            "  MultiUse = -1  'True",
            "END",
            "Attribute VB_Name = \"Bag\"",
            "Option Explicit",
            "Private mItem As Variant",
            "Public Property Get Item() As Variant",
            "    Item = mItem",
            "End Property",
            "Public Property Let Item(ByVal v As Variant)",
            "    mItem = v",
            "End Property",
            "Public Property Set Item(ByVal v As Object)",
            "    Set mItem = v",
            "End Property");
        var user = Module("User",
            "Option Explicit",
            "Public Sub Use(ByVal b As Bag)",
            "    b.Item = 1",
            "    Set b.Item = Nothing",
            "    Set b.Item = b",
            "    Debug.Print b.Item",
            "End Sub");
        var context = Compose(("Bag", bag), ("User", user));
        string[] Of(string line) => Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Bag"), At(context, "Bag", line, "Item"), includeDeclaration: false));

        CollectionAssert.AreEqual(new[] { "User:Debug.Print b.Item" }, Of("Property Get Item"));
        CollectionAssert.AreEqual(new[] { "User:b.Item = 1" }, Of("Property Let Item"));
        CollectionAssert.AreEqual(new[] { "User:Set b.Item = Nothing", "User:Set b.Item = b" }, Of("Property Set Item"));
    }

    [TestMethod]
    public void TheResultAFunctionAssigns_IsNoReferenceOfIt_ACallOfItIs()
    {
        var context = Compose(("Maths", Module("Maths",
            "Option Explicit",
            "Public Function Twice(ByVal n As Long) As Long",
            "    Twice = n * 2",
            "End Function",
            "Public Sub Use()",
            "    Debug.Print Twice(2)",
            "End Sub")));

        CollectionAssert.AreEqual(new[] { "Maths:Debug.Print Twice(2)" },
            Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Maths"), At(context, "Maths", "Function Twice", "Twice"), includeDeclaration: false)));
    }

    // MS-VBAL §5.6.2.1: a name that refers to the enclosing function evaluates to its return value; with an argument list, it calls the function again.
    [TestMethod]
    public void TheResultAFunctionReads_IsNoReferenceOfIt_ARecursiveCallIs()
    {
        var context = Compose(("Maths", Module("Maths",
            "Option Explicit",
            "Public Function Fact(ByVal n As Long) As Long",
            "    If n <= 1 Then",
            "        Fact = 1",
            "    Else",
            "        Fact = n * Fact(n - 1)",
            "    End If",
            "End Function",
            "Public Function Counter() As Long",
            "    Counter = Counter + 1",
            "    Debug.Print Counter",
            "End Function")));
        string[] Of(string line, string name) => Referenced(context,
            SymbolNavigation.ReferencesAt(context, ModuleUri("Maths"), At(context, "Maths", line, name), includeDeclaration: false));

        CollectionAssert.AreEqual(new[] { "Maths:Fact = n * Fact(n - 1)" }, Of("Function Fact", "Fact"), "the recursive call, and not the result");
        Assert.IsEmpty(Of("Function Counter", "Counter"), "what its body assigns and reads is its result");
        CollectionAssert.AreEqual(new[]
        {
            "Maths:Public Function Counter() As Long", "Maths:Counter = Counter + 1", "Maths:Counter = Counter + 1", "Maths:Debug.Print Counter",
        }, Referenced(context, SymbolNavigation.OccurrencesAt(context, ModuleUri("Maths"), At(context, "Maths", "Function Counter", "Counter"))),
            "a rename renames the result too");
    }

    [TestMethod]
    public void AClass_IsReferredTo_WhereATypeNamesIt()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Program:Dim c As Class1", "Program:Set c = New Class1" },
            Referenced(context, SymbolNavigation.ReferencesAt(context, ModuleUri("Program"), At(context, "Program", "Dim c As Class1", "Class1"))));
    }

    [TestMethod]
    public void ALateBoundMember_HasNoReferences()
        => Assert.IsNull(SymbolNavigation.ReferencesAt(Workspace(), ModuleUri("Program"), At(Workspace(), "Program", "o.Value = 1", "Value")));
}

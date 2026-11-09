using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using RDCore.Runtime.LanguageCore.Refactoring;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A rename renames what refers to the symbol, wherever it is written, and nothing else; and it refuses a new name that would change what the code means.
/// </summary>
[TestClass]
public sealed class SymbolRenameTests
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
        "Attribute Value.VB_UserMemId = 0",
        "    Value = mValue",
        "End Property",
        "Public Property Let Value(ByVal v As String)",
        "    mValue = v",
        "End Property");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Module(string name, params string[] lines) => Lines([$"Attribute VB_Name = \"{name}\"", .. lines]);

    // the workspace of the modules, by name: a .cls when the source is a class's.
    private static RenameContext Compose(params (string Name, string Source)[] modules) => ComposeReferencing([], modules);

    // the workspace of the modules, referencing the standard library and a host's: what calls a procedure by its name in a string.
    private static RenameContext ComposeWithHost(params (string Name, string Source)[] modules)
        => ComposeReferencing(
            [
                .. new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols(),
                .. new LibrarySymbolReader(Root).Read([Host]),
            ], modules);

    // a host's library: the members a host calls a procedure by its name with - a macro it runs, a procedure it runs at a time or on a key, a control's action.
    private static readonly LibraryDescription Host = new()
    {
        Name = "Host",
        Origin = new LibraryOrigin { Guid = "{00000000-0000-0000-0000-0000000000AA}", Version = "1.0", ExportedBy = "tests" },
        Classes =
        [
            new ClassDescription
            {
                Name = "Application",
                Members =
                [
                    new MemberDescription
                    {
                        Name = "Run", Kind = MemberKind.Method, Type = "Variant",
                        Parameters = [new ParameterDescription { Name = "Macro", Type = "Variant", IsOptional = true }],
                    },
                    new MemberDescription
                    {
                        Name = "OnTime", Kind = MemberKind.Method,
                        Parameters =
                        [
                            new ParameterDescription { Name = "EarliestTime", Type = "Variant" },
                            new ParameterDescription { Name = "Procedure", Type = "String" },
                        ],
                    },
                ],
            },
            new ClassDescription
            {
                Name = "Button",
                Members =
                [
                    new MemberDescription { Name = "OnAction", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription
                    {
                        Name = "OnAction", Kind = MemberKind.PropertyLet,
                        Parameters = [new ParameterDescription { Name = "RHS", Type = "String", IsByVal = true }],
                    },
                ],
            },
        ],
    };

    private static RenameContext ComposeReferencing(IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> libraries, (string Name, string Source)[] modules)
    {
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: new UriBuilder(Root) { Fragment = module.Name }.Uri, Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver(),
            libraries: libraries.Count == 0 ? null : libraries);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // the position of the first `text` on the line that has `line`, in the module.
    private static SourcePosition At(RenameContext context, string module, string line, string text)
    {
        var lines = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        return new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal) + 1);
    }

    private static RenamePlan Rename(RenameContext context, string module, string line, string text, string newName)
    {
        var preparation = SymbolRename.Prepare(context, ModuleUri(module), At(context, module, line, text));
        Assert.IsNotNull(preparation.Target, preparation.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        return SymbolRename.Plan(context, preparation.Target, newName);
    }

    // the text of the module once the rename's edits are made.
    private static string Renamed(RenameContext context, RenamePlan plan, string module)
    {
        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        var lines = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source.Split("\r\n");
        foreach (var edit in plan.Edits.Where(edit => SymbolUri.ModuleNameOf(edit.Module) == module).OrderByDescending(edit => edit.Range.Start))
        {
            var line = lines[edit.Range.Start.Line];
            lines[edit.Range.Start.Line] = line[..edit.Range.Start.Character] + edit.NewText + line[edit.Range.End.Character..];
        }

        return string.Join("\r\n", lines);
    }

    [TestMethod]
    public void ALocal_IsRenamedWhereItsProcedureRefersToIt_AndNotInAnotherProcedure()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub One()",
            "    Dim x As Long",
            "    x = x + 1 ' x",
            "    Debug.Print \"x\", x",
            "End Sub",
            "Public Sub Two()",
            "    Dim x As Long",
            "    x = 2",
            "End Sub")));

        var plan = Rename(context, "Main", "x = x + 1", "x", "count");

        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim count As Long",
            "    count = count + 1 ' x",
            "    Debug.Print \"x\", count",
            "End Sub",
            "Public Sub Two()",
            "    Dim x As Long",
            "    x = 2",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheCounterOfAForLoop_IsRenamedInTheNextThatNamesIt()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Count()",
            "    Dim x As Integer",
            "    For x = 1 To 10",
            "        Debug.Print \"Hello RDCore!\"",
            "    Next x",
            "End Sub")));

        var plan = Rename(context, "Main", "For x = 1", "x", "tehDuck");

        Assert.AreEqual(Module("Main",
            "Public Sub Count()",
            "    Dim tehDuck As Integer",
            "    For tehDuck = 1 To 10",
            "        Debug.Print \"Hello RDCore!\"",
            "    Next tehDuck",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheElementOfAForEachLoop_IsRenamedInTheNextThatNamesIt()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub List(ByVal items As Collection)",
            "    Dim item As Variant",
            "    For Each item In items",
            "        Debug.Print item",
            "    Next item",
            "End Sub")));

        var plan = Rename(context, "Main", "For Each item", "item", "entry");

        Assert.AreEqual(Module("Main",
            "Public Sub List(ByVal items As Collection)",
            "    Dim entry As Variant",
            "    For Each entry In items",
            "        Debug.Print entry",
            "    Next entry",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheCounterOfAnOuterLoop_IsRenamedAfterTheCommaOfANextThatEndsBoth_FromTheNext()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Grid()",
            "    Dim i As Long, j As Long",
            "    For i = 1 To 3",
            "        For j = 1 To 3",
            "            Debug.Print i * j",
            "    Next j, i",
            "End Sub")));

        var plan = Rename(context, "Main", "Next j, i", "i", "row");

        Assert.AreEqual(Module("Main",
            "Public Sub Grid()",
            "    Dim row As Long, j As Long",
            "    For row = 1 To 3",
            "        For j = 1 To 3",
            "            Debug.Print row * j",
            "    Next j, row",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheCounterOfALoop_WhoseNextNamesNoVariable_IsRenamedWhereItIsWritten()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Count()",
            "    Dim x As Integer",
            "    For x = 1 To 10",
            "        Debug.Print x",
            "    Next",
            "End Sub")));

        var plan = Rename(context, "Main", "For x = 1", "x", "tehDuck");

        Assert.AreEqual(Module("Main",
            "Public Sub Count()",
            "    Dim tehDuck As Integer",
            "    For tehDuck = 1 To 10",
            "        Debug.Print tehDuck",
            "    Next",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AModuleVariable_IsRenamedInEveryModule_QualifiedOrNot()
    {
        var context = Compose(
            ("Main", Module("Main", "Public strName As String", "Public Sub One()", "    strName = \"a\"", "End Sub")),
            ("Other", Module("Other", "Public Sub Two()", "    Debug.Print strName, Main.strName", "End Sub")));

        var plan = Rename(context, "Other", "Debug.Print", "strName", "Name1");

        Assert.AreEqual(Module("Main", "Public Name1 As String", "Public Sub One()", "    Name1 = \"a\"", "End Sub"), Renamed(context, plan, "Main"));
        Assert.AreEqual(Module("Other", "Public Sub Two()", "    Debug.Print Name1, Main.Name1", "End Sub"), Renamed(context, plan, "Other"));
    }

    [TestMethod]
    public void AProperty_IsRenamedWithEachAccessor_ItsMemberAccesses_AndItsAttribute()
    {
        var context = Compose(("Class1", Class1), ("Main", Module("Main",
            "Public Sub One()",
            "    Dim c As Class1",
            "    Set c = New Class1",
            "    c.Value = \"a\"",
            "    With c",
            "        .Value = .Value & \"b\"",
            "    End With",
            "End Sub")));

        var plan = Rename(context, "Main", "c.Value", "Value", "Text");

        Assert.AreEqual(Class1.Replace(" Value", " Text"), Renamed(context, plan, "Class1"), "and not mValue, which is another name");
        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim c As Class1",
            "    Set c = New Class1",
            "    c.Text = \"a\"",
            "    With c",
            "        .Text = .Text & \"b\"",
            "    End With",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AParameter_IsRenamedWithTheNamedArgumentsOfTheCallsOfItsProcedure()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Show(ByVal count As Long)",
            "    Debug.Print count",
            "End Sub",
            "Public Sub One(ByVal count As Long)",
            "    Show count:=count",
            "    Show count:=1",
            "End Sub")));

        var plan = Rename(context, "Main", "Public Sub Show", "count", "times");

        Assert.AreEqual(Module("Main",
            "Public Sub Show(ByVal times As Long)",
            "    Debug.Print times",
            "End Sub",
            "Public Sub One(ByVal count As Long)",
            "    Show times:=count",
            "    Show times:=1",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AConstant_IsRenamedInTheExpressionOfAnotherConstant()
    {
        var context = Compose(("Main", Module("Main",
            "Private Const Size2 As Long = 3",
            "Private Const Size As Long = 2 * Size2",
            "Public Sub One()",
            "    Debug.Print Size2",
            "End Sub")));

        var plan = Rename(context, "Main", "Debug.Print", "Size2", "Width");

        Assert.AreEqual(Module("Main",
            "Private Const Width As Long = 3",
            "Private Const Size As Long = 2 * Width",
            "Public Sub One()",
            "    Debug.Print Width",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AFieldOfAType_AndAMemberOfAnEnum_AreRenamedWhereTheyAreUsed()
    {
        var context = Compose(("Main", Module("Main",
            "Private Enum Color",
            "    Red = 1",
            "    Green",
            "End Enum",
            "Private Type Point",
            "    X As Long",
            "End Type",
            "Public Sub One()",
            "    Dim p As Point",
            "    p.X = Green + Color.Red",
            "End Sub")));

        var field = Rename(context, "Main", "p.X", "X", "Left1");
        var member = Rename(context, "Main", "p.X", "Green", "Blue");

        Assert.AreEqual(Module("Main",
            "Private Enum Color", "    Red = 1", "    Green", "End Enum", "Private Type Point", "    Left1 As Long", "End Type",
            "Public Sub One()", "    Dim p As Point", "    p.Left1 = Green + Color.Red", "End Sub"), Renamed(context, field, "Main"));
        Assert.AreEqual(Module("Main",
            "Private Enum Color", "    Red = 1", "    Blue", "End Enum", "Private Type Point", "    X As Long", "End Type",
            "Public Sub One()", "    Dim p As Point", "    p.X = Blue + Color.Red", "End Sub"), Renamed(context, member, "Main"));
    }

    [TestMethod]
    public void TheMemberOfAClassThatIsImplemented_AndTheProcedureThatImplementsIt_AreNotRenamed()
    {
        var context = Compose(
            ("IShape", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"IShape\"", "Public Sub Draw()", "End Sub")),
            ("Square", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Square\"", "Implements IShape", "Private Sub IShape_Draw()", "End Sub")));

        Assert.AreEqual(RenameRefusalKind.InterfaceMember, SymbolRename.Prepare(context, ModuleUri("IShape"), At(context, "IShape", "Public Sub Draw", "Draw")).Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.NamedByConvention, SymbolRename.Prepare(context, ModuleUri("Square"), At(context, "Square", "IShape_Draw", "IShape_Draw")).Refusal?.Kind);
    }

    [TestMethod]
    public void ANameWrittenWithATypeCharacter_KeepsIt()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim s$", "    s$ = \"a\"", "    Debug.Print s", "End Sub")));

        var plan = Rename(context, "Main", "Debug.Print", "s", "text");

        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim text$", "    text$ = \"a\"", "    Debug.Print text", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheKeyOfADictionaryAccess_IsNotRenamed()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub One()",
            "    Dim key As Long",
            "    Dim c As Collection",
            "    Set c = New Collection",
            "    Debug.Print c!key, key",
            "End Sub")));

        var plan = Rename(context, "Main", "Dim key", "key", "index");

        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim index As Long",
            "    Dim c As Collection",
            "    Set c = New Collection",
            "    Debug.Print c!key, index",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void ARenameThatOnlyChangesTheCase_IsMade()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim total As Long", "    total = 1", "End Sub")));

        var plan = Rename(context, "Main", "total = 1", "total", "Total");

        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim Total As Long", "    Total = 1", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void ANameDeclaredWhereTheSymbolIs_IsRefused()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim a As Long", "    Dim b As Long", "    a = b", "End Sub")));

        var plan = Rename(context, "Main", "Dim a", "a", "B");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind);
    }

    [TestMethod]
    public void ANameThatMeansSomethingNearerWhereTheSymbolIsUsed_IsRefused()
    {
        var context = Compose(("Main", Module("Main",
            "Private total As Long",
            "Public Sub One()",
            "    Dim count As Long",
            "    count = total",
            "End Sub")));

        var plan = Rename(context, "Main", "Private total", "total", "count");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind, "the procedure's count would be the one it reads");
    }

    [TestMethod]
    public void ANameTheSymbolWouldBecomeNearerThan_IsRefused()
    {
        var context = Compose(("Main", Module("Main",
            "Private total As Long",
            "Public Sub One()",
            "    Dim count As Long",
            "    count = total",
            "End Sub")));

        var plan = Rename(context, "Main", "Dim count", "count", "total");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind, "the procedure's total would be the local, and no longer the module's");
    }

    [TestMethod]
    public void ANameThatIsNoIdentifier_IsRefused()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim a As Long", "End Sub")));
        var target = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Dim a", "a")).Target!;

        Assert.AreEqual(RenameRefusalKind.Reserved, SymbolRename.Plan(context, target, "Next").Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.NotAnIdentifier, SymbolRename.Plan(context, target, "2nd").Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.NotAnIdentifier, SymbolRename.Plan(context, target, "a b").Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.Empty, SymbolRename.Plan(context, target, string.Empty).Refusal?.Kind);
        Assert.IsTrue(SymbolRename.Plan(context, target, "Données").IsValid, "a letter of another script is a letter");
    }

    [TestMethod]
    public void WhatTheWorkspaceDoesNotDeclare_IsNotRenamed()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Debug.Print Left(\"abc\", 1)", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Debug.Print", "Left"));

        Assert.AreEqual(RenameRefusalKind.NotDeclaredByWorkspace, preparation.Refusal?.Kind);
    }

    [TestMethod]
    public void AType_IsNotRenamedYet()
    {
        var context = Compose(("Class1", Class1), ("Main", Module("Main", "Public Sub One()", "    Dim c As Class1", "    Set c = New Class1", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Set c", "Class1"));

        Assert.AreEqual(RenameRefusalKind.Type, preparation.Refusal?.Kind);
    }

    [TestMethod]
    public void WhereNoNameIsWritten_ThereIsNothingToRename()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Debug.Print 42", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Debug.Print", "42"));

        Assert.AreEqual(RenameRefusalKind.NoName, preparation.Refusal?.Kind);
    }

    // a French project's names: what a refusal says names the module, the symbol and the place as the code is written, not as an address escapes them.
    [TestMethod]
    public void AConflictInAModuleNamedWithAnAccent_NamesWhatItIsAndWhere_AsTheCodeDoes()
    {
        var context = Compose(("Modèle", Module("Modèle", "Public Sub Calculer()", "    Dim a As Long", "    Dim Durée As Long", "    a = Durée", "End Sub")));

        var plan = Rename(context, "Modèle", "Dim a", "a", "durée");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind);
        var message = plan.Refusal!.ToMessage(culture: CultureInfo.InvariantCulture);
        StringAssert.Contains(message, "Modèle.Calculer.Durée");
        Assert.IsFalse(message.Contains('%', StringComparison.Ordinal), message);
    }

    [TestMethod]
    public void TheMemberOfAClassThatAClassNamedWithAnAccentImplements_IsRefused_NamingTheImplementerAsItsCodeDoes()
    {
        var context = Compose(
            ("IForme", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"IForme\"", "Public Sub Dessiner()", "End Sub")),
            ("Carré", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Carré\"", "Implements IForme", "Private Sub IForme_Dessiner()", "End Sub")));

        var refusal = SymbolRename.Prepare(context, ModuleUri("IForme"), At(context, "IForme", "Public Sub Dessiner", "Dessiner")).Refusal;

        Assert.AreEqual(RenameRefusalKind.InterfaceMember, refusal?.Kind);
        StringAssert.Contains(refusal!.ToMessage(culture: CultureInfo.InvariantCulture), "IForme, Carré");
    }

    [TestMethod]
    public void AFieldOfATypeNamedWithAnAccent_IsRenamedWhereItIsUsed()
    {
        var context = Compose(("Modèle", Module("Modèle",
            "Private Type Personne",
            "    Prénom As String",
            "End Type",
            "Public Sub Saluer()",
            "    Dim p As Personne",
            "    p.Prénom = \"Éloïse\"",
            "End Sub")));

        var plan = Rename(context, "Modèle", "p.Prénom", "Prénom", "Nom");

        Assert.AreEqual(Module("Modèle",
            "Private Type Personne", "    Nom As String", "End Type",
            "Public Sub Saluer()", "    Dim p As Personne", "    p.Nom = \"Éloïse\"", "End Sub"), Renamed(context, plan, "Modèle"));
    }

    [TestMethod]
    public void ARefusal_SaysWhyInTheLanguageOfTheReader()
    {
        var refusal = new RenameRefusal(RenameRefusalKind.Reserved, "Next");

        Assert.AreEqual("'Next' is a keyword of the language: nothing can be named 'Next'.", refusal.ToMessage(culture: CultureInfo.InvariantCulture));
        Assert.AreEqual("« Next » est un mot clé du langage : rien ne peut être nommé « Next ».", refusal.ToMessage(culture: new CultureInfo("fr-CA")));
    }

    // what a library calls by its name in a string is only known when the code runs: the string is renamed too, and its edit says so, for the user to confirm.
    [TestMethod]
    public void AProcedureCalledByItsNameInAString_IsRenamedThere_AsAnEditToConfirm()
    {
        var context = ComposeWithHost(
            ("Main", Module("Main",
                "Public Sub Foo()",
                "End Sub",
                "Public Sub Bar(ByVal app As Application, ByVal b As Button)",
                "    Dim o As Object",
                "    CallByName o, \"Foo\", VbMethod",
                "    app.Run \"Main.Foo\"",
                "    app.Run Macro:=\"'Book1.xlsm'!Foo\"",
                "    app.OnTime Now, \"Foo\"",
                "    b.OnAction = \"Main.Foo\"",
                "End Sub")));

        var plan = Rename(context, "Main", "Public Sub Foo", "Foo", "Baz");

        Assert.AreEqual(Module("Main",
            "Public Sub Baz()",
            "End Sub",
            "Public Sub Bar(ByVal app As Application, ByVal b As Button)",
            "    Dim o As Object",
            "    CallByName o, \"Baz\", VbMethod",
            "    app.Run \"Main.Baz\"",
            "    app.Run Macro:=\"'Book1.xlsm'!Baz\"",
            "    app.OnTime Now, \"Baz\"",
            "    b.OnAction = \"Main.Baz\"",
            "End Sub"), Renamed(context, plan, "Main"));
        Assert.AreEqual(1, plan.Edits.Count(edit => !edit.IsInString), "the declaration is the only edit that is not in a string");
        Assert.AreEqual(5, plan.Edits.Count(edit => edit.IsInString));
    }

    // another class with a member of the same name as Class1's.
    private static readonly string Class2 = Lines(
        "VERSION 1.0 CLASS",
        "BEGIN",
        "  MultiUse = -1  'True",
        "END",
        "Attribute VB_Name = \"Class2\"",
        "Option Explicit",
        "Public Value As String",
        "Private Sub Helper()",
        "End Sub");

    // which member a late-bound access calls is only known when the code runs: where the object may be of the renamed member's class, the access is renamed,
    // and its edit says so, for the user to confirm, and whether what the code says of the object backs the member.
    [TestMethod]
    [DataRow("Public Sub Bar(ByVal o As Object)", "", false, DisplayName = "a parameter, which the callers assign")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = New Class1", true, DisplayName = "a local Set to the class")]
    [DataRow("Public Sub Bar()", "    Dim o As Variant|    Set o = New Class2|    Set o = New Class1", true, DisplayName = "a Variant Set to either class")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = Make()", false, DisplayName = "a local Set to an Object a function returns")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = New Class2|    Take o", false, DisplayName = "a local given to a call, which may Set it")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = New Class1|    Take o", true, DisplayName = "a local Set to the class, given to a call")]
    [DataRow("Public Sub Bar(ByVal items As Collection)", "    Dim o As Object|    For Each o In items|    Next", false, DisplayName = "the variable of a For Each loop")]
    public void ALateBoundAccess_OfAnObjectThatMayBeOfTheClass_IsRenamed_AsAnEditToConfirm(string header, string assigned, bool isBacked)
    {
        var context = Compose(("Class1", Class1), ("Class2", Class2), ("Main", Module("Main",
            [
                header,
                .. assigned.Split('|', StringSplitOptions.RemoveEmptyEntries),
                "    o.Value = \"x\"",
                "    With o",
                "        Debug.Print .Value",
                "    End With",
                "End Sub",
                "Public Function Make() As Object",
                "End Function",
                "Public Sub Take(ByRef o As Object)",
                "End Sub",
            ])));

        var plan = Rename(context, "Class1", "Public Property Get Value", "Value", "Amount");

        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        var lateBound = plan.Edits.Where(edit => edit.IsLateBound).ToList();
        Assert.HasCount(2, lateBound, "the access after a '.', and the one in the With block");
        Assert.IsTrue(lateBound.All(edit => SymbolUri.ModuleNameOf(edit.Module) == "Main" && !edit.IsInString));
        Assert.IsTrue(lateBound.All(edit => edit.IsBacked == isBacked), isBacked ? "the code says the object holds a Class1" : "nothing says what it holds");
        StringAssert.Contains(Renamed(context, plan, "Main"), "    o.Amount = \"x\"");
        StringAssert.Contains(Renamed(context, plan, "Main"), "        Debug.Print .Amount");
        Assert.IsFalse(plan.Edits.Any(edit => SymbolUri.ModuleNameOf(edit.Module) == "Class2"), "Class2's own Value is another member");
    }

    // where the assignments of the code say every class the object may be of, and the renamed member's is not one of them, the access is not renamed.
    [TestMethod]
    [DataRow("    Dim o As Object|    Set o = New Class2", DisplayName = "a local Set to another class only")]
    [DataRow("    Dim o As Object|    Set o = New Class2|    Set o = Nothing", DisplayName = "a local Set to another class, then Nothing")]
    [DataRow("    Dim o As Object", DisplayName = "a local never Set, which holds Nothing")]
    [DataRow("    Set o = New Class2", DisplayName = "a Private variable of the module, Set to another class only")]
    public void ALateBoundAccess_OfAnObjectOfAnotherClassOnly_IsNotRenamed(string assigned)
    {
        var context = Compose(("Class1", Class1), ("Class2", Class2), ("Main", Module("Main",
            [
                "Option Explicit",
                "Private o As Object",
                "Public Sub Bar()",
                .. assigned.Split('|', StringSplitOptions.RemoveEmptyEntries),
                "    o.Value = \"x\"",
                "End Sub",
            ])));

        var plan = Rename(context, "Class1", "Public Property Get Value", "Value", "Amount");

        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        Assert.IsFalse(plan.Edits.Any(edit => edit.IsLateBound || SymbolUri.ModuleNameOf(edit.Module) == "Main"));
    }

    // a member that code outside of its class can't call - a Private one, or one of a standard module - is never what a late-bound access calls.
    [TestMethod]
    public void ALateBoundAccess_OfAPrivateMember_OrOfAStandardModulesMember_IsNotRenamed()
    {
        var context = Compose(("Class2", Class2), ("Helpers", Module("Helpers",
                "Public Sub Run()",
                "End Sub")),
            ("Main", Module("Main",
                "Public Sub Bar(ByVal o As Object)",
                "    o.Helper",
                "    o.Run",
                "End Sub")));

        var helper = Rename(context, "Class2", "Private Sub Helper", "Helper", "Assist");
        var run = Rename(context, "Helpers", "Public Sub Run", "Run", "Go");

        Assert.IsFalse(helper.Edits.Any(edit => edit.IsLateBound), "a Private member is not found late-bound");
        Assert.IsFalse(run.Edits.Any(edit => edit.IsLateBound), "an object is never a standard module");
    }

    // what is known of the classes an object may be of is open: a source the workspace is given says what the code does not.
    [TestMethod]
    public void ALateBoundAccess_IsNotRenamed_WhereASourceSaysTheObjectIsOfAnotherClassOnly()
    {
        var context = Compose(("Class1", Class1), ("Class2", Class2), ("Main", Module("Main",
            "Public Sub Bar(ByVal o As Object)",
            "    o.Value = \"x\"",
            "End Sub")));
        var class2 = context.Resolver.ResolveType("Class2", RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Local, ModuleUri("Main")).Symbol!.SemanticId;
        var class1 = context.Resolver.ResolveType("Class1", RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Local, ModuleUri("Main")).Symbol!.SemanticId;
        var known = context with { ClassSources = [new KnownClasses(new PossibleClasses([class2], IsComplete: true))] };
        var seen = context with { ClassSources = [new KnownClasses(new PossibleClasses([class1], IsComplete: false))] };

        var open = Rename(context, "Class1", "Public Property Get Value", "Value", "Amount");
        var closed = Rename(known, "Class1", "Public Property Get Value", "Value", "Amount");
        var backed = Rename(seen, "Class1", "Public Property Get Value", "Value", "Amount");

        Assert.AreEqual(1, open.Edits.Count(edit => edit.IsLateBound && !edit.IsBacked), "the parameter may be of any class");
        Assert.IsFalse(closed.Edits.Any(edit => edit.IsLateBound), "the source rules Class1 out");
        Assert.AreEqual(1, backed.Edits.Count(edit => edit.IsLateBound && edit.IsBacked), "the source says the parameter holds a Class1, among others");
    }

    // a source that knows the classes of every expression it is asked about.
    private sealed class KnownClasses(PossibleClasses classes) : IPossibleClassesSource
    {
        public PossibleClasses? ClassesOf(PossibleClassesQuery query) => classes;
    }

    // a string is renamed only where a library calls a procedure by it: not a message, not the name of a local, not a member of another module.
    [TestMethod]
    public void AStringThatDoesNotCallTheProcedure_IsNotRenamed()
    {
        var context = ComposeWithHost(
            ("Main", Module("Main",
                "Public Sub Foo()",
                "End Sub",
                "Public Sub Bar(ByVal app As Application)",
                "    MsgBox \"Foo\"",
                "    app.Run \"Other.Foo\"",
                "    Debug.Print \"Foo\"",
                "End Sub")),
            ("Other", Module("Other", "Public Sub Foo()", "End Sub")));

        var plan = Rename(context, "Main", "Public Sub Foo", "Foo", "Baz");

        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        Assert.IsFalse(plan.Edits.Any(edit => edit.IsInString), "no string calls Main.Foo");
        Assert.HasCount(1, plan.Edits);
    }

    // a local is not called by its name: a string that names it is a string.
    [TestMethod]
    public void ALocalNamedByAString_IsRenamed_AndTheStringIsNot()
    {
        var context = ComposeWithHost(("Main", Module("Main",
            "Public Sub Bar()",
            "    Dim Foo As Object",
            "    CallByName Foo, \"Foo\", VbMethod",
            "End Sub")));

        var plan = Rename(context, "Main", "Dim Foo", "Foo", "Baz");

        Assert.AreEqual(Module("Main",
            "Public Sub Bar()",
            "    Dim Baz As Object",
            "    CallByName Baz, \"Foo\", VbMethod",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheConfirmation_IsAskedInTheLanguageOfTheReader()
    {
        Assert.AreEqual("'Foo' is called by its name in 2 strings, the first at Main 5:17. Rename it there too?",
            RefactoringTitles.RenameInStringQuestion("Foo", 2, "Main 5:17", CultureInfo.InvariantCulture));
        Assert.AreEqual("« Foo » est appelé par son nom dans une chaîne, à Main 5:17. Le renommer là aussi?",
            RefactoringTitles.RenameInStringQuestion("Foo", 1, "Main 5:17", new CultureInfo("fr-CA")));
    }
}

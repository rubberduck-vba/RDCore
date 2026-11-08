using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
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
        Assert.AreEqual("« Foo » est appelé par son nom dans une chaîne, à Main 5:17. Le renommer là aussi ?",
            RefactoringTitles.RenameInStringQuestion("Foo", 1, "Main 5:17", new CultureInfo("fr-CA")));
    }
}

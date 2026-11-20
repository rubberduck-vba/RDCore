using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The static pass counts the references to the declarations of a module, and states a count only when it is the whole truth: when nothing outside the code
/// analyzed can refer to the declaration, and the code that could was analyzed completely.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3 Declarations")]
public sealed class DeclarationUsageTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Main = new UriBuilder(Root) { Fragment = "Main" }.Uri;

    // the facts of the declarations of a standard module whose source is `source`.
    private static ImmutableArray<DeclarationFact> Facts(params string[] source) => Project(("Main", source)).Single().Declarations;

    // the facts of the declarations of `module`, of a project of standard modules.
    private static ImmutableArray<DeclarationFact> ProjectFacts(string module, params (string Name, string[] Source)[] modules)
        => Project(modules).Single(model => model.Module.AbsoluteUri == ModuleUri(module).AbsoluteUri).Declarations;

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // the models of the standard modules of a project, each made as the module loader makes it, but for the calls of a module's own procedures: what one
    // does with the arguments it takes by reference is not followed.
    private static List<ModuleSemanticModel> Project(params (string Name, string[] Source)[] modules) => Project(followsArguments: false, modules);

    // the facts of the declarations of a standard module whose source is `source`, made as the module loader makes them: the calls of its own procedures
    // are followed.
    private static ImmutableArray<DeclarationFact> FollowedFacts(params string[] source)
        => Project(followsArguments: true, ("Main", source)).Single().Declarations;

    private static List<ModuleSemanticModel> Project(bool followsArguments, params (string Name, string[] Source)[] modules)
    {
        var parses = modules.Select(module =>
        {
            var parse = new ModuleParser().Parse(
                new Uri($"file:///c:/ws/{module.Name}.bas"), $"Attribute VB_Name = \"{module.Name}\"\r\n{string.Join("\r\n", module.Source)}\r\n");
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), parse);
        }).ToList();

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [.. parses.Select(module => (module.Uri, ModuleType.StdModule, module.parse))], new IntrinsicSymbolResolver());
        var models = new List<ModuleSemanticModel>();
        foreach (var (uri, parse) in parses)
        {
            var symbols = new SyntaxTreeSymbolProvider(Root, uri, ModuleType.StdModule, parse, composition.Resolver, withImplicitDeclarations: true).ProvideSymbols().ToList();
            var members = symbols.OfType<VBTypeMemberSymbol>().Where(member => member.ParentUri.AbsoluteUri == uri.AbsoluteUri).ToList();

            var procedures = new List<ProcedureSemanticModel>();
            var unanalyzedNames = new List<string>();
            var arguments = new ArgumentParameters();
            foreach (var declaration in parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Where(declaration => declaration.MemberKind is not (MemberKind.Enum or MemberKind.UserDefinedType)))
            {
                var procedure = members.First(member => string.Equals(member.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)
                    && member is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol);
                var model = StatementStaticSemanticsEvaluator.Analyze(
                    procedure.SemanticId, new StatementBlock([.. declaration.Children]), kind: declaration.MemberKind,
                    context: new StaticEvaluationContext(composition.Resolver, composition.ScopeTree.ScopeFor(procedure.Uri)) { Arguments = arguments });
                procedures.Add(model);
                if (!model.IsFullyAnalyzed)
                {
                    unanalyzedNames.AddRange(DeclarationUsage.NamesIn(declaration));
                }
            }

            var declarations = ModuleDeclarationStaticSemantics.Analyze(parse.SyntaxTree.Children, new StaticEvaluationContext(composition.Resolver, composition.ScopeTree.ScopeFor(uri)));
            // a provider may state a procedure's locals and parameters as symbols of their own, as well as on the procedure.
            var declared = DeclarationUsage.DeclaredBy(members).Concat(symbols).ToList();
            models.Add(new ModuleSemanticModel(uri, [], [.. procedures])
            {
                Declarations = DeclarationUsage.Of(
                    declared, procedures, byRefArguments: followsArguments ? DeclarationUsage.ArgumentsOf(declared, procedures, arguments) : null,
                    declarations: declarations, namesInUnanalyzedCode: unanalyzedNames),
            });
        }

        return models;
    }

    private static DeclarationFact Fact(ImmutableArray<DeclarationFact> facts, string name) => facts.Single(fact => fact.Name == name);

    private static DeclarationReferences References(ImmutableArray<DeclarationFact> facts, string name)
    {
        var references = Fact(facts, name).References;
        Assert.IsNotNull(references, $"'{name}' has references that are known");
        return references.Value;
    }

    // ---- what is counted ----

    [TestMethod]
    public void ALocalThatNothingRefersTo_HasNoReferences()
        => Assert.AreEqual(default, References(Facts("Public Sub Run()", "Dim unused As Long", "End Sub"), "unused"));

    [TestMethod]
    public void ALocalThatIsOnlyRead_IsReadAndNotWritten()
        => Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(Facts("Public Sub Run()", "Dim x", "Dim n As Long", "x = n", "End Sub"), "n"));

    [TestMethod]
    public void ALocalThatIsWrittenAndRead_IsCountedBoth()
        => Assert.AreEqual(new DeclarationReferences(1, 1, 0), References(Facts("Public Sub Run()", "Dim x", "Dim n As Long", "n = 1", "x = n", "End Sub"), "n"));

    [TestMethod]
    public void AnElementOfAnArray_IsWrittenThroughTheArray()
        => Assert.AreEqual(1, References(Facts("Public Sub Run()", "Dim a(3) As Long", "a(1) = 5", "End Sub"), "a").Writes);

    [TestMethod]
    public void TheCounterOfALoop_IsWritten()
        => Assert.AreEqual(1, References(Facts("Public Sub Run()", "Dim i As Long", "For i = 1 To 3", "Next", "End Sub"), "i").Writes);

    [TestMethod]
    public void ReDim_IsAWriteToTheArray()
        => Assert.AreEqual(1, References(Facts("Public Sub Run()", "Dim a() As Long", "ReDim a(3)", "End Sub"), "a").Writes);

    [TestMethod]
    public void AVariablePassedToAProcedure_IsPassedAsAnArgument_WhichMayWriteToItThroughByRef()
    {
        var facts = Facts("Private Sub Work(p As Long)", "End Sub", "Public Sub Run()", "Dim n As Long", "Work n", "End Sub");

        Assert.AreEqual(new DeclarationReferences(0, 0, 1), References(facts, "n"));
    }

    // the one place the code refers to the declaration.
    private static DeclarationReference Site(ImmutableArray<DeclarationFact> facts, string name)
    {
        var sites = Fact(facts, name).ReferencedFrom;
        Assert.IsNotNull(sites, $"'{name}' has references that are known");
        return Assert.ContainsSingle(sites.Value);
    }

    // MS-VBAL §5.3.1.11: the call assigns the argument when its procedure writes the ByRef parameter, or passes it on by reference to one that does.
    [TestMethod]
    public void AnArgumentThatAProcedureOfTheChainAssigns_IsAssignedThroughTheCall_AtEachLevel()
    {
        var facts = FollowedFacts(
            "Public Sub Run()", "Dim n As Long", "Relay n", "End Sub",
            "Private Sub Relay(ByRef x As Long)", "Fill x", "End Sub",
            "Private Sub Fill(ByRef y As Long)", "y = 1", "End Sub");

        Assert.AreEqual(new DeclarationReferences(0, 0, 1), References(facts, "n"));
        Assert.IsTrue(Site(facts, "n").AssignedThroughCall, "Relay passes it on to Fill, which assigns it");
        Assert.IsTrue(Site(facts, "x").AssignedThroughCall, "Fill assigns it");
    }

    [TestMethod]
    public void AnArgumentThatNoProcedureOfTheChainAssigns_IsRead_AndNotAssignedThroughTheCall()
    {
        var facts = FollowedFacts(
            "Public Sub Run()", "Dim n As Long", "Relay n", "End Sub",
            "Private Sub Relay(ByRef x As Long)", "Peek x", "End Sub",
            "Private Sub Peek(ByRef y As Long)", "Debug.Print y", "End Sub");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(facts, "n"));
        Assert.IsFalse(Site(facts, "n").AssignedThroughCall);
    }

    // a procedure that was not fully analyzed may assign what it is given; what one that was analyzed assigns is known whatever the rest of the module is.
    [TestMethod]
    public void AnArgumentThatAnAnalyzedProcedureAssigns_IsAssignedThroughTheCall_ThoughAnotherProcedureWasNotAnalyzed()
    {
        var facts = FollowedFacts(
            "Option Explicit",
            "Public Sub Run()", "Dim filled As Long", "Fill filled", "Dim given As Long", "Broken given", "End Sub",
            "Private Sub Fill(ByRef y As Long)", "y = 1", "End Sub",
            "Private Sub Broken(ByRef z As Long)", "Debug.Print Undeclared", "End Sub");

        Assert.IsTrue(Site(facts, "filled").AssignedThroughCall);
        Assert.AreEqual(DeclarationReferenceKind.PassedAsArgument, Site(facts, "given").Kind, "Broken may assign it");
        Assert.IsFalse(Site(facts, "given").AssignedThroughCall);
    }

    [TestMethod]
    [DataRow("Abs")]
    [DataRow("Int")]
    [DataRow("Sgn")]
    public void AVariablePassedToAByValParameterOfTheLibrary_IsRead(string function)
    {
        var facts = Facts("Public Sub Run()", "Dim x As Long", "x = 5", $"Debug.Print {function}(x)", "End Sub");

        // MS-VBAL §5.3.1.11: the function takes its argument ByVal, which the call cannot assign.
        Assert.AreEqual(new DeclarationReferences(1, 1, 0), References(facts, "x"));
    }

    [TestMethod]
    public void AVariablePassedToAByValParameter_IsRead()
    {
        var facts = Facts("Private Sub Work(ByVal n As Long)", "End Sub", "Public Sub Run()", "Dim local As Long", "Work local", "End Sub");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(facts, "local"));
    }

    [TestMethod]
    public void AnArgumentWrittenWithByVal_IsRead_ForItCanNotBeWrittenTo()
    {
        var facts = Facts("Private Sub Work(ByVal n As Long)", "End Sub", "Public Sub Run()", "Dim local As Long", "Work ByVal local", "End Sub");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(facts, "local"));
    }

    [TestMethod]
    public void AnIndexOfAnArray_IsNoArgument()
        => Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(Facts("Public Sub Run()", "Dim a(3) As Long", "Dim i As Long", "a(i) = 1", "End Sub"), "i"));

    [TestMethod]
    public void AParameterThatIsNeverUsed_HasNoReferences()
        => Assert.AreEqual(default, References(Facts("Public Sub Run(ByVal n As Long)", "End Sub"), "n"));

    [TestMethod]
    public void AVariableThatWasNeverDeclared_IsImplicit()
    {
        var facts = Facts("Public Sub Run()", "Dim x", "x = 1", "y = 2", "End Sub");

        Assert.IsTrue(Fact(facts, "y").IsImplicit);
        Assert.IsFalse(Fact(facts, "x").IsImplicit);
        Assert.AreEqual(1, References(facts, "y").Writes);
    }

    [TestMethod]
    public void APrivateModuleVariable_IsCountedAcrossTheProceduresThatUseIt()
    {
        var facts = Facts("Private Total As Long", "Public Sub Add()", "Total = Total + 1", "End Sub", "Public Sub Show()", "Debug.Print Total", "End Sub");

        Assert.AreEqual(new DeclarationReferences(2, 1, 0), References(facts, "Total"));
        Assert.AreEqual(AccessModifier.Private, Fact(facts, "Total").Access);
    }

    // ---- what is not known is not stated ----

    [TestMethod]
    public void APublicVariable_HasNoCount_ForAnotherModuleCanReferToIt()
        => Assert.IsNull(Fact(Facts("Public Total As Long", "Public Sub Run()", "End Sub"), "Total").References);

    [TestMethod]
    [DataRow("Private Sub Work()\r\nEnd Sub", "Work")]
    [DataRow("Public Sub Work()\r\nEnd Sub", "Work")]
    [DataRow("Public Property Get Work() As Long\r\nEnd Property", "Work")]
    public void AProcedureAPropertyOrAnEvent_HasNoCount_ForItIsCalledByMoreThanWhatNamesIt(string declaration, string name)
        => Assert.IsNull(Fact(Facts(declaration, "Public Sub Run()", "Work", "End Sub"), name).References);

    [TestMethod]
    public void TheAccessorsOfAProperty_AreOneDeclaration()
    {
        var facts = Facts("Private mV As Long", "Public Property Get V() As Long", "V = mV", "End Property", "Public Property Let V(ByVal n As Long)", "mV = n", "End Property");

        Assert.AreEqual(DeclarationKind.Property, Fact(facts, "V").Kind);
    }

    [TestMethod]
    public void ALocalOfAProcedureWithAnError_HasNoCount_ForWhatIsAfterTheErrorWasNotLookedAt()
        => Assert.IsNull(Fact(Facts("Option Explicit", "Public Sub Run()", "Dim x", "Dim n As Long", "x = Undeclared + n", "End Sub"), "n").References);

    // a procedure with an error that names the variable may refer to it, in what is after the error.
    [TestMethod]
    [DataRow("x = Undeclared + Total", DisplayName = "named by an expression")]
    [DataRow("x = Undeclared + Total&", DisplayName = "named with its type-declaration character")]
    [DataRow("x = Undeclared + [Total]", DisplayName = "named in square brackets")]
    [DataRow("If Undeclared Then x = Total", DisplayName = "named in the body of a statement")]
    [DataRow("x = Undeclared: Total = 1", DisplayName = "assigned after the error")]
    public void AModuleVariable_HasNoCount_WhenAProcedureWithAnErrorNamesIt(string statement)
    {
        var facts = Facts("Option Explicit", "Private Total As Long", "Public Sub Show()", "Debug.Print Total", "End Sub", "Public Sub Broken()", "Dim x", statement, "End Sub");

        Assert.IsNull(Fact(facts, "Total").References);
    }

    // a constant of the module is read by the code of the module and by the expressions of its declarations; another module cannot refer to it.
    [TestMethod]
    [DataRow("Private Const Size As Long = 3", "", 0, DisplayName = "read by nothing")]
    [DataRow("Const Size As Long = 3", "", 0, DisplayName = "read by nothing, with no access modifier")]
    [DataRow("Private Const Size As Long = 3", "Debug.Print Size", 1, DisplayName = "read by a statement")]
    [DataRow("Private Const Size As Long = 3", "Dim a(1 To Size) As Long", 1, DisplayName = "read by the bounds of an array of a procedure")]
    [DataRow("Private Const Size As Long = 3\r\nPrivate Const Twice As Long = Size * 2", "", 1, DisplayName = "read by the value of another constant")]
    [DataRow("Private Const Size As Long = 3\r\nPrivate Items(Size) As Long", "", 1, DisplayName = "read by the bounds of an array of the module")]
    [DataRow("Private Const Size As Long = 3\r\nPrivate Enum Sizes\r\nLarge = Size\r\nEnd Enum", "", 1, DisplayName = "read by the value of an enumeration member")]
    [DataRow("Private Const Size As Long = 3\r\nPrivate Type Row\r\nCells(Size) As Long\r\nEnd Type", "", 1, DisplayName = "read by the bounds of an array field of a record")]
    [DataRow("Private Const Size As Long = 3\r\nPrivate Declare PtrSafe Sub Work Lib \"lib\" (Optional ByVal n As Long = Size)", "", 1, DisplayName = "read by the default of a parameter of a Declare")]
    [DataRow("Private Const Size As Long = 3\r\nPrivate Sub Work(Optional ByVal n As Long = Size)\r\nEnd Sub", "", 1, DisplayName = "read by the default of a parameter of a procedure")]
    [DataRow("Private Const Size As Long = 3", "Debug.Print Size\r\nDebug.Print Size + 1", 2, DisplayName = "read twice")]
    public void AConstantOfTheModule_HasItsReadsCounted(string declarations, string statements, int reads)
    {
        var facts = Facts("Option Explicit", declarations, "Public Sub Run()", statements, "End Sub");

        Assert.AreEqual(new DeclarationReferences(reads, 0, 0), References(facts, "Size"));
    }

    [TestMethod]
    public void APrivateConstant_IsNotReadFromAnotherModule()
    {
        var facts = ProjectFacts("Main",
            ("Main", ["Option Explicit", "Private Const Size As Long = 3", "Public Sub Run()", "End Sub"]),
            ("Other", ["Public Sub Show()", "Debug.Print Size", "End Sub"]));

        Assert.AreEqual(default, References(facts, "Size"));
    }

    [TestMethod]
    public void AConstantThatGivesTheLengthOfAStringOfTheModule_IsNeverStatedAsNeverRead()
    {
        var references = Fact(Facts("Private Const Size As Long = 3", "Private Buffer As String * Size", "Public Sub Run()", "End Sub"), "Size").References;

        Assert.IsTrue(references is null || references.Value.Reads == 1);
    }

    [TestMethod]
    public void AConstantOfTheModuleWithAnErrorInItsDeclarations_HasNoCount()
        => Assert.IsNull(Fact(Facts("Option Explicit", "Private Const Size As Long = 3", "Private Const Twice As Long = Undeclared + Size", "Public Sub Run()", "End Sub"), "Size").References);

    [TestMethod]
    public void AConstantOfTheModule_HasNoCount_WhenAProcedureWithAnErrorNamesIt()
        => Assert.IsNull(Fact(Facts("Option Explicit", "Private Const Size As Long = 3", "Public Sub Run()", "Debug.Print Undeclared + Size", "End Sub"), "Size")
            .References);

    [TestMethod]
    public void AConstantOfTheModule_IsCounted_WhenNoProcedureWithAnErrorNamesIt()
        => Assert.AreEqual(
            default, References(Facts("Option Explicit", "Private Const Size As Long = 3", "Public Sub Run()", "Debug.Print Undeclared", "End Sub"), "Size"));

    // Rubberduck's unused-variable inspection reports a variable of a module with an error elsewhere, that the code with the error never names.
    [TestMethod]
    public void AModuleVariable_IsCounted_WhenNoProcedureWithAnErrorNamesIt()
    {
        var facts = Facts(
            "Option Explicit", "Private Total As Long", "Private Unused As Long",
            "Public Sub Show()", "Debug.Print Total", "End Sub",
            "Public Sub Broken()", "Dim x", "x = Undeclared", "End Sub");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(facts, "Total"));
        Assert.AreEqual(default, References(facts, "Unused"));
    }

    [TestMethod]
    public void AModuleVariable_HasNoCount_WhenAProcedureHasAnError_AndWhatItNamesIsNotKnown()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"),
            "Attribute VB_Name = \"Main\"\r\nOption Explicit\r\nPrivate Unused As Long\r\nPublic Sub Broken()\r\nDim x\r\nx = Undeclared\r\nEnd Sub\r\n");
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Main, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(Root, Main, ModuleType.StdModule, parse, composition.Resolver, withImplicitDeclarations: true).ProvideSymbols().ToList();
        var broken = symbols.OfType<VBProcedureMemberSymbol>().Single(member => member.Name == "Broken");
        var declaration = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var model = StatementStaticSemanticsEvaluator.Analyze(
            broken.SemanticId, new StatementBlock([.. declaration.Children]), kind: declaration.MemberKind,
            context: new StaticEvaluationContext(composition.Resolver, composition.ScopeTree.ScopeFor(broken.Uri)));

        var facts = DeclarationUsage.Of(symbols, [model]);

        Assert.IsFalse(model.IsFullyAnalyzed);
        Assert.IsNull(Fact(facts, "Unused").References);
    }

    [TestMethod]
    public void TheNamesOfAProcedure_AreThoseOfItsExpressionsAndOfTheirMembers_InEveryBodyOfIt()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), string.Join("\r\n",
            "Attribute VB_Name = \"Main\"",
            "Public Sub Run()",
            "If Ready Then",
            "    Counter = Counter + 1",
            "End If",
            "Debug.Print Sheet.Range(Address)",
            "ReDim Items(Size)",
            "End Sub",
            ""));
        var names = DeclarationUsage.NamesIn(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single());

        foreach (var name in new[] { "Ready", "Counter", "Sheet", "Range", "Address", "Items", "Size" })
        {
            Assert.IsTrue(names.Contains(name), $"'{name}' is a name of the procedure: {string.Join(", ", names)}");
        }
    }

    [TestMethod]
    [DataRow("Public", DisplayName = "Public")]
    [DataRow("Global", DisplayName = "Global")]
    public void AConstantOfTheModuleThatOtherModulesCanReferTo_HasNoCount(string access)
        => Assert.IsNull(Fact(Facts($"{access} Const Size As Long = 3", "Public Sub Run()", "Debug.Print Size", "End Sub"), "Size").References);

    // a constant of a procedure is read by the procedure alone: its statements, the value of another of its constants, the bounds of its arrays.
    [TestMethod]
    [DataRow("Unused", 0, DisplayName = "read by nothing")]
    [DataRow("Read", 1, DisplayName = "read by a statement")]
    [DataRow("Base", 1, DisplayName = "read by the value of another constant")]
    [DataRow("Derived", 1, DisplayName = "read by the bounds of an array")]
    public void AConstantOfAProcedure_HasItsReadsCounted(string name, int reads)
    {
        var facts = Facts(
            "Option Explicit",
            "Public Sub Run()",
            "Const Unused As Long = 1",
            "Const Read As Long = 2",
            "Debug.Print Read",
            "Const Base As Long = 3",
            "Const Derived As Long = Base + 1",
            "Dim items(Derived) As Long",
            "Debug.Print items(0)",
            "End Sub");

        Assert.AreEqual(new DeclarationReferences(reads, 0, 0), References(facts, name));
    }

    [TestMethod]
    public void AConstantThatGivesTheLengthOfAString_IsNeverStatedAsNeverRead()
    {
        var references = Fact(Facts("Public Sub Run()", "Const Size As Long = 3", "Dim s As String * Size", "s = \"a\"", "End Sub"), "Size").References;

        Assert.IsTrue(references is null || references.Value.Reads == 1);
    }

    [TestMethod]
    public void ALengthOfAStringWrittenAsANumber_LeavesTheCountsStated()
        => Assert.AreEqual(new DeclarationReferences(0, 1, 0), References(Facts("Public Sub Run()", "Dim s As String * 3", "s = \"a\"", "End Sub"), "s"));

    [TestMethod]
    public void AConstantOfAProcedureWithAnError_HasNoCount()
        => Assert.IsNull(Fact(Facts("Option Explicit", "Public Sub Run()", "Const Size As Long = 3", "Debug.Print Undeclared + Size", "End Sub"), "Size").References);

    [TestMethod]
    public void AVariableInTheBoundsOfAnArray_IsNeverStatedAsNeverRead()
    {
        // not valid VBA, which wants a constant expression: but the pass does not say so, and a count that left the reference out would be a lie.
        var references = Fact(Facts("Public Sub Run()", "Dim n As Long", "Dim a(1 To n) As Long", "End Sub"), "n").References;

        Assert.IsTrue(references is null || references.Value.Reads == 1);
    }
}

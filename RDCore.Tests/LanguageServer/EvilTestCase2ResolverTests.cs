using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Legacy-Rubberduck issue #973, "Evil Test Case 2 for Parser/Resolver" — the complete sample: one identifier
/// (<c>MyProject</c> / <c>MyModule</c> / <c>MyProc</c>) reused at once as a module name, a
/// <c>Const</c>, a <c>Type</c>, a field, a procedure, a local, a parameter and an interface member, with an
/// <c>Interface</c> class, a <c>Class</c> that implements it, and a <c>With</c>-nested, <c>Set</c>-heavy
/// procedure body. The issue states no expected results, so every expectation here is derived from
/// MS-VBAL.
/// <para>
/// The collisions are legal, not duplicates: <strong>MS-VBAL §5.6.4</strong> binds a name in one of two
/// contexts. A simple name expression uses the default binding context, whose candidates
/// (<strong>§5.6.10</strong>) include no user-defined type; <c>As X</c> and <c>New X</c> use the type
/// binding context, whose candidates are only types, modules and the project. A <c>Const</c> and a
/// <c>Type</c> of one name therefore never compete for the same lookup.
/// </para>
/// </summary>
[TestClass]
public sealed class EvilTestCase2ResolverTests
{
    private static readonly Uri WorkspaceRoot = new("file://rdcore-test");

    // MyModule.bas — every module-scope name collides; MyProc1 shadows them with locals.
    private const string MyModuleSource = """
        Attribute VB_Name = "MyModule"
        Option Explicit

        Public Const MyProject As String = "String"
        Public Const MyModule As String = MyProject
        Public Const MyConst As String = MyModule

        Public MyProc As Interface

        Type MyProc
          MyModule As String
          MyProject As String
        End Type

        Type MyModule
          MyProject As MyProc
        End Type

        Type MyProject
          MyModule As MyModule
        End Type

        Sub MyProc1()
          Dim MyProject As Interface
          Dim MyModule
          Dim MyCircular
          Dim o
          Set MyProject = New Interface
          Set MyProject.MyModule = MyProject
          Set MyModule = New Interface
          Set MyProject.MyModule = MyModule
          With MyProject
            With MyModule
              Set MyProject = New Class
              Set MyProject.MyModule = New MyProject.Class
              Set MyProject.MyModule.MyProc = MyProject
              Set o = MyProject.MyModule
              Set MyModule.MyProc = MyProject.MyModule
              Set .MyProc = MyProject.MyModule
              On Error Resume Next
              Set .MyModule.MyProject.MyModule.MyProject = .MyModule.MyProject
              On Error GoTo 0
            End With
            Set .MyProc = .MyModule
          End With
        End Sub
        """;

    // Interface.cls — the interface, whose members all return the interface itself. Neither class module is
    // predeclared: the sample is exactly as the issue lists it (imported into a VBE by the author, 2026-09-18).
    private const string InterfaceSource = """
        Attribute VB_Name = "Interface"
        Option Explicit

        Public Property Get MyProject() As Interface
        End Property

        Public Property Set MyProject(MyModule As Interface)
        End Property

        Public Property Get MyModule() As Interface
        End Property

        Public Property Set MyModule(MyProject As Interface)
        End Property

        Public Property Get MyProc() As Interface
        End Property

        Public Property Set MyProc(MyProject As Interface)
        End Property
        """;

    // Class.cls — implements the interface; its own private Type and field reuse the same names.
    private const string ClassSource = """
        Attribute VB_Name = "Class"
        Option Explicit

        Implements Interface

        Private MyProject As MyModule

        Private Type MyModule
          MyProject As Interface
          MyModule As Interface
          MyProc As Interface
        End Type

        Private Property Set Interface_MyModule(MyModule As Interface)
          Set MyProject.MyModule = MyModule
        End Property

        Private Property Get Interface_MyModule() As Interface
          Set Interface_MyModule = MyProject.MyModule
        End Property

        Private Property Set Interface_MyProc(MyModule As Interface)
          Set MyProject.MyProject = MyModule
        End Property

        Private Property Get Interface_MyProc() As Interface
          Set MyProject.MyModule = MyProject.MyProc
        End Property

        Private Property Set Interface_MyProject(MyModule As Interface)
          Set MyProject.MyProc = MyModule
        End Property

        Private Property Get Interface_MyProject() As Interface
          Set Interface = New Interface
        End Property
        """;

    // not part of the sample: proves the evaluators below can still flag a real error, so a clean run means something.
    private const string ControlSource = """
        Attribute VB_Name = "Control"
        Option Explicit

        Sub Bad()
          Dim s As String
          Set s = New Interface
        End Sub
        """;

    private static Uri ModuleUri(string name) => new UriBuilder(WorkspaceRoot) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Parsed(string name, ModuleType moduleType, string source)
        => (ModuleUri(name), moduleType, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}{(moduleType == ModuleType.ClassModule ? ".cls" : ".bas")}"), source));

    private static readonly (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) MyModuleParse = Parsed("MyModule", ModuleType.StdModule, MyModuleSource);
    private static readonly (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) InterfaceParse = Parsed("Interface", ModuleType.ClassModule, InterfaceSource);
    private static readonly (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) ClassParse = Parsed("Class", ModuleType.ClassModule, ClassSource);

    private static WorkspaceComposition Composed(params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] extra)
        => WorkspaceSymbolResolver.ComposeWithScopes(
            WorkspaceRoot, [MyModuleParse, InterfaceParse, ClassParse, .. extra], new IntrinsicSymbolResolver(), projectName: "MyProject");

    private static (ISymbolResolver Resolver, Uri ModuleUri, Uri ProcUri) Compose()
    {
        var procUri = new UriBuilder(WorkspaceRoot) { Fragment = "MyModule.MyProc1" }.Uri;
        return (Composed().Resolver, MyModuleParse.Uri, procUri);
    }

    private static SymbolResolutionResult ResolveValue(string name, Uri from)
        => Compose().Resolver.ResolveValue(name, ScopeKind.Unallocated, from);

    private static SymbolResolutionResult ResolveType(string name, Uri from)
        => Compose().Resolver.ResolveType(name, ScopeKind.Unallocated, from);

    // a member's body, evaluated from its own scope; a property's Get/Set accessors share one scope.
    private static (StaticEvaluationContext Context, StatementBlock Block) BodyOf(
        (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) module, string member, MemberKind kind,
        WorkspaceComposition? composition = null)
    {
        composition ??= Composed();
        var node = module.Parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(candidate => candidate.Name == member && candidate.MemberKind == kind);
        Assert.IsTrue(composition.Value.ScopeTree.TryGetScope(ModuleUri($"{module.Uri.Fragment.TrimStart('#')}.{member}"), out var scope));

        return (new StaticEvaluationContext(composition.Value.Resolver, scope), new StatementBlock([.. node.Children]));
    }

    // Every assignment of a body in source order - its target's and value's declared types - with each
    // With block's target type threaded into its body (MS-VBAL §5.6.15), as one readable line each.
    private static List<string> TypedTrace(StaticEvaluationContext context, StatementBlock block)
    {
        var lines = new List<string>();
        Trace(context, block, lines);
        return lines;
    }

    private static void Trace(StaticEvaluationContext context, StatementBlock block, List<string> lines)
    {
        foreach (var statement in block.Children.OfType<StatementNode>())
        {
            switch (statement)
            {
                case WithStatementNode with:
                    var target = ExpressionStaticSemanticsEvaluator.Evaluate(context, with.WithExpression);
                    Trace(target.IsSuccess ? context with { EnclosingWithTargetType = target.Result } : context, with.Body, lines);
                    break;
                case AssignmentStatementNode assignment:
                    lines.Add($"{Render(assignment.Target)} = {Render(assignment.Value)}  ->  "
                        + $"{Show(ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Target))} := "
                        + $"{Show(ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Value))}");
                    break;
            }
        }
    }

    private static string Render(ExpressionNode? node) => node switch
    {
        null => string.Empty,
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode member => $"{Render(member.Owner)}.{member.Member.IdentifierName}",
        NewExpressionNode created => $"New {Render(created.TypeExpression)}",
        _ => node.GetType().Name,
    };

    private static string Show(StaticSemanticsEvaluationResult result)
        => result.IsSuccess ? result.Result!.Name : $"ERROR {result.ErrorInfo!.VBCompileErrorId}";

    [TestMethod]
    public void FromMyProc1_MyProject_BindsTheLocal_NotTheModuleConst()
    {
        var (resolver, _, procUri) = Compose();

        var result = resolver.ResolveValue("MyProject", ScopeKind.Unallocated, procUri);

        var local = Assert.IsInstanceOfType<VBLocalVariableSymbol>(result.Symbol);
        Assert.AreEqual(procUri, local.ParentUri);
    }

    [TestMethod]
    public void FromModuleScope_MyProject_BindsTheConst_TheTypeIsNotInTheDefaultContext()
        // MS-VBAL 5.6.10: a Const and a Type of one name do not compete - the Type is only a candidate in
        // the type binding context (see the ResolveType cases below).
    {
        var result = ResolveValue("MyProject", Compose().ModuleUri);

        Assert.IsTrue(result.IsResolved);
        Assert.IsInstanceOfType<VBConstantMemberSymbol>(result.Symbol);
    }

    [TestMethod]
    public void FromModuleScope_MyProc_BindsTheField_TheTypeIsNotInTheDefaultContext()
        => Assert.IsInstanceOfType<VBModuleFieldVariableMemberSymbol>(
            ResolveValue("MyProc", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void FromModuleScope_MyModule_BindsTheConst_NotTheModuleNorTheType()
        // the module-level Const is the enclosing-module tier; the module of the same name is a later tier.
        => Assert.IsInstanceOfType<VBConstantMemberSymbol>(
            ResolveValue("MyModule", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void FromModuleScope_MyConst_ResolvesUnambiguously()
        => Assert.IsInstanceOfType<VBConstantMemberSymbol>(
            ResolveValue("MyConst", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void FromGlobalScope_MyModule_ResolvesToTheModuleSymbol()
        => Assert.IsInstanceOfType<VBStandardModuleSymbol>(
            ResolveValue("MyModule", GlobalSymbols.UnresolvedSymbol.Uri).Symbol);

    [TestMethod]
    public void AsTypeContext_FromModuleScope_MyProject_BindsTheType_NotTheConst()
    {
        var result = ResolveType("MyProject", Compose().ModuleUri);

        Assert.IsTrue(result.IsResolved);
        Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(result.Symbol);
    }

    [TestMethod]
    public void AsTypeContext_FromModuleScope_MyProc_BindsTheType_NotTheField()
        => Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            ResolveType("MyProc", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void AsTypeContext_FromModuleScope_MyModule_BindsTheModulesOwnType_NotTheModule()
        // the enclosing module's types are the first tier of the type binding context; the module named
        // MyModule is a later one.
        => Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            ResolveType("MyModule", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void AsTypeContext_FromMyProc1_MyProject_BindsTheType_NotTheLocal()
        // the local Dim MyProject As Interface is never a candidate in the type binding context.
        => Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            ResolveType("MyProject", Compose().ProcUri).Symbol);

    [TestMethod]
    public void AsTypeContext_AConstant_IsNotAType()
        => Assert.IsTrue(ResolveType("MyConst", Compose().ModuleUri).IsUnbound);

    [TestMethod]
    public void AllThreeModules_Parse()
    {
        Assert.IsTrue(MyModuleParse.Parse.IsSuccess);
        Assert.IsTrue(InterfaceParse.Parse.IsSuccess);
        Assert.IsTrue(ClassParse.Parse.IsSuccess);
    }

    [TestMethod]
    public void MyProc1_EveryAssignmentTypesAsMsVbalDerives()
        // Locals: MyProject As Interface; MyModule, MyCircular, o - no As clause, so Variant (5.2.3.1.5). A
        // member access on a Variant is late-bound and is a Variant itself (5.6.12); on Interface it is that
        // interface member's type, which is Interface all the way down. The outer With's target is Interface, the
        // inner With's is Variant, and each `.Member` binds against the innermost.
    {
        var (context, block) = BodyOf(MyModuleParse, "MyProc1", MemberKind.Procedure);

        var trace = TypedTrace(context, block);

        CollectionAssert.AreEqual(new[]
        {
            "MyProject = New Interface  ->  Interface := Interface",
            "MyProject.MyModule = MyProject  ->  Interface := Interface",
            "MyModule = New Interface  ->  Variant := Interface",
            "MyProject.MyModule = MyModule  ->  Interface := Variant",
            "MyProject = New Class  ->  Interface := Class",
            "MyProject.MyModule = New MyProject.Class  ->  Interface := Class",
            "MyProject.MyModule.MyProc = MyProject  ->  Interface := Interface",
            "o = MyProject.MyModule  ->  Variant := Interface",
            "MyModule.MyProc = MyProject.MyModule  ->  Variant := Interface",
            ".MyProc = MyProject.MyModule  ->  Variant := Interface",
            ".MyModule.MyProject.MyModule.MyProject = .MyModule.MyProject  ->  Variant := Variant",
            ".MyProc = .MyModule  ->  Interface := Interface",
        }, trace);
    }

    [TestMethod]
    public void MyProc1_HasNoCompileErrors_NewMyProjectDotClassBindsTheProjectsClass()
        // `New MyProject.Class`, where MyModule declares `Type MyProject`: the type binding context's first tier is the
        // enclosing module's own Type, which a bare `MyProject` would mean - but the qualifier of a qualified name is a
        // namespace, and a Type cannot contain a type (ISymbolResolver.ResolveQualifier), so the qualifier is the
        // project. Legacy Rubberduck (issue comment 3) bound it to the LOCAL variable, and later to the Type; the VBE
        // compiles it, and so does the VB6 compiler. A variable is never a candidate for either.
    {
        var (context, block) = BodyOf(MyModuleParse, "MyProc1", MemberKind.Procedure);

        Assert.IsEmpty(StatementStaticSemanticsEvaluator.Evaluate(context, block));
    }

    [TestMethod]
    public void Interface_EveryPropertyGet_ReturnsTheInterface_AndEverySetTakesOne()
    {
        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            Composed().Resolver.ResolveType("Interface", ScopeKind.Global, StaticSymbol.GlobalUri).Symbol);

        var gets = module.DefaultInterfaceMembers.OfType<VBPropertyGetMemberSymbol>().ToArray();
        var sets = module.DefaultInterfaceMembers.OfType<VBPropertySetMemberSymbol>().ToArray();

        CollectionAssert.AreEquivalent(new[] { "MyProject", "MyModule", "MyProc" }, gets.Select(get => get.Name).ToArray());
        Assert.IsTrue(gets.All(get => get.ResolvedType is VBClassType { Name: "Interface" }));
        Assert.HasCount(3, sets);
        // each Set takes one declared parameter (plus the implicit Me), typed Interface even though its
        // name is that of another member of the interface.
        Assert.IsTrue(sets.All(set => set.Parameters.Single(parameter => parameter.Name != "Me").ResolvedType is VBClassType { Name: "Interface" }));
    }

    [TestMethod]
    public void Class_ImplementsTheInterface()
    {
        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            Composed().Resolver.ResolveType("Class", ScopeKind.Global, StaticSymbol.GlobalUri).Symbol);

        Assert.AreEqual("Interface", module.ImplementedInterfaces.Single().Name);
    }

    [TestMethod]
    public void Class_MyProjectField_IsTypedByTheClassesOwnPrivateType_NotTheStandardModulesOne()
        // `Private MyProject As MyModule`: tier 1 of the type binding context is the enclosing module's own
        // `Private Type MyModule` - not the public `Type MyModule` in the standard module, nor the module MyModule.
    {
        var composition = Composed();
        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            composition.Resolver.ResolveType("Class", ScopeKind.Global, StaticSymbol.GlobalUri).Symbol);

        var field = module.Members.Single(member => member.Name == "MyProject");

        var type = Assert.IsInstanceOfType<VBUserDefinedType>(field.ResolvedType);
        Assert.AreEqual(module.Uri.AbsoluteUri, type.Symbol.ParentUri.AbsoluteUri);

        // the type's own Members is the snapshot from before its fields were bound; a type is read from its
        // declaration, by identity - which is what member access does.
        var declaration = Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            composition.Resolver.ResolveType(type.Symbol.Name, ScopeKind.Global, type.Symbol.ParentUri).Symbol);
        Assert.AreEqual("Interface", declaration.Members.Select(member => member.ResolvedType.Name).Distinct().Single());
    }

    [TestMethod]
    public void Class_PropertyGetBodies_TypeAsMsVbalDerives()
    {
        var (getModuleContext, getModuleBlock) = BodyOf(ClassParse, "Interface_MyModule", MemberKind.PropertyGet);
        var (getProcContext, getProcBlock) = BodyOf(ClassParse, "Interface_MyProc", MemberKind.PropertyGet);

        CollectionAssert.AreEqual(
            new[] { "Interface_MyModule = MyProject.MyModule  ->  Interface := Interface" },
            TypedTrace(getModuleContext, getModuleBlock));
        CollectionAssert.AreEqual(
            new[] { "MyProject.MyModule = MyProject.MyProc  ->  Interface := Interface" },
            TypedTrace(getProcContext, getProcBlock));
    }

    [TestMethod]
    [Ignore("A property's Get/Let/Set accessors share one scope (Symbol.CreateUri keys on the name alone), so the last accessor " +
        "registered decides the scope's parameters: a Set accessor's `MyModule As Interface` parameter is not visible when a Get " +
        "follows it in the module, and the name falls through to the module MyModule. Per-accessor scopes are separate tracked work.")]
    public void Class_PropertySetBodies_SeeTheirOwnParameter()
    {
        var (context, block) = BodyOf(ClassParse, "Interface_MyModule", MemberKind.PropertySet);

        CollectionAssert.AreEqual(
            new[] { "MyProject.MyModule = MyModule  ->  Interface := Interface" },
            TypedTrace(context, block));
    }

    [TestMethod]
    public void Class_SetInterfaceEqualsNewInterface_IsAnUndefinedVariable()
        // The one line of the sample that MS-VBA accepts and MS-VBAL does not (the author imported the sample into a
        // VBE, 2026-09-18: it compiles and runs, yet the VBE cannot go to the definition of the assigned `Interface`,
        // and Rubberduck reports an undeclared variable). Neither class module is predeclared, so no default instance
        // variable has the name (5.2.4.1.2), and no default-context tier holds a class module (5.6.10): under
        // Option Explicit the name is undefined. The spec is right and MS-VBA has a bug; RD-VBA follows the spec.
    {
        var (context, block) = BodyOf(ClassParse, "Interface_MyProject", MemberKind.PropertyGet);

        CollectionAssert.AreEqual(
            new[] { "Interface = New Interface  ->  ERROR VariableNotDefined := Interface" },
            TypedTrace(context, block));

        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, errors[0].VBCompileErrorId);
        Assert.AreEqual("Interface", errors[0].Verbose);
    }

    [TestMethod]
    public void NeitherClassOfTheSample_HasADefaultInstance_SoNeitherNameIsAValue()
    {
        var resolver = Composed().Resolver;

        foreach (var name in new[] { "Interface", "Class" })
        {
            Assert.IsTrue(resolver.ResolveValue(name, ScopeKind.Unallocated, MyModuleParse.Uri).IsUnbound, $"'{name}' from MyModule");
            Assert.IsTrue(resolver.ResolveValue(name, ScopeKind.Unallocated, ModuleUri("Class")).IsUnbound, $"'{name}' from Class");
        }
    }

    [TestMethod]
    public void Control_ARealSetCoercionMismatch_IsStillFlagged()
        // without this, every "no errors" expectation above could be an evaluator that never errors.
    {
        var control = Parsed("Control", ModuleType.StdModule, ControlSource);
        var (context, block) = BodyOf(control, "Bad", MemberKind.Procedure, Composed(control));

        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, errors[0].VBCompileErrorId);
    }
}

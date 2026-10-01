using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.3.1.9</strong>, runtime semantics: "When the target object of an invocation has a declared type
/// that is an interface class of the actual target object's class and the method name is the name of an interface member
/// of that interface class then the actual invoked method is the method defined by the corresponding implemented method
/// declaration of target's object's class."
/// </summary>
/// <remarks>
/// <c>IShape</c> has a public variable <c>Title</c>, a Sub <c>Draw</c> and a Function <c>Area</c>. <c>Disc</c> implements it
/// and also has a public <c>Draw</c> of its own, so that which of the two is run says what the call was dispatched on.
/// A module's <c>Main</c> has <c>s</c> (<c>IShape</c>), <c>d</c> (<c>Disc</c>) and <c>o</c> (<c>Object</c>).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.9 Implemented Name Declarations")]
public sealed class ImplementsRuntimeTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(IEnumerable<Symbol> symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static InstructionList Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var result = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Verbose)));
        return result.InstructionList;
    }

    private static VBParameterSymbol Me(Symbol owner) => new(Root, owner.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);

    private static string[] Run(params string[] body)
    {
        var bodies = new Dictionary<SemanticId, InstructionList>();

        VBProcedureMemberSymbol Sub(VBClassModuleSymbol owner, string name, AccessModifier access, params string[] statements)
        {
            var procedure = new VBProcedureMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);
            procedure = procedure with { Parameters = [Me(procedure)] };
            bodies[procedure.SemanticId] = Lower(statements);
            return procedure;
        }

        VBFunctionMemberSymbol Function(VBClassModuleSymbol owner, string name, AccessModifier access, VBType type, params string[] statements)
        {
            var function = new VBFunctionMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, SymbolKindExt.Function, type, R, R, access);
            function = function with { Parameters = [Me(function)] };
            bodies[function.SemanticId] = Lower(statements);
            return function;
        }

        // the interface: a variable, a Sub and a Function, with nothing to run of their own.
        var shape = new VBClassModuleSymbol(Root, Root, "IShape");
        var title = new VBInstanceFieldVariableMemberSymbol(Root, shape.Uri, "Title", R, R, VBStringType.TypeInfo, AccessModifier.Public);
        var shapeDraw = Sub(shape, "Draw", AccessModifier.Public);
        var shapeArea = Function(shape, "Area", AccessModifier.Public, VBLongType.TypeInfo);
        shape = shape with { Members = [title, shapeDraw, shapeArea], DefaultInterfaceMembers = [title, shapeDraw, shapeArea] };
        var shapeType = VBClassType.FromClassModule(shape);

        // the implementation, and a Draw of its own.
        var disc = new VBClassModuleSymbol(Root, Root, "Disc") { ImplementedInterfaces = [shape] };
        var ownDraw = Sub(disc, "Draw", AccessModifier.Public, "Debug.Print \"own draw\"");
        var implementedDraw = Sub(disc, "IShape_Draw", AccessModifier.Private, "Debug.Print \"interface draw\"");
        var implementedArea = Function(disc, "IShape_Area", AccessModifier.Private, VBLongType.TypeInfo, "IShape_Area = 42");
        var implementedTitle = new VBPropertyGetMemberSymbol(Root, disc.Uri, ScopeKind.Instance, "IShape_Title", R, R, AccessModifier.Private);
        implementedTitle = implementedTitle with { ResolvedType = VBStringType.TypeInfo, Parameters = [Me(implementedTitle)] };
        bodies[implementedTitle.SemanticId] = Lower("IShape_Title = \"round\"");
        disc = disc with
        {
            Members = [ownDraw, implementedDraw, implementedArea, implementedTitle],
            DefaultInterfaceMembers = [ownDraw],
        };
        var discType = VBClassType.FromClassModule(disc);

        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var main = new VBProcedureMemberSymbol(Root, module.Uri, "Main", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        BoundTypedSymbol Local(string name, VBType type) => new VBLocalVariableSymbol(main.Uri, main.Uri, name, ScopeKind.Local, R, R, ResolvedType: type);
        main = main with { Locals = [Local("s", shapeType), Local("d", discType), Local("o", VBObjectType.TypeInfo)] };
        bodies[main.SemanticId] = Lower(body);

        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [],
            [new StdLibSymbolProvider(Root), new Provider([module, shape, disc, title, shapeDraw, shapeArea, ownDraw, implementedDraw, implementedArea, implementedTitle, main])],
            output: output);
        var pipeline = RuntimeExecutionPipeline.Create(session, bodies, Substitute.For<IVerboseMessageBuilder>());

        var outcome = pipeline.Invoker.Invoke(main, session.Symbols.Resolver, []);
        Assert.IsTrue(outcome.IsSuccess, $"{outcome.ErrorInfo?.Description} (internal error: {outcome.IsInternalError})");
        return [.. output.Lines.Select(line => line.Trim())];
    }

    [TestMethod]
    public void ACallThroughTheInterface_RunsTheImplementation_NotTheClassesOwnMethodOfTheSameName()
        => CollectionAssert.AreEqual(new[] { "interface draw" }, Run("Set d = New Disc", "Set s = d", "s.Draw"));

    [TestMethod]
    public void ACallThroughTheClass_RunsTheClassesOwnMethod()
        => CollectionAssert.AreEqual(new[] { "own draw" }, Run("Set d = New Disc", "d.Draw"));

    [TestMethod]
    public void TheSameObject_IsDispatchedByHowTheVariableIsDeclared()
        => CollectionAssert.AreEqual(
            new[] { "own draw", "interface draw", "own draw" },
            Run("Set d = New Disc", "d.Draw", "Set s = d", "s.Draw", "Set o = d", "o.Draw"));

    [TestMethod]
    public void AFunctionOfTheInterface_RunsItsImplementation()
        => CollectionAssert.AreEqual(new[] { "42" }, Run("Set d = New Disc", "Set s = d", "Debug.Print s.Area"));

    [TestMethod]
    public void AVariableOfTheInterface_IsReadThroughItsPropertyGet()
        => CollectionAssert.AreEqual(new[] { "round" }, Run("Set d = New Disc", "Set s = d", "Debug.Print s.Title"));

    [TestMethod]
    public void ACallStatementThroughTheInterface_RunsTheImplementation()
        => CollectionAssert.AreEqual(new[] { "interface draw" }, Run("Set d = New Disc", "Set s = d", "Call s.Draw"));

    [TestMethod]
    public void AnObjectAssignedDirectlyToTheInterfaceVariable_IsDispatchedToo()
        => CollectionAssert.AreEqual(new[] { "interface draw" }, Run("Set s = New Disc", "s.Draw"));
}

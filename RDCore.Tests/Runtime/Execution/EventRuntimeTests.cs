using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
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
/// Events at run time (<strong>MS-VBAL §5.4.2.20</strong>, <strong>§5.4.3.9</strong>): <c>RaiseEvent</c> invokes the
/// procedures that handle the event of whatever object a <c>WithEvents</c> variable holds, in the order the variables
/// were assigned; assigning the variable detaches the handlers from the object it held and attaches them to the one
/// it is given.
/// </summary>
/// <remarks>
/// Two classes. <c>Source</c> declares <c>Event Changed(ByVal Value As Long)</c> and <c>Event Bump(ByRef Count As Long)</c>
/// and raises them from <c>Fire</c> (<c>Changed(7)</c>) and <c>FireBump</c> (<c>Bump</c> of its own <c>Total</c>, then
/// prints what <c>Total</c> is). <c>Sink</c> has a <c>WithEvents Src As Source</c> and a <c>Tag</c>; <c>Attach(Origin,
/// Name)</c> tags it and sets <c>Src</c>, <c>Detach</c> sets <c>Src</c> to <c>Nothing</c>, and the handlers are what
/// each test gives it. <c>Main</c>, which has the variables <c>a</c> and <c>b</c> (<c>Source</c>) and <c>x</c> and <c>y</c>
/// (<c>Sink</c>), runs the statements under test.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.20 RaiseEvent Statement")]
public sealed class EventRuntimeTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(IEnumerable<Symbol> symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private sealed record World(
        IRuntimeSession Session, RuntimeExecutionPipeline Pipeline, RuntimeOutputBuffer Output, VBStandardModuleSymbol Module,
        VBClassModuleSymbol Source, VBClassModuleSymbol Sink, Dictionary<SemanticId, InstructionList> Bodies);

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

    private static VBParameterSymbol Parameter(Symbol owner, string name, ParameterKind kind, VBType type)
        => new(Root, owner.Uri, name, R, R, kind, type);

    private static VBProcedureMemberSymbol Sub(
        VBClassModuleSymbol owner, string name, AccessModifier access, Dictionary<SemanticId, InstructionList> bodies, string[] body,
        params (string Name, ParameterKind Kind, VBType Type)[] parameters)
    {
        var procedure = new VBProcedureMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);
        procedure = procedure with
        {
            Parameters = [Parameter(procedure, "Me", ParameterKind.ImplicitByRef, VBObjectType.TypeInfo),
                .. parameters.Select(p => Parameter(procedure, p.Name, p.Kind, p.Type))],
        };
        bodies[procedure.SemanticId] = Lower(body);
        return procedure;
    }

    private static VBEventMemberSymbol Event(VBClassModuleSymbol owner, string name, ParameterKind kind, string parameter)
    {
        var declared = new VBEventMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, R, R, AccessModifier.Public);
        return declared with { Parameters = [Parameter(declared, parameter, kind, VBLongType.TypeInfo)] };
    }

    // the handlers are what a test gives Sink: a name and a body. A name ending in _Changed takes Changed's parameter,
    // one ending in _Bump takes Bump's, and the rest - Class_Terminate - take none.
    private static World Compose((string Name, string[] Body)[] handlers, string[]? fireBody = null, string[]? attachBody = null)
    {
        var bodies = new Dictionary<SemanticId, InstructionList>();

        var source = new VBClassModuleSymbol(Root, Root, "Source");
        var changed = Event(source, "Changed", ParameterKind.ExplicitByVal, "Value");
        var bump = Event(source, "Bump", ParameterKind.ExplicitByRef, "Count");
        var fire = Sub(source, "Fire", AccessModifier.Public, bodies, fireBody ?? ["RaiseEvent Changed(7)"]);
        var fireBump = Sub(source, "FireBump", AccessModifier.Public, bodies, ["Total = 1", "RaiseEvent Bump(Total)", "Debug.Print \"total \" & Total"]);
        var total = new VBInstanceFieldVariableMemberSymbol(Root, source.Uri, "Total", R, R, VBLongType.TypeInfo, AccessModifier.Public);
        source = source with { Members = [changed, bump, fire, fireBump, total], DefaultInterfaceMembers = [fire, fireBump, total] };
        var sourceType = VBClassType.FromClassModule(source);

        var sink = new VBClassModuleSymbol(Root, Root, "Sink");
        var src = (VBInstanceFieldVariableMemberSymbol)new VBInstanceFieldVariableMemberSymbol(
            Root, sink.Uri, "Src", R, R, sourceType, AccessModifier.Private).With(SymbolProperties.WithEvents, true);
        var tag = new VBInstanceFieldVariableMemberSymbol(Root, sink.Uri, "Tag", R, R, VBStringType.TypeInfo, AccessModifier.Public);
        var attach = Sub(sink, "Attach", AccessModifier.Public, bodies, attachBody ?? ["Tag = Name", "Set Src = Origin"],
            ("Origin", ParameterKind.ExplicitByVal, sourceType), ("Name", ParameterKind.ExplicitByVal, VBStringType.TypeInfo));
        var detach = Sub(sink, "Detach", AccessModifier.Public, bodies, ["Set Src = Nothing"]);
        var handled = handlers.Select(handler => Sub(
            sink, handler.Name, AccessModifier.Private, bodies, handler.Body,
            handler.Name.EndsWith("_Bump", StringComparison.Ordinal) ? [("Count", ParameterKind.ExplicitByRef, VBLongType.TypeInfo)]
            : handler.Name.EndsWith("_Changed", StringComparison.Ordinal) ? [("Value", ParameterKind.ExplicitByVal, VBLongType.TypeInfo)]
            : [])).ToArray();
        sink = sink with { Members = [src, tag, attach, detach, .. handled], DefaultInterfaceMembers = [src, tag, attach, detach] };

        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [],
            [new StdLibSymbolProvider(Root), new Provider([module, source, sink, changed, bump, fire, fireBump, total, src, tag, attach, detach, .. handled])],
            output: output);
        var pipeline = RuntimeExecutionPipeline.Create(session, bodies, Substitute.For<IVerboseMessageBuilder>());
        return new World(session, pipeline, output, module, source, sink, bodies);
    }

    // a procedure of the module with the variables a and b (Source) and x and y (Sink), run as a call would be.
    private static RuntimeSemanticsEvaluationResult Main(World world, params string[] body)
    {
        var main = new VBProcedureMemberSymbol(
            Root, world.Module.Uri, "Main", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        BoundTypedSymbol Local(string name, VBClassModuleSymbol type)
            => new VBLocalVariableSymbol(main.Uri, main.Uri, name, ScopeKind.Local, R, R, ResolvedType: VBClassType.FromClassModule(type));
        main = main with { Locals = [Local("a", world.Source), Local("b", world.Source), Local("x", world.Sink), Local("y", world.Sink)] };
        world.Bodies[main.SemanticId] = Lower(body);
        world.Session.Symbols.TryDefine(main, ScopeKind.Module);

        return world.Pipeline.Invoker.Invoke(main, world.Session.Symbols.Resolver, []);
    }

    private static string[] Printed(World world) => [.. world.Output.Lines.Select(line => line.Trim())];

    private static void AssertRan(RuntimeSemanticsEvaluationResult outcome)
        => Assert.IsTrue(outcome.IsSuccess, $"{outcome.ErrorInfo?.Description} (internal error: {outcome.IsInternalError})");

    private static readonly (string, string[]) ChangedHandler = ("Src_Changed", ["Debug.Print Tag & \" got \" & Value"]);

    [TestMethod]
    public void RaiseEvent_InvokesTheHandlerOfAnAttachedWithEventsVariable_WithTheArguments()
    {
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "x.Attach a, \"x\"", "a.Fire"));

        CollectionAssert.AreEqual(new[] { "x got 7" }, Printed(world));
    }

    [TestMethod]
    public void RaiseEvent_WithNothingAttached_DoesNothing()
    {
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "a.Fire", "Debug.Print \"fired\""));

        CollectionAssert.AreEqual(new[] { "fired" }, Printed(world));
    }

    [TestMethod]
    public void AnAttachedSinkWithNoHandlerForTheEvent_IsSkipped()
    {
        var world = Compose([("Src_Bump", ["Count = Count + 1"])]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "x.Attach a, \"x\"", "a.Fire", "Debug.Print \"fired\""));

        CollectionAssert.AreEqual(new[] { "fired" }, Printed(world));
    }

    [TestMethod]
    public void Handlers_RunInTheOrderTheirVariablesWereAssigned()
    {
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "Set y = New Sink", "x.Attach a, \"x\"", "y.Attach a, \"y\"", "a.Fire"));

        CollectionAssert.AreEqual(new[] { "x got 7", "y got 7" }, Printed(world));
    }

    [TestMethod]
    public void AssigningTheVariableAgain_MovesItsHandlersToTheEndOfTheOrder()
    {
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "Set y = New Sink",
            "x.Attach a, \"x\"", "y.Attach a, \"y\"", "x.Attach a, \"x\"", "a.Fire"));

        CollectionAssert.AreEqual(new[] { "y got 7", "x got 7" }, Printed(world));
    }

    [TestMethod]
    public void AWithEventsVariable_SetThroughAMemberAccess_AttachesTheHandlersOfTheObjectItIsAVariableOf()
    {
        // `Set x.Src = a` assigns a variable of x, from code that is not x's: the handlers are x's, not Main's.
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "x.Tag = \"x\"", "Set x.Src = a", "a.Fire", "Set x.Src = Nothing", "a.Fire"));

        CollectionAssert.AreEqual(new[] { "x got 7" }, Printed(world));
    }

    [TestMethod]
    public void SettingTheVariableToNothing_DetachesItsHandlers()
    {
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "x.Attach a, \"x\"", "a.Fire", "x.Detach", "a.Fire", "Debug.Print \"end\""));

        CollectionAssert.AreEqual(new[] { "x got 7", "end" }, Printed(world));
    }

    [TestMethod]
    public void SettingTheVariableToAnotherObject_MovesItsHandlersToThatObject()
    {
        var world = Compose([ChangedHandler]);

        AssertRan(Main(world, "Set a = New Source", "Set b = New Source", "Set x = New Sink",
            "x.Attach a, \"x\"", "x.Attach b, \"x\"", "a.Fire", "Debug.Print \"a fired\"", "b.Fire"));

        CollectionAssert.AreEqual(new[] { "a fired", "x got 7" }, Printed(world));
    }

    [TestMethod]
    public void AByRefEventParameter_IsTheArgumentVariable_EachHandlerSeeingWhatTheLastLeft()
    {
        // MS-VBAL §5.4.2.20: the next invocation's argument is what the parameter last contained, which is also what the
        // raiser finds in its variable afterwards.
        var world = Compose([("Src_Bump", ["Count = Count + 10", "Debug.Print Tag & \" count \" & Count"])]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "Set y = New Sink", "x.Attach a, \"x\"", "y.Attach a, \"y\"", "a.FireBump"));

        CollectionAssert.AreEqual(new[] { "x count 11", "y count 21", "total 21" }, Printed(world));
    }

    [TestMethod]
    public void AByRefEventParameter_GivenAValueNotAVariable_StillChainsFromHandlerToHandler()
    {
        // `RaiseEvent Bump(5)` has no variable to leave a value in, but the second handler still starts with what the
        // first left in the parameter (MS-VBAL §5.4.2.20).
        var world = Compose([("Src_Bump", ["Count = Count + 10", "Debug.Print Tag & \" count \" & Count"])], fireBody: ["RaiseEvent Bump(5)"]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "Set y = New Sink", "x.Attach a, \"x\"", "y.Attach a, \"y\"", "a.Fire"));

        CollectionAssert.AreEqual(new[] { "x count 15", "y count 25" }, Printed(world));
    }

    [TestMethod]
    public void TheTemporaryAByRefValueIsGivenIsFreed_SoRaisingAgainStartsFromTheArgumentAgain()
    {
        var world = Compose([("Src_Bump", ["Count = Count + 10", "Debug.Print Tag & \" count \" & Count"])], fireBody: ["RaiseEvent Bump(5)", "RaiseEvent Bump(5)"]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "x.Attach a, \"x\"", "a.Fire"));

        CollectionAssert.AreEqual(new[] { "x count 15", "x count 15" }, Printed(world));
    }

    [TestMethod]
    public void AnErrorAHandlerLeavesUnhandled_IsTheErrorOfRaiseEvent_AndStopsTheLaterHandlers()
    {
        var world = Compose([("Src_Changed", ["Debug.Print Tag & \" before\"", "Debug.Print 1 / 0"])]);

        var outcome = Main(world, "Set a = New Source", "Set x = New Sink", "Set y = New Sink", "x.Attach a, \"x\"", "y.Attach a, \"y\"", "a.Fire");

        Assert.IsTrue(outcome.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, outcome.ErrorInfo!.ErrorId);
        CollectionAssert.AreEqual(new[] { "x before" }, Printed(world));
    }

    [TestMethod]
    public void ADestroyedSink_HandlesNoMoreEvents()
    {
        // the sink is held by x only: setting x to Nothing destroys it, and its handlers go with it.
        var world = Compose([ChangedHandler, ("Class_Terminate", ["Debug.Print Tag & \" gone\""])]);

        AssertRan(Main(world, "Set a = New Source", "Set x = New Sink", "x.Attach a, \"x\"", "Set x = Nothing", "a.Fire", "Debug.Print \"end\""));

        CollectionAssert.AreEqual(new[] { "x gone", "end" }, Printed(world));
    }
}

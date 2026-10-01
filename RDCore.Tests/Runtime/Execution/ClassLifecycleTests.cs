using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A class's lifecycle (<strong>MS-VBAL §5.3.1.10</strong>): <c>Class_Initialize</c> runs when an instance is created,
/// <c>Class_Terminate</c> when it loses its last reference — both dispatched through the interface every class
/// implicitly implements, never called by name.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.10 Class Module Lifecycle")]
public sealed class ClassLifecycleTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(IEnumerable<Symbol> symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private sealed record World(
        IRuntimeSession Session, RuntimeExecutionPipeline Pipeline, RuntimeOutputBuffer Output, Uri ModuleUri, VBClassModuleSymbol Widget,
        Dictionary<SemanticId, InstructionList> Bodies);

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

    // a class Widget whose members are the given procedures, each running the given source: the name is whatever the
    // test calls it, so that a procedure which only looks like a handler can be told from one. A name with an
    // underscore is a handler's, Private as one is written; any other is a Public member of the class.
    private static World Compose(params (string Name, string[] Body)[] handlers)
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var bodies = new Dictionary<SemanticId, InstructionList>();
        var members = new List<VBTypeMemberSymbol>();

        foreach (var (name, body) in handlers)
        {
            var handler = new VBProcedureMemberSymbol(
                Root, widget.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R,
                name.Contains('_') ? AccessModifier.Private : AccessModifier.Public);
            handler = handler with
            {
                Parameters = [new VBParameterSymbol(Root, handler.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo)],
            };
            bodies[handler.SemanticId] = Lower(body);
            members.Add(handler);
        }

        widget = widget with
        {
            Members = [.. members],
            DefaultInterfaceMembers = [.. members.Where(member => member.AccessModifier is not AccessModifier.Private)],
        };
        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [],
            [new StdLibSymbolProvider(Root), new Provider([module, widget, .. members])], output: output);
        var pipeline = RuntimeExecutionPipeline.Create(session, bodies, Substitute.For<IVerboseMessageBuilder>());
        return new World(session, pipeline, output, module.Uri, widget, bodies);
    }

    // a procedure of the module, run through the invoker as a call would: its locals are hoisted, and let go of when it
    // returns. A local named Thing is a variable of the class Widget, declared As New when asked to be.
    private static RuntimeSemanticsEvaluationResult RunMain(World world, bool thingIsAsNew, params string[] body)
    {
        var main = new VBProcedureMemberSymbol(
            Root, world.ModuleUri, "Main", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        Symbol thing = new VBLocalVariableSymbol(
            main.Uri, main.Uri, "Thing", ScopeKind.Local, R, R, ResolvedType: VBClassType.FromClassModule(world.Widget));
        thing = thingIsAsNew ? thing.With(SymbolProperties.AutoInstantiated, true) : thing;
        main = main with { Locals = [(BoundTypedSymbol)thing] };
        world.Bodies[main.SemanticId] = Lower(body);
        // the local rides on its procedure, which is what makes it resolvable: defining it too would define it twice.
        world.Session.Symbols.TryDefine(main, ScopeKind.Module);

        return world.Pipeline.Invoker.Invoke(main, world.Session.Symbols.Resolver, []);
    }

    private static RuntimeSemanticsEvaluationResult New(World world)
    {
        var id = new SyntaxNodeId(world.ModuleUri.AbsolutePath, [1]);
        var type = new SimpleNameExpressionNode(new SyntaxNodeId(world.ModuleUri.AbsolutePath, [2]), TestLocations.TestLocation, "Widget");
        return world.Pipeline.Expressions.Evaluate(
            world.Session, new NewExpressionNode(id, TestLocations.TestLocation, type), new RuntimeEvaluationContext(world.ModuleUri));
    }

    private static string[] Printed(World world) => [.. world.Output.Lines.Select(line => line.Trim())];

    private static IBindingHandle Handle() => Substitute.For<IBindingHandle>();

    #region Initialize

    [TestMethod]
    public void New_RaisesInitialize_OnTheNewObject()
    {
        var world = Compose(("Class_Initialize", ["Debug.Print \"init\""]));

        var created = New(world);

        Assert.IsTrue(created.IsSuccess);
        CollectionAssert.AreEqual(new[] { "init" }, Printed(world));
    }

    [TestMethod]
    public void New_ReturnsTheObject_TheHandlerRanOn()
    {
        var world = Compose(("Class_Initialize", ["Debug.Print \"init\""]));

        var created = New(world);

        var instance = Assert.IsInstanceOfType<VBObjectValue>(created.Result);
        Assert.IsTrue(world.Session.Symbols.TryGetInstance(instance.Value, out _));
    }

    [TestMethod]
    public void EachNew_RaisesInitializeAgain()
    {
        var world = Compose(("Class_Initialize", ["Debug.Print \"init\""]));

        New(world);
        New(world);

        CollectionAssert.AreEqual(new[] { "init", "init" }, Printed(world));
    }

    [TestMethod]
    public void AClassWithoutAHandler_IsCreatedAndNothingRuns()
    {
        var world = Compose();

        Assert.IsTrue(New(world).IsSuccess);
        Assert.IsEmpty(Printed(world));
    }

    [TestMethod]
    public void AProcedureThatOnlyLooksLikeAHandler_IsNotOne()
    {
        // the handler is the member of the interface the class implements, which is called Class: nothing else is.
        var world = Compose(("Initialize", ["Debug.Print \"no\""]), ("Widget_Initialize", ["Debug.Print \"no\""]));

        Assert.IsTrue(New(world).IsSuccess);
        Assert.IsEmpty(Printed(world));
    }

    [TestMethod]
    public void AnErrorTheInitializeHandlerLeavesUnhandled_IsTheErrorOfNew()
    {
        var world = Compose(("Class_Initialize", ["Debug.Print 1 / 0"]));

        var created = New(world);

        Assert.IsTrue(created.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, created.ErrorInfo!.ErrorId);
    }

    #endregion

    #region Terminate

    [TestMethod]
    public void ReleasingTheLastReference_RaisesTerminate_ThenDestroysTheObject()
    {
        var world = Compose(("Class_Terminate", ["Debug.Print \"term\""]));
        var id = Assert.IsInstanceOfType<VBObjectValue>(New(world).Result).Value;
        var holder = Handle();
        world.Session.Objects.AddRef(id, holder);

        var destroyed = world.Session.ReleaseReference(id, holder);

        Assert.IsTrue(destroyed);
        CollectionAssert.AreEqual(new[] { "term" }, Printed(world));
        Assert.IsFalse(world.Session.Symbols.TryGetInstance(id, out _));
    }

    [TestMethod]
    public void ReleasingAReferenceThatIsNotTheLast_RaisesNothing()
    {
        var world = Compose(("Class_Terminate", ["Debug.Print \"term\""]));
        var id = Assert.IsInstanceOfType<VBObjectValue>(New(world).Result).Value;
        var first = Handle();
        var second = Handle();
        world.Session.Objects.AddRef(id, first);
        world.Session.Objects.AddRef(id, second);

        Assert.IsFalse(world.Session.ReleaseReference(id, first));

        Assert.IsEmpty(Printed(world));
        Assert.IsTrue(world.Session.Symbols.TryGetInstance(id, out _));

        Assert.IsTrue(world.Session.ReleaseReference(id, second));
        CollectionAssert.AreEqual(new[] { "term" }, Printed(world));
    }

    [TestMethod]
    public void AClassWithOnlyInitialize_IsDestroyedQuietly()
    {
        var world = Compose(("Class_Initialize", ["Debug.Print \"init\""]));
        var id = Assert.IsInstanceOfType<VBObjectValue>(New(world).Result).Value;
        var holder = Handle();
        world.Session.Objects.AddRef(id, holder);

        Assert.IsTrue(world.Session.ReleaseReference(id, holder));
        CollectionAssert.AreEqual(new[] { "init" }, Printed(world));
    }

    [TestMethod]
    public void AnObjectTerminateGivesAReferenceAgain_IsNotDestroyed_AndIsNotTerminatedTwice()
    {
        // MS-VBAL §5.3.1.10: the handler can make the object reachable again; it is then not destroyed, and when it
        // loses its last reference once more the handler does not run a second time.
        var world = Compose();
        var id = world.Session.Objects.CreateObject();
        world.Session.Symbols.CreateInstance(id, world.Widget);
        var resurrecting = Handle();
        var holder = Handle();
        world.Session.Objects.AddRef(id, holder);

        var lifecycle = Substitute.For<IObjectLifecycle>();
        lifecycle.Terminate(id).Returns(_ =>
        {
            world.Session.Objects.AddRef(id, resurrecting);
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        });
        world.Session.Lifecycle = lifecycle;

        Assert.IsFalse(world.Session.ReleaseReference(id, holder));
        Assert.IsTrue(world.Session.Symbols.TryGetInstance(id, out _), "an object that is reachable again is not destroyed");

        Assert.IsTrue(world.Session.ReleaseReference(id, resurrecting));
        lifecycle.Received(1).Terminate(id);
    }

    [TestMethod]
    public void ASessionNothingCanRunCodeIn_DestroysWithoutRaisingAnything()
    {
        var world = Compose(("Class_Terminate", ["Debug.Print \"term\""]));
        var id = Assert.IsInstanceOfType<VBObjectValue>(New(world).Result).Value;
        var holder = Handle();
        world.Session.Objects.AddRef(id, holder);
        world.Session.Lifecycle = null;

        Assert.IsTrue(world.Session.ReleaseReference(id, holder));
        Assert.IsEmpty(Printed(world));
    }

    #endregion

    #region What a program does to a variable

    private static readonly (string, string[])[] Handlers =
    [
        ("Class_Initialize", ["Debug.Print \"init\""]),
        ("Class_Terminate", ["Debug.Print \"term\""]),
        ("Hello", ["Debug.Print \"hello\""]),
    ];

    [TestMethod]
    public void SettingTheOnlyVariableToNothing_RaisesTerminate()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: false, "Set Thing = New Widget", "Debug.Print \"between\"", "Set Thing = Nothing", "Debug.Print \"after\"");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "init", "between", "term", "after" }, Printed(world));
    }

    [TestMethod]
    public void TheEndOfTheProcedure_ReleasesWhatItsVariablesHold()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: false, "Set Thing = New Widget", "Debug.Print \"last\"");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "init", "last", "term" }, Printed(world));
    }

    [TestMethod]
    public void SettingAVariableToAnotherObject_ReleasesTheOneItHeld()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: false, "Set Thing = New Widget", "Set Thing = New Widget");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        // the second is created before the first is let go of, and each is terminated once.
        CollectionAssert.AreEqual(new[] { "init", "init", "term", "term" }, Printed(world));
    }

    [TestMethod]
    public void SettingAVariableToTheObjectItAlreadyHolds_ReleasesNothing()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: false, "Set Thing = New Widget", "Set Thing = Thing", "Debug.Print \"kept\"");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "init", "kept", "term" }, Printed(world));
    }

    #endregion

    #region Automatic instantiation (MS-VBAL §5.2.3.1.4)

    [TestMethod]
    public void AnAsNewVariable_CreatesItsObjectWhenFirstReferred_NotWhenDeclared()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: true, "Debug.Print \"declared\"", "Thing.Hello");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "declared", "init", "hello", "term" }, Printed(world));
    }

    [TestMethod]
    public void AnAsNewVariable_IsNotCreatedAgainWhileItHoldsAnObject()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: true, "Thing.Hello", "Thing.Hello");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "init", "hello", "hello", "term" }, Printed(world));
    }

    [TestMethod]
    public void AnAsNewVariableSetToNothing_CreatesANewObjectTheNextTimeItIsReferred()
    {
        // a member call on a Nothing reference is error 91, and does not happen here: the reference is what creates
        // the object it is about to call a member of.
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: true, "Thing.Hello", "Set Thing = Nothing", "Debug.Print \"gone\"", "Thing.Hello");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "init", "hello", "term", "gone", "init", "hello", "term" }, Printed(world));
    }

    [TestMethod]
    public void AVariableNotDeclaredAsNew_SetToNothing_IsError91OnAMemberCall()
    {
        var world = Compose(Handlers);

        var outcome = RunMain(world, thingIsAsNew: false, "Set Thing = New Widget", "Set Thing = Nothing", "Thing.Hello");

        Assert.IsTrue(outcome.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, outcome.ErrorInfo!.ErrorId);
    }

    #endregion

    #region Default instances (MS-VBAL §5.2.4.1.2)

    [TestMethod]
    public void ThePredeclaredInstance_IsCreatedWhenTheClassNameIsFirstReferred_AndKept()
    {
        var world = Compose(Handlers);
        world.Session.Symbols.TryDefine(new VBPredeclaredInstanceSymbol(world.Widget), ScopeKind.Global);

        var outcome = RunMain(world, thingIsAsNew: false, "Debug.Print \"start\"", "Widget.Hello", "Widget.Hello");

        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        CollectionAssert.AreEqual(new[] { "start", "init", "hello", "hello" }, Printed(world));
    }

    #endregion
}

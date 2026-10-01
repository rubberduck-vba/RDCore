using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// The static semantics of <c>RaiseEvent</c> (<strong>MS-VBAL §5.4.2.20</strong>): it is written where a class module
/// declares the event it names, and its arguments are compatible with the event's parameter list.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.20 RaiseEvent Statement")]
public sealed class RaiseEventStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, identifier);

    private static LiteralExpressionNode LongOf(int value)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, new VBLongValue(value));

    private static KeywordStatementNode RaiseOf(string eventName, params ExpressionNode[] arguments)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [3]), TestLocations.TestLocation, Tokens.RaiseEvent,
            [NameOf(eventName), .. arguments]);

    // a class module Widget with Event Changed(ByVal Value As Long) and Event Closed(), or a standard module that has no events.
    private static (StaticEvaluationContext Context, Symbol Module) Compose(bool classModule, bool optionExplicit = false)
    {
        Symbol module = classModule ? new VBClassModuleSymbol(Root, Root, "Widget") : new VBStandardModuleSymbol(Root, Root, "Widget");
        if (optionExplicit)
        {
            module = ((VBModuleSymbol)module) with { Directives = new ModuleDirectives(Explicit: true) };
        }

        var changed = new VBEventMemberSymbol(Root, module.Uri, "Changed", ScopeKind.Instance, R, R, AccessModifier.Public);
        changed = changed with { Parameters = [new VBParameterSymbol(Root, changed.Uri, "Value", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo)] };
        var closed = new VBEventMemberSymbol(Root, module.Uri, "Closed", ScopeKind.Instance, R, R, AccessModifier.Public);

        // events with one parameter each, of the kind the compatibility rules tell apart, and a variable of each type.
        var gadget = new VBClassModuleSymbol(Root, Root, "Gadget");
        var gadgetType = VBClassType.FromClassModule(gadget);
        VBEventMemberSymbol WithParameter(string name, ParameterKind kind, VBType type)
        {
            var declared = new VBEventMemberSymbol(Root, module.Uri, name, ScopeKind.Instance, R, R, AccessModifier.Public);
            return declared with { Parameters = [new VBParameterSymbol(Root, declared.Uri, "P", R, R, kind, type)] };
        }

        VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
            => new(Root, module.Uri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

        Symbol[] events =
        [
            changed, closed,
            WithParameter("Bump", ParameterKind.ExplicitByRef, VBLongType.TypeInfo),
            WithParameter("TextOf", ParameterKind.ExplicitByVal, VBStringType.TypeInfo),
            WithParameter("ShapeOf", ParameterKind.ExplicitByVal, gadgetType),
            WithParameter("ShapeRef", ParameterKind.ExplicitByRef, gadgetType),
            WithParameter("ObjectRef", ParameterKind.ExplicitByRef, VBObjectType.TypeInfo),
            WithParameter("AnyRef", ParameterKind.ExplicitByRef, VBVariantType.TypeInfo),
        ];
        Symbol[] variables =
        [
            Variable("anInteger", VBIntegerType.TypeInfo), Variable("aLong", VBLongType.TypeInfo), Variable("aVariant", VBVariantType.TypeInfo),
            Variable("anObject", VBObjectType.TypeInfo), Variable("aGadget", gadgetType),
        ];

        Symbol[] symbols = classModule ? [module, gadget, .. events, .. variables] : [module];
        var tree = ScopeTreeBuilder.Build(symbols);
        return (new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(module.Uri)), module);
    }

    private static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, params StatementNode[] statements)
        => StatementStaticSemanticsEvaluator.Evaluate(context, new StatementBlock([.. statements]));

    [TestMethod]
    public void RaisingADeclaredEvent_WithItsArguments_IsValid()
        => Assert.IsEmpty(Evaluate(Compose(classModule: true).Context, RaiseOf("Changed", LongOf(1)), RaiseOf("Closed")));

    [TestMethod]
    public void TheEventNameIsNotAVariable_SoItIsNotUndefinedUnderOptionExplicit()
        => Assert.IsEmpty(Evaluate(Compose(classModule: true, optionExplicit: true).Context, RaiseOf("Closed")));

    [TestMethod]
    public void RaisingAnEventTheClassDoesNotDeclare_IsEventNotDefined()
    {
        var errors = Evaluate(Compose(classModule: true).Context, RaiseOf("Opened"));

        Assert.AreEqual(VBCompileErrorId.EventNotDefined, errors.Single().VBCompileErrorId);
    }

    [TestMethod]
    public void RaiseEventInAStandardModule_IsEventNotDefined()
    {
        // a standard module declares no events, so there is no event for the statement to name.
        var errors = Evaluate(Compose(classModule: false).Context, RaiseOf("Changed", LongOf(1)));

        Assert.AreEqual(VBCompileErrorId.EventNotDefined, errors.Single().VBCompileErrorId);
    }

    [TestMethod]
    public void TooFewArguments_AreIncompatible()
        => Assert.AreEqual(VBCompileErrorId.EventArgumentsIncompatible, Evaluate(Compose(classModule: true).Context, RaiseOf("Changed")).Single().VBCompileErrorId);

    [TestMethod]
    public void TooManyArguments_AreIncompatible()
        => Assert.AreEqual(VBCompileErrorId.EventArgumentsIncompatible, Evaluate(Compose(classModule: true).Context, RaiseOf("Closed", LongOf(1))).Single().VBCompileErrorId);

    [TestMethod]
    public void AnArgumentIsAnExpressionLikeAnyOther_AndAnUndefinedNameInItIsReported()
    {
        var errors = Evaluate(Compose(classModule: true, optionExplicit: true).Context, RaiseOf("Changed", NameOf("Missing")));

        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, errors.Single().VBCompileErrorId);
    }

    #region Compatibility of the arguments (MS-VBAL §5.3.1.11)

    private static void AssertIncompatible(ImmutableArray<VBCompileErrorInfo> errors)
        => Assert.AreEqual(VBCompileErrorId.EventArgumentsIncompatible, errors.Single().VBCompileErrorId, string.Join("; ", errors.Select(error => error.Verbose)));

    private static ImmutableArray<VBCompileErrorInfo> Raise(string eventName, ExpressionNode argument)
        => Evaluate(Compose(classModule: true).Context, RaiseOf(eventName, argument));

    [TestMethod]
    public void AByRefParameter_TakesAVariableOfExactlyItsType()
        => Assert.IsEmpty(Raise("Bump", NameOf("aLong")));

    [TestMethod]
    public void AByRefParameter_DoesNotTakeAVariableOfAnotherType()
        => AssertIncompatible(Raise("Bump", NameOf("anInteger")));

    [TestMethod]
    public void AByRefLongParameter_TakesAnIntegerLiteral()
    {
        // an Integer value, not a Long one: it is let-coerced to the parameter's type, which is not an error.
        var integerLiteral = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [9]), TestLocations.TestLocation, new VBIntegerValue(5));

        Assert.IsEmpty(Raise("Bump", integerLiteral));
    }

    [TestMethod]
    public void AByValLongParameter_TakesAnIntegerLiteral()
    {
        var integerLiteral = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [9]), TestLocations.TestLocation, new VBIntegerValue(5));

        Assert.IsEmpty(Raise("Changed", integerLiteral));
    }

    [TestMethod]
    public void AByRefParameter_TakesAValueOfItsType_ItBeingPassedAsANewLocal()
        // §5.3.1.11 runtime semantics: an argument that is a value, not a variable, is Let-assigned to a local.
        => Assert.IsEmpty(Raise("Bump", LongOf(5)));

    [TestMethod]
    public void AByRefVariantParameter_TakesAVariableOfAnyType()
        => Assert.IsEmpty(Raise("AnyRef", NameOf("anInteger")));

    [TestMethod]
    public void AByValParameter_TakesAnArgumentLetCoercibleToItsType()
        => Assert.IsEmpty(Raise("TextOf", NameOf("aLong")));

    [TestMethod]
    public void AClassParameter_TakesAnObjectOfAClass()
        => Assert.IsEmpty(Raise("ShapeOf", NameOf("aGadget")));

    [TestMethod]
    public void AClassParameter_TakesAnObject_AndAByValOneAVariant()
    {
        Assert.IsEmpty(Raise("ShapeOf", NameOf("anObject")));
        Assert.IsEmpty(Raise("ShapeOf", NameOf("aVariant")));
    }

    [TestMethod]
    public void AClassParameter_DoesNotTakeAnArgumentThatIsNotAnObject()
        => AssertIncompatible(Raise("ShapeOf", NameOf("aLong")));

    [TestMethod]
    public void AByRefClassParameter_DoesNotTakeAVariant()
        => AssertIncompatible(Raise("ShapeRef", NameOf("aVariant")));

    [TestMethod]
    public void AByRefObjectParameter_TakesAClassObject_AndNotALong()
    {
        Assert.IsEmpty(Raise("ObjectRef", NameOf("aGadget")));
        AssertIncompatible(Raise("ObjectRef", NameOf("aLong")));
    }

    [TestMethod]
    public void AnIncompatibleArgument_IsReportedWhereItIsWritten_AndNamesTheParameter()
    {
        var error = Raise("Bump", NameOf("anInteger")).Single();

        StringAssert.Contains(error.Verbose, "parameter 'P'");
        StringAssert.Contains(error.Verbose, "Argument 1");
    }

    #endregion

    [TestMethod]
    public void RaiseEventNestedInABlock_IsChecked()
    {
        var nested = new DoLoopStatementNode(new(TestUri.TestModuleUri().AbsolutePath, [5]), TestLocations.TestLocation, new StatementBlock([RaiseOf("Opened")]));

        Assert.AreEqual(VBCompileErrorId.EventNotDefined, Evaluate(Compose(classModule: true).Context, nested).Single().VBCompileErrorId);
    }
}

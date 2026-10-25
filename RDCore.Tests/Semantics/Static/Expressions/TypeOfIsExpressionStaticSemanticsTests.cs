using NSubstitute;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.Tests.Semantics.Static.Expressions;

[TestClass]
public sealed class TypeOfIsExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly StaticEvaluationContext Context = new(
        ResolverOfWidget(), new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []));

    // a resolver that knows one type, the class Widget.
    private static ISymbolResolver ResolverOfWidget()
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.ResolveType("Widget", Arg.Any<ScopeKind>(), Arg.Any<Uri>())
            .Returns(SymbolResolutionResult.Resolved(new VBClassModuleSymbol(Root, Root, "Widget")));
        return resolver;
    }

    private static TypeOfIsExpressionNode TypeOfIs(ExpressionNode typeExpression)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [71]), TestLocations.TestLocation, NameOf("o"), typeExpression);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [70]), TestLocations.TestLocation, identifier);

    private static TypeOfIsExpressionNode TypeOfIsOf(string operandName, string typeName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [71]), TestLocations.TestLocation, NameOf(operandName), NameOf(typeName));

    private static VBClassType Class(string name)
        => new(new VBClassModuleSymbol(Root, Root, name), []);

    private static VBUserDefinedType Udt(string name)
        => new(new VBUserDefinedTypeMemberSymbol(Root, Root, name, ScopeKind.Module, R, R, AccessModifier.Public), []);

    [TestMethod]
    public void AClassOperand_ResolvesToBoolean()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("obj", "Widget"), Class("Widget"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AUdtOperand_ResolvesToBoolean()
        // MS-VBAL 5.6.7 explicitly allows "a specific UDT", unlike dictionary access.
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("point", "Widget"), Udt("TPoint"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnObjectOperand_ResolvesToBoolean()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("o", "Widget"), VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AVariantOperand_ResolvesToBoolean()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("v", "Widget"), VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnUnknownOperand_ResolvesToBoolean_DeferredNotAnError()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("whatever", "Widget"), VBUnknownType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnIntrinsicValueTypeOperand_IsATypeMismatchError()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("n", "Widget"), VBLongType.TypeInfo);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AnArrayOperand_IsATypeMismatchError()
    {
        var array = new VBFixedSizeArrayType(VBLongType.TypeInfo);

        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("data", "Widget"), array);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    [DataRow("Object")]
    [DataRow("object")]
    public void ObjectAsTheType_IsValid(string name)
        => Assert.IsTrue(TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("o", name), VBObjectType.TypeInfo).IsSuccess);

    [TestMethod]
    [DataRow("Date")]
    [DataRow("Decimal")]
    // MS-VBA, Excel-verified (oracle\s29 T34, T40): an intrinsic type no object is, read as a name, is a syntax error.
    public void AnIntrinsicTypeReadAsAName_IsASyntaxError(string name)
        => Assert.AreEqual(VBCompileErrorId.SyntaxError,
            TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("o", name), VBObjectType.TypeInfo).ErrorInfo?.VBCompileErrorId);

    [TestMethod]
    public void ABuiltInType_IsASyntaxError()
    {
        var builtInType = new BuiltInTypeExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [72]), TestLocations.TestLocation, "Long");

        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIs(builtInType), VBObjectType.TypeInfo);

        Assert.AreEqual(VBCompileErrorId.SyntaxError, result.ErrorInfo?.VBCompileErrorId);
    }

    [TestMethod]
    public void ATypeThatIsNoName_IsASyntaxError()
    {
        // `TypeOf o Is Nothing` (MS-VBA, Excel-verified, oracle\s29 T43).
        var nothing = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [72]), TestLocations.TestLocation, VBObjectValue.Nothing);

        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIs(nothing), VBObjectType.TypeInfo);

        Assert.AreEqual(VBCompileErrorId.SyntaxError, result.ErrorInfo?.VBCompileErrorId);
    }

    [TestMethod]
    public void ANameThatBindsNoType_IsAUserDefinedTypeThatIsNotDefined()
        => Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined,
            TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("o", "Nope"), VBObjectType.TypeInfo).ErrorInfo?.VBCompileErrorId);

    [TestMethod]
    public void ANonTypeOfIsExpression_Throws()
    {
        var notTypeOfIs = NameOf("bareName");

        Assert.ThrowsExactly<ArgumentException>(() => TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, notTypeOfIs, VBLongType.TypeInfo));
    }
}

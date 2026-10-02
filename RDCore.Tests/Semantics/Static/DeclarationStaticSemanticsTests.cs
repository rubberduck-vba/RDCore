using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics.Static;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// A declared type is a name that resolves to a type: one that still does not, once everything the declaration can see is defined, is an illegal name.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.4 Binding Contexts")]
public sealed class DeclarationStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);

    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol Field(Uri module, string name, VBType type)
        => new(Root, module, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    // the members of the module under test, with everything else the workspace defines, as the resolver sees them.
    private static ImmutableArray<VBCompileErrorInfo> Evaluate(DeclarationRules rules, VBType declared, params Symbol[] others)
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "x", declared);
        var tree = ScopeTreeBuilder.Build([module, field, .. others]);
        return DeclarationStaticSemanticsEvaluator.Evaluate(module, [field], new ScopeTreeSymbolResolver(tree), rules);
    }


    [TestMethod]
    public void ANameThatDoesNotResolve_IsAnErrorThatSaysWhichName()
    {
        var error = Evaluate(DeclarationRules.DeclaredTypes, new VBUnresolvedType("Missing")).Single();

        Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined, error.VBCompileErrorId);
        StringAssert.Contains(error.Verbose, "The declared type 'Missing' could not be resolved.");
    }

    [TestMethod]
    public void TheElementTypeOfAnArray_IsADeclaredTypeToo()
        => Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined,
            Evaluate(DeclarationRules.DeclaredTypes, new VBResizableArrayType(new VBUnresolvedType("Missing"))).Single().VBCompileErrorId);

    [TestMethod]
    public void ANameThatResolvesNow_WasOnlyDefinedAfterTheDeclaration_AndIsNoError()
        => Assert.IsTrue(Evaluate(DeclarationRules.DeclaredTypes, new VBUnresolvedType("Later"), ClassModule("Later")).IsEmpty);

    [TestMethod]
    public void AnIntrinsicName_IsNoError()
        => Assert.IsTrue(Evaluate(DeclarationRules.DeclaredTypes, new VBUnresolvedType("Long")).IsEmpty);

    [TestMethod]
    public void AnUnknownType_IsNotAnUnresolvedName()
        // a declaration with no As clause, or whose type is not known yet, names nothing.
        => Assert.IsTrue(Evaluate(DeclarationRules.DeclaredTypes, VBUnknownType.TypeInfo).IsEmpty);

    [TestMethod]
    public void TheRuleIsOneThatIsAskedFor()
        => Assert.IsTrue(Evaluate(DeclarationRules.Default, new VBUnresolvedType("Missing")).IsEmpty);
}

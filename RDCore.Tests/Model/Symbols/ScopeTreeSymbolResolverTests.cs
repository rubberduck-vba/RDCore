using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Symbols;

[TestClass]
public sealed class ScopeTreeSymbolResolverTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name)
        => new(Root, moduleUri, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

    private static VBLocalVariableSymbol Local(Uri procedureUri, string name)
        => new(Root, procedureUri, name, ScopeKind.Local, R, R);

    // a realistic, valid property: Get returns Object, Let/Set each take just the matching Object value
    // parameter - MS-VBAL §5.3.1.7 consistency (see ScopeTreeSymbolResolver.IsConsistentProperty) is not
    // what these fixtures are testing, so they stay internally consistent rather than triggering it.
    private static VBParameterSymbol ValueParameter(Uri moduleUri)
        => new(Root, moduleUri, "value", R, R, ParameterKind.ImplicitByVal, VBObjectType.TypeInfo);

    private static VBPropertyGetMemberSymbol PropertyGet(Uri moduleUri, string name)
        => new VBPropertyGetMemberSymbol(Root, moduleUri, ScopeKind.Module, name, R, R, AccessModifier.Implicit) { ResolvedType = VBObjectType.TypeInfo };

    private static VBPropertyLetMemberSymbol PropertyLet(Uri moduleUri, string name)
        => new VBPropertyLetMemberSymbol(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, AccessModifier.Implicit) { Parameters = [ValueParameter(moduleUri)] };

    private static VBPropertySetMemberSymbol PropertySet(Uri moduleUri, string name)
        => new VBPropertySetMemberSymbol(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, AccessModifier.Implicit) { Parameters = [ValueParameter(moduleUri)] };

    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);

    private static VBUserDefinedTypeMemberSymbol Udt(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, R, R, access);

    private static VBEnumMemberSymbol EnumType(Uri moduleUri, string name)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Enum, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

    private static VBConstantMemberSymbol Const(Uri moduleUri, string name)
        => new(Root, moduleUri, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

    private static ScopeTreeSymbolResolver Resolver(params Symbol[] symbols)
        => new(ScopeTreeBuilder.Build(symbols));

    [TestMethod]
    public void Resolve_BindsALocal_FromItsProcedure()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "temp");

        var result = Resolver(module, procedure, local).ResolveValue("temp", ScopeKind.Unallocated, procedure.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(local, result.Symbol);
    }

    [TestMethod]
    public void Resolve_BindsASiblingModulesPublicMember_FromThisProcedure()
    {
        var library = Module("Library");
        var api = Procedure(library.Uri, "Compute", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");

        var result = Resolver(library, api, caller, run).ResolveValue("Compute", ScopeKind.Unallocated, run.Uri);

        Assert.AreSame(api, result.Symbol);
    }

    [TestMethod]
    public void Resolve_BindsTheInnermostDeclaration()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "State");
        var field = Field(module.Uri, "State");
        var resolver = Resolver(module, procedure, local, field);

        Assert.AreSame(local, resolver.ResolveValue("State", ScopeKind.Unallocated, procedure.Uri).Symbol);
        Assert.AreSame(field, resolver.ResolveValue("State", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void Resolve_SameNameTwiceInOneModule_IsADuplicateDeclaration()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Value");
        var procedure = Procedure(module.Uri, "Value");

        var result = Resolver(module, field, procedure).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, result.ErrorId);
        Assert.IsNull(result.Symbol);
        CollectionAssert.AreEquivalent(new Symbol[] { field, procedure }, result.Candidates.ToArray());
    }

    [TestMethod]
    public void Resolve_PropertyGetAndLet_ResolvesToTheGetAccessor()
        // MS-VBAL §5.3.1: a property's Get/Let/Set accessors share one name by design - not a
        // duplicate declaration. Get is the representative for a general (read-context) lookup.
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value");
        var let = PropertyLet(module.Uri, "Value");

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(get, result.Symbol);
    }

    [TestMethod]
    public void Resolve_PropertyGetLetAndSet_ResolvesToTheGetAccessor()
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value");
        var let = PropertyLet(module.Uri, "Value");
        var set = PropertySet(module.Uri, "Value");

        var result = Resolver(module, get, let, set).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.AreSame(get, result.Symbol);
    }

    [TestMethod]
    public void Resolve_PropertyLetAndSetOnly_ResolvesToTheLetAccessor()
        // no Get in this property (write-only) - falls back to Let, then Set.
    {
        var module = Module("Mod1");
        var let = PropertyLet(module.Uri, "Value");
        var set = PropertySet(module.Uri, "Value");

        var result = Resolver(module, let, set).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.AreSame(let, result.Symbol);
    }

    [TestMethod]
    public void Resolve_TwoPropertyGetsSameModule_IsStillADuplicateDeclaration()
        // a second accessor of the SAME kind is a genuine duplicate declaration, not a multi-accessor
        // property - the exception is narrow, not "any collision among property symbols".
    {
        var module = Module("Mod1");
        var first = PropertyGet(module.Uri, "Value");
        var second = PropertyGet(module.Uri, "Value");

        var result = Resolver(module, first, second).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, result.ErrorId);
    }

    // ---- property accessor consistency (MS-VBAL §5.3.1.7) ----

    private static VBParameterSymbol IndexParameter(Uri moduleUri, string name = "index", bool optional = false)
        => new(Root, moduleUri, name, R, R, ParameterKind.ImplicitByVal, VBLongType.TypeInfo, optional);

    private static ParamArrayParameterSymbol ParamArrayIndex(Uri moduleUri, string name = "rest")
        => new(Root, moduleUri, name, R, R, ParameterKind.ExplicitByRef);

    [TestMethod]
    public void Resolve_GetAndLetWithMismatchedIndexCount_IsInconsistentPropertyAccessors()
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri)] };
        var let = PropertyLet(module.Uri, "Value"); // no index at all

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InconsistentPropertyAccessors, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_GetAndLetWithMatchingIndex_IsConsistent()
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri)] };
        var let = PropertyLet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri), ValueParameter(module.Uri)] };

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(get, result.Symbol);
    }

    [TestMethod]
    public void Resolve_IndexParametersWithDifferentNames_IsInconsistentPropertyAccessors()
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri, "idx")] };
        var let = PropertyLet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri, "index"), ValueParameter(module.Uri)] };

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InconsistentPropertyAccessors, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_AGetOnlyPropertyWithAnOptionalIndex_IsConsistent()
        // a single accessor has nothing to be inconsistent with - Optional is only ever rejected once
        // a property has more than one accessor.
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri, optional: true)] };

        var result = Resolver(module, get).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
    }

    [TestMethod]
    public void Resolve_AGetWithAnOptionalIndex_PairedWithALet_IsInconsistentPropertyAccessors()
        // the real compiler's own wording: "...or property procedure has an optional parameter, a
        // ParamArray, or an invalid Set final parameter" - Optional/ParamArray is only ever legal on a
        // Get-only property, even when both accessors would otherwise describe the same index shape.
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri, optional: true)] };
        var let = PropertyLet(module.Uri, "Value") with { Parameters = [IndexParameter(module.Uri, optional: true), ValueParameter(module.Uri)] };

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InconsistentPropertyAccessors, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_AGetWithAParamArrayIndex_PairedWithASet_IsInconsistentPropertyAccessors()
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [ParamArrayIndex(module.Uri)] };
        var set = PropertySet(module.Uri, "Value") with { Parameters = [ParamArrayIndex(module.Uri), ValueParameter(module.Uri)] };

        var result = Resolver(module, get, set).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InconsistentPropertyAccessors, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_GetAndLetWithDifferentDeclaredTypes_IsInconsistentPropertyAccessors()
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { ResolvedType = VBLongType.TypeInfo };
        var let = PropertyLet(module.Uri, "Value"); // ValueParameter is Object-typed

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InconsistentPropertyAccessors, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_ASetWhoseValueIsNotObjectVariantOrAClass_IsInconsistentPropertyAccessors()
        // MS-VBAL §5.3.1.7: the declared type of a property set declaration MUST be Object, Variant, or
        // a named class.
    {
        var module = Module("Mod1");
        var get = PropertyGet(module.Uri, "Value") with { ResolvedType = VBLongType.TypeInfo };
        var set = PropertySet(module.Uri, "Value") with { Parameters = [new VBParameterSymbol(Root, module.Uri, "value", R, R, ParameterKind.ImplicitByVal, VBLongType.TypeInfo)] };

        var result = Resolver(module, get, set).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InconsistentPropertyAccessors, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_ASetWhoseValueIsAVariant_IsConsistent()
    {
        var module = Module("Mod1");
        var set = PropertySet(module.Uri, "Value") with { Parameters = [new VBParameterSymbol(Root, module.Uri, "value", R, R, ParameterKind.ImplicitByVal, VBVariantType.TypeInfo)] };

        var result = Resolver(module, set).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
    }

    [TestMethod]
    public void Resolve_ASetWhoseValueIsANamedClass_IsConsistent()
    {
        var module = Module("Mod1");
        var widget = VBClassType.FromClassModule(ClassModule("Widget"));
        var set = PropertySet(module.Uri, "Value") with { Parameters = [new VBParameterSymbol(Root, module.Uri, "value", R, R, ParameterKind.ImplicitByVal, widget)] };

        var result = Resolver(module, set).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
    }

    [TestMethod]
    public void Resolve_ImplicitVsExplicitByRef_IsNotADifference()
        // MS-VBAL §5.3.1.7: corresponding parameters can differ in whether the parameter-mechanism is
        // implicitly or explicitly specified - only the actual ByRef-vs-ByVal distinction counts.
    {
        var module = Module("Mod1");
        var implicitByRef = new VBParameterSymbol(Root, module.Uri, "index", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var explicitByRef = new VBParameterSymbol(Root, module.Uri, "index", R, R, ParameterKind.ExplicitByRef, VBLongType.TypeInfo);
        var get = PropertyGet(module.Uri, "Value") with { Parameters = [implicitByRef] };
        var let = PropertyLet(module.Uri, "Value") with { Parameters = [explicitByRef, ValueParameter(module.Uri)] };

        var result = Resolver(module, get, let).ResolveValue("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
    }

    [TestMethod]
    public void Resolve_APublicNameInTwoModules_IsAnAmbiguousName_FromAThirdModule()
    {
        var alpha = Module("Alpha");
        var alphaCompute = Procedure(alpha.Uri, "Compute", AccessModifier.Public);
        var beta = Module("Beta");
        var betaCompute = Procedure(beta.Uri, "Compute", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");

        var result = Resolver(alpha, alphaCompute, beta, betaCompute, caller, run)
            .ResolveValue("Compute", ScopeKind.Unallocated, run.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorId);
        CollectionAssert.AreEquivalent(new Symbol[] { alphaCompute, betaCompute }, result.Candidates.ToArray());
    }

    [TestMethod]
    public void Resolve_APublicNameInTwoModules_StillBindsToItsOwnModule()
    {
        var alpha = Module("Alpha");
        var alphaCompute = Procedure(alpha.Uri, "Compute", AccessModifier.Public);
        var beta = Module("Beta");
        var betaCompute = Procedure(beta.Uri, "Compute", AccessModifier.Public);
        var alphaRun = Procedure(alpha.Uri, "Run");

        var result = Resolver(alpha, alphaCompute, beta, betaCompute, alphaRun)
            .ResolveValue("Compute", ScopeKind.Unallocated, alphaRun.Uri);

        Assert.AreSame(alphaCompute, result.Symbol, "own module wins before the project scope is reached");
    }

    [TestMethod]
    public void Resolve_AnUndeclaredName_IsUnbound()
    {
        var result = Resolver(Module("Mod1")).ResolveValue("Nope", ScopeKind.Unallocated, Module("Mod1").Uri);

        Assert.IsTrue(result.IsUnbound);
        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void Resolve_FromAnUnknownScope_FallsBackToTheGlobalScope()
        => Assert.IsInstanceOfType<VBStandardModuleSymbol>(
            Resolver(Module("Mod1")).ResolveValue("Mod1", ScopeKind.Unallocated, new Uri("file://rdcore-test#Ghost")).Symbol);

    [TestMethod]
    public void ResolveValue_AUserDefinedType_IsNotACandidate()
        // MS-VBAL 5.6.10: the default binding context's tiers list a variable, constant, Enum type, Enum
        // member, property, function or subroutine - no user-defined type.
    {
        var module = Module("Mod1");
        var udt = Udt(module.Uri, "Point");

        Assert.IsTrue(Resolver(module, udt).ResolveValue("Point", ScopeKind.Unallocated, module.Uri).IsUnbound);
    }

    [TestMethod]
    public void ResolveValue_AConstantAndATypeOfTheSameName_IsNotADuplicate()
    {
        var module = Module("Mod1");
        var constant = Const(module.Uri, "Total");
        var udt = Udt(module.Uri, "Total");

        var result = Resolver(module, constant, udt).ResolveValue("Total", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(constant, result.Symbol);
    }

    [TestMethod]
    public void ResolveValue_AFieldAndATypeOfTheSameName_BindsTheField()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total");
        var udt = Udt(module.Uri, "Total");

        Assert.AreSame(field, Resolver(module, field, udt).ResolveValue("Total", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveValue_AnEnumType_IsACandidate()
    {
        var module = Module("Mod1");
        var enumType = EnumType(module.Uri, "Colour");

        Assert.AreSame(enumType, Resolver(module, enumType).ResolveValue("Colour", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_AModuleLevelUserDefinedType_BindsFromItsProcedure()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var udt = Udt(module.Uri, "Point");

        Assert.AreSame(udt, Resolver(module, procedure, udt).ResolveType("Point", ScopeKind.Unallocated, procedure.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_AnEnumType_IsAType()
    {
        var module = Module("Mod1");
        var enumType = EnumType(module.Uri, "Colour");

        Assert.AreSame(enumType, Resolver(module, enumType).ResolveType("Colour", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_ALocalNamedLikeAClass_DoesNotHideTheClass()
        // `Dim Widget As Widget`: the local is a candidate in the default binding context only.
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "Widget");
        var widget = ClassModule("Widget");
        var resolver = Resolver(module, procedure, local, widget);

        Assert.AreSame(widget, resolver.ResolveType("Widget", ScopeKind.Unallocated, procedure.Uri).Symbol);
        Assert.AreSame(local, resolver.ResolveValue("Widget", ScopeKind.Unallocated, procedure.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_AConstantAndATypeOfTheSameName_BindsTheType()
    {
        var module = Module("Mod1");
        var constant = Const(module.Uri, "Total");
        var udt = Udt(module.Uri, "Total");

        Assert.AreSame(udt, Resolver(module, constant, udt).ResolveType("Total", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_AFieldNamedLikeAType_DoesNotHideTheType()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total");
        var udt = Udt(module.Uri, "Total");

        Assert.AreSame(udt, Resolver(module, field, udt).ResolveType("Total", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveQualifier_BindsAClassModule_TheProject_AndAProceduralModuleByName()
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var caller = Module("Caller");
        var helpers = Module("Helpers");
        var widget = ClassModule("Widget");
        var resolver = Resolver(project, caller, helpers, widget);

        Assert.AreSame(project, resolver.ResolveQualifier("MyProject", ScopeKind.Unallocated, caller.Uri).Symbol);
        Assert.AreSame(helpers, resolver.ResolveQualifier("Helpers", ScopeKind.Unallocated, caller.Uri).Symbol);
        Assert.AreSame(widget, resolver.ResolveQualifier("Widget", ScopeKind.Unallocated, caller.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveQualifier_NeverLooksForAUserDefinedTypeOrAnEnum()
        // a qualifier is a namespace, and neither a Type nor an Enum can contain a type: neither is a candidate, wherever it is declared.
    {
        var owner = Module("Owner");
        var udt = Udt(owner.Uri, "Point", AccessModifier.Public);
        var enumType = EnumType(owner.Uri, "Colour");
        var resolver = Resolver(owner, udt, enumType);

        Assert.IsTrue(resolver.ResolveQualifier("Point", ScopeKind.Unallocated, owner.Uri).IsUnbound);
        Assert.IsTrue(resolver.ResolveQualifier("Colour", ScopeKind.Unallocated, owner.Uri).IsUnbound);
        Assert.AreSame(udt, resolver.ResolveType("Point", ScopeKind.Unallocated, owner.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveQualifier_AModuleLevelTypeOfTheProjectsName_DoesNotHideTheProject()
        // ResolveType selects the enclosing module's own Type first (5.6.10); ResolveQualifier has no such tier.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var owner = Module("Owner");
        var udt = Udt(owner.Uri, "MyProject");
        var resolver = Resolver(project, owner, udt);

        Assert.AreSame(udt, resolver.ResolveType("MyProject", ScopeKind.Unallocated, owner.Uri).Symbol);
        Assert.AreSame(project, resolver.ResolveQualifier("MyProject", ScopeKind.Unallocated, owner.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveQualifier_ALocalOrAFieldNamedLikeAClass_DoesNotHideIt()
    {
        var owner = Module("Owner");
        var procedure = Procedure(owner.Uri, "Run");
        var local = Local(procedure.Uri, "Widget");
        var field = Field(owner.Uri, "Widget");
        var widget = ClassModule("Widget");

        Assert.AreSame(widget, Resolver(owner, procedure, local, field, widget).ResolveQualifier("Widget", ScopeKind.Unallocated, procedure.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveQualifier_ATypeAndAClassOfTheSameName_BindsTheClass()
    {
        var owner = Module("Owner");
        var udt = Udt(owner.Uri, "Widget");
        var widget = ClassModule("Widget");
        var resolver = Resolver(owner, udt, widget);

        Assert.AreSame(widget, resolver.ResolveQualifier("Widget", ScopeKind.Unallocated, owner.Uri).Symbol);
        Assert.AreSame(udt, resolver.ResolveType("Widget", ScopeKind.Unallocated, owner.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_BindsTheProject_AndAProceduralOrClassModuleByName()
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var caller = Module("Caller");
        var helpers = Module("Helpers");
        var widget = ClassModule("Widget");
        var resolver = Resolver(project, caller, helpers, widget);

        Assert.AreSame(project, resolver.ResolveType("MyProject", ScopeKind.Unallocated, caller.Uri).Symbol);
        Assert.AreSame(helpers, resolver.ResolveType("Helpers", ScopeKind.Unallocated, caller.Uri).Symbol);
        Assert.AreSame(widget, resolver.ResolveType("Widget", ScopeKind.Unallocated, caller.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_AModuleLevelType_BeatsTheProjectOfTheSameName_OnlyInItsOwnModule()
        // MS-VBAL 5.6.4's type tiers: the enclosing module's own types come before the project, and the
        // first tier with a match is the selected tier (5.6.10). Another module sees the project.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var owner = Module("Owner");
        var udt = Udt(owner.Uri, "MyProject", AccessModifier.Public);
        var other = Module("Other");
        var resolver = Resolver(project, owner, udt, other);

        Assert.AreSame(udt, resolver.ResolveType("MyProject", ScopeKind.Unallocated, owner.Uri).Symbol);
        Assert.AreSame(project, resolver.ResolveType("MyProject", ScopeKind.Unallocated, other.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveType_AModule_BeatsAnotherModulesPublicTypeOfTheSameName()
        // the project tier (its modules) precedes the tier of other modules' accessible types.
    {
        var shape = Module("Shape");
        var other = Module("Other");
        var udt = Udt(other.Uri, "Shape", AccessModifier.Public);
        var caller = Module("Caller");

        var result = Resolver(shape, other, udt, caller).ResolveType("Shape", ScopeKind.Unallocated, caller.Uri);

        Assert.AreSame(shape, result.Symbol);
    }

    [TestMethod]
    public void ResolveType_AnotherModulesPublicType_IsVisible_APrivateOneIsNot()
    {
        var library = Module("Library");
        var visible = Udt(library.Uri, "Visible", AccessModifier.Public);
        var hidden = Udt(library.Uri, "Hidden", AccessModifier.Private);
        var caller = Module("Caller");
        var resolver = Resolver(library, visible, hidden, caller);

        Assert.AreSame(visible, resolver.ResolveType("Visible", ScopeKind.Unallocated, caller.Uri).Symbol);
        Assert.IsTrue(resolver.ResolveType("Hidden", ScopeKind.Unallocated, caller.Uri).IsUnbound);
    }

    [TestMethod]
    public void ResolveType_TwoTypesOfTheSameNameInOneModule_IsADuplicateDeclaration()
    {
        var module = Module("Mod1");
        var first = Udt(module.Uri, "Point");
        var second = EnumType(module.Uri, "Point");

        var result = Resolver(module, first, second).ResolveType("Point", ScopeKind.Unallocated, module.Uri);

        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, result.ErrorId);
        CollectionAssert.AreEquivalent(new Symbol[] { first, second }, result.Candidates.ToArray());
    }

    [TestMethod]
    public void ResolveType_APublicTypeInTwoModules_IsAnAmbiguousName_FromAThirdModule()
    {
        var alpha = Module("Alpha");
        var alphaPoint = Udt(alpha.Uri, "Point", AccessModifier.Public);
        var beta = Module("Beta");
        var betaPoint = Udt(beta.Uri, "Point", AccessModifier.Public);
        var caller = Module("Caller");

        var result = Resolver(alpha, alphaPoint, beta, betaPoint, caller).ResolveType("Point", ScopeKind.Unallocated, caller.Uri);

        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorId);
        CollectionAssert.AreEquivalent(new Symbol[] { alphaPoint, betaPoint }, result.Candidates.ToArray());
    }

    [TestMethod]
    public void ResolveType_AVariable_IsNeverAType()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total");

        Assert.IsTrue(Resolver(module, field).ResolveType("Total", ScopeKind.Unallocated, module.Uri).IsUnbound);
    }

    [TestMethod]
    public void ResolveType_AnUndeclaredName_IsUnbound()
        => Assert.IsTrue(Resolver(Module("Mod1")).ResolveType("Nope", ScopeKind.Unallocated, Module("Mod1").Uri).IsUnbound);

    [TestMethod]
    public void ResolveValue_AModuleName_BeatsAnotherModulesPublicMemberOfTheSameName()
        // MS-VBAL 5.6.10: the project and its procedural modules (tier 3) come before another procedural
        // module's members (tier 4).
    {
        var shape = Module("Shape");
        var other = Module("Other");
        var member = Procedure(other.Uri, "Shape", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");

        var result = Resolver(shape, other, member, caller, run).ResolveValue("Shape", ScopeKind.Unallocated, run.Uri);

        Assert.AreSame(shape, result.Symbol);
    }

    [TestMethod]
    public void ResolveValue_TheProject_BeatsAnotherModulesPublicMemberOfTheSameName()
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var other = Module("Other");
        var constant = new VBConstantMemberSymbol(Root, other.Uri, "MyProject", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");

        var result = Resolver(project, other, constant, caller, run).ResolveValue("MyProject", ScopeKind.Unallocated, run.Uri);

        Assert.AreSame(project, result.Symbol);
    }

    [TestMethod]
    public void ResolveValue_AModulesOwnMember_BeatsTheModuleNamedLikeIt()
        // the enclosing module (tier 2) precedes the project's modules (tier 3).
    {
        var shape = Module("Shape");
        var constant = Const(shape.Uri, "Shape");
        var run = Procedure(shape.Uri, "Run");

        Assert.AreSame(constant, Resolver(shape, constant, run).ResolveValue("Shape", ScopeKind.Unallocated, run.Uri).Symbol);
    }

    [TestMethod]
    public void ResolveValue_TheProjectAndAModuleOfTheSameName_IsAnAmbiguousName()
        // both are matches in the same tier (5.6.10: 2 or more matches remaining in the selected tier is invalid).
    {
        var project = new VBProjectSymbol(Root, "Shape");
        var shape = Module("Shape");
        var caller = Module("Caller");

        var result = Resolver(project, shape, caller).ResolveValue("Shape", ScopeKind.Unallocated, caller.Uri);

        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorId);
    }

    [TestMethod]
    public void ResolveValue_AClassModule_IsNotACandidate()
        // MS-VBAL 5.6.10: the default binding context lists no class module - a class is a name there only
        // through its predeclared instance (5.2.4.1.2).
    {
        var module = Module("Mod1");
        var widget = ClassModule("Widget");

        Assert.IsTrue(Resolver(module, widget).ResolveValue("Widget", ScopeKind.Unallocated, module.Uri).IsUnbound);
    }

    [TestMethod]
    public void ResolveValue_APredeclaredInstance_BindsAsTheVariableItIs()
    {
        var module = Module("Mod1");
        var widget = ClassModule("Widget");
        var instance = new VBPredeclaredInstanceSymbol(widget);

        var result = Resolver(module, widget, instance).ResolveValue("Widget", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(instance, result.Symbol);
    }

    [TestMethod]
    public void ResolveType_AClassModule_StillBinds_ItsPredeclaredInstanceIsNotAType()
    {
        var module = Module("Mod1");
        var widget = ClassModule("Widget");
        var instance = new VBPredeclaredInstanceSymbol(widget);

        var result = Resolver(module, widget, instance).ResolveType("Widget", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(widget, result.Symbol);
    }

    [TestMethod]
    public void ResolveValue_ALocalNamedLikeAPredeclaredClass_HidesItsDefaultInstance()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "Widget");
        var widget = ClassModule("Widget");

        var result = Resolver(module, procedure, local, widget, new VBPredeclaredInstanceSymbol(widget)).ResolveValue("Widget", ScopeKind.Unallocated, procedure.Uri);

        Assert.AreSame(local, result.Symbol);
    }

    [TestMethod]
    public void GetValue_Throws_ItBindsNamesOnly()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => Resolver(Module("Mod1")).GetValue(GlobalSymbols.UnresolvedSymbol));

    [TestMethod]
    public void TryRead_ReturnsFalse_ItHoldsNoRuntimeBindings()
    {
        Assert.IsFalse(Resolver(Module("Mod1")).TryRead(new MemoryAddress(0), out var value));
        Assert.IsNull(value);
    }
}

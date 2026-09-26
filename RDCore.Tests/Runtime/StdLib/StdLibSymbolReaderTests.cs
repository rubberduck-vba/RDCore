using RDCore.SDK.Model.Source;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// The standard library's symbols, read off the SDK declarations that define it
/// (<strong>MS-VBAL §6.1</strong>). What these assert is that a declaration is the <em>only</em> place a
/// member's name, parameters and return type are written down: everything here is read back off a
/// signature an implementation has to satisfy, so a symbol a workspace resolves cannot describe a
/// member the runtime does not have.
/// </summary>
[TestClass]
public sealed class StdLibSymbolReaderTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static IReadOnlyList<Symbol> Read() => new StdLibSymbolReader(Root).Read(typeof(IStdErrClass).Assembly);

    private static VBStandardModuleSymbol Module(string name)
        => Read().OfType<VBStandardModuleSymbol>().Single(module => module.Name == name);

    private static VBClassModuleSymbol Class(string name)
        => Read().OfType<VBClassModuleSymbol>().Single(@class => @class.Name == name);

    // a standard module's members are separate symbols parented to it, which is what promotes them to
    // the project scope; a class module's ride on the class, reachable only through an instance of it.
    // Compared by AbsoluteUri: a scope path lives entirely in the fragment, and plain Uri equality
    // ignores the fragment - so `ParentUri == module.Uri` matches every module's members alike.
    private static IReadOnlyList<Symbol> MembersOf(VBModuleSymbol module)
        => module is VBClassModuleSymbol @class
            ? [.. @class.Members]
            : [.. Read().Where(symbol => symbol.ParentUri.AbsoluteUri == module.Uri.AbsoluteUri)];

    [TestMethod]
    public void AModuleDeclaration_IsNamedByConvention()
    {
        // IStdInformationModule declares no name of its own, so the module is named for the interface
        // without its IStd prefix and Module suffix. MS-VBAL §6.1.2.7 calls it Information.
        Assert.IsNotNull(Module("Information"));
    }

    [TestMethod]
    public void AModuleDeclaration_IsAStandardModule_SoItsMembersResolveUnqualified()
    {
        // it being a *standard* module is what promotes its Public members to the project scope; a class
        // module's would only be reachable through an instance.
        var information = Module("Information");

        Assert.AreEqual(ScopeKind.Module, information.ScopeKind);
        Assert.Contains("IsNumeric", MembersOf(information).Select(member => member.Name).ToArray());
    }

    [TestMethod]
    public void AFunctionDeclaration_TakesItsReturnTypeFromItsSignature()
    {
        // MS-VBAL 6.1.2.7.1.8: Function IsNumeric(Arg As Variant) As Boolean. The declaration states the
        // Boolean by returning RuntimeSemanticsEvaluationResult<VBBooleanValue> and nothing else.
        var isNumeric = (VBFunctionMemberSymbol)MembersOf(Module("Information")).Single(member => member.Name == "IsNumeric");

        Assert.AreEqual(VBBooleanType.TypeInfo, isNumeric.ResolvedType);
        Assert.AreEqual(SymbolKindExt.Function, isNumeric.Kind);

        var arg = isNumeric.Parameters.Single();
        Assert.AreEqual("Arg", arg.Name, "a VBA parameter name is Pascal-cased: it is what a named argument has to spell");
        Assert.AreEqual(VBVariantType.TypeInfo, arg.ResolvedType);
    }

    [TestMethod]
    public void AMemberDeclaringNoReturnType_IsASub()
    {
        // MS-VBAL 6.1.3.2.1.1: Sub Clear(). The non-generic result is what says so — there is no
        // separate "this one is a Sub" to keep in step with the signature.
        var clear = MembersOf(Class("ErrObject")).Single(member => member.Name == "Clear");

        Assert.IsInstanceOfType<VBProcedureMemberSymbol>(clear);
        Assert.AreEqual(VBVoidType.TypeInfo, ((VBProcedureMemberSymbol)clear).ResolvedType);
    }

    [TestMethod]
    public void AnEnumReturnType_IsTheEnumRatherThanTheValueTypeBehindIt()
    {
        // MS-VBAL 6.1.2.7.1.13: Function VarType(VarName As Variant) As VbVarType. Every value of an
        // enum is a Long, which is what the signature has to name; the declared type is the enum, and
        // the declaration says so with StdLibMemberAttribute.ReturnType.
        var varType = (VBFunctionMemberSymbol)MembersOf(Module("Information")).Single(member => member.Name == "VarType");

        Assert.IsInstanceOfType<VBEnumType>(varType.ResolvedType);
        Assert.AreEqual("VbVarType", varType.ResolvedType.Name);
        Assert.AreEqual("VarName", varType.Parameters.Single().Name);
    }

    [TestMethod]
    public void AClassReturnType_CarriesTheClassesOwnMembers()
    {
        // Information.Err() As ErrObject — and Err.Number has to resolve through the type it returns,
        // which means the class it names is complete by the time the function is read.
        var err = (VBFunctionMemberSymbol)MembersOf(Module("Information")).Single(member => member.Name == "Err");

        var returned = (VBClassType)err.ResolvedType;
        Assert.AreEqual("ErrObject", returned.Name);
        Assert.Contains("Number", returned.Members.Select(member => member.Name).ToArray());
        Assert.IsEmpty(err.Parameters, "Err takes no argument");
    }

    [TestMethod]
    public void TheErrObjectClass_IsNotCreatable()
    {
        // its one instance comes from the environment: `New ErrObject` names nothing.
        Assert.IsFalse(Class("ErrObject").GetProperty(SymbolProperties.Creatable));
    }

    [TestMethod]
    public void APropertyDeclaration_ReadsAsItsTwoAccessors()
    {
        // MS-VBAL 6.1.3.2.2.1: Property Description As String. A signature cannot say "Property", so the
        // declaration does; the Get's return type and the Let's parameter still come from the signature.
        var accessors = MembersOf(Class("ErrObject")).Where(member => member.Name == "Description").ToArray();

        var get = accessors.OfType<VBPropertyGetMemberSymbol>().Single();
        Assert.AreEqual(VBStringType.TypeInfo, get.ResolvedType);

        var let = accessors.OfType<VBPropertyLetMemberSymbol>().Single();
        Assert.AreEqual(VBStringType.TypeInfo, let.Parameters.Last().ResolvedType);
    }

    [TestMethod]
    public void AReadOnlyProperty_HasNoLetAccessor()
    {
        // MS-VBAL 6.1.3.2.2.4: LastDllError is read-only, so the declaration has a Get and no Let.
        var accessors = MembersOf(Class("ErrObject")).Where(member => member.Name == "LastDllError").ToArray();

        Assert.IsInstanceOfType<VBPropertyGetMemberSymbol>(accessors.Single());
    }

    [TestMethod]
    public void AnOptionalParameter_IsOptional_AndKeepsItsVBAName()
    {
        // MS-VBAL 6.1.3.2.1.2: Sub Raise(Number As Long, Optional Source, Optional Description,
        // Optional HelpFile, Optional HelpContext).
        var raise = (VBProcedureMemberSymbol)MembersOf(Class("ErrObject")).Single(member => member.Name == "Raise");

        // slot 0 is the implicit Me of any instance member; the declared parameters follow it.
        var declared = raise.Parameters.Skip(1).ToArray();
        CollectionAssert.AreEqual(
            new[] { "Number", "Source", "Description", "HelpFile", "HelpContext" },
            declared.Select(parameter => parameter.Name).ToArray());

        Assert.IsFalse(declared[0].IsOptional);
        Assert.AreEqual(VBLongType.TypeInfo, declared[0].ResolvedType);
        Assert.IsTrue(declared.Skip(1).All(parameter => parameter.IsOptional));
    }

    [TestMethod]
    public void AnInstanceMember_HasTheImplicitMeAtSlotZero()
    {
        // the same shape SymbolBuilder gives a workspace class module's own members, so dispatch does not
        // have to tell a standard-library member from a source one.
        var raise = (VBProcedureMemberSymbol)MembersOf(Class("ErrObject")).Single(member => member.Name == "Raise");

        var me = raise.Parameters.First();
        Assert.AreEqual("Me", me.Name);
        Assert.AreEqual(ParameterKind.ImplicitByRef, me.ParameterKind);
    }

    [TestMethod]
    public void AModuleMember_HasNoImplicitMe()
    {
        // a standard module has no instance to dispatch on.
        var rgb = (VBFunctionMemberSymbol)MembersOf(Module("Information")).Single(member => member.Name == "RGB");

        CollectionAssert.AreEqual(
            new[] { "Red", "Green", "Blue" },
            rgb.Parameters.Select(parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public void TheMathModule_IsReadWithEveryMemberOfItsSpecification()
    {
        // MS-VBAL 6.1.2.10: eleven public functions and one public subroutine, and the Sub is a Sub
        // because its declaration states no return type.
        var members = MembersOf(Module("Math"));

        CollectionAssert.AreEquivalent(
            new[] { "Abs", "Atn", "Cos", "Exp", "Log", "Rnd", "Round", "Sgn", "Sin", "Sqr", "Tan", "Randomize" },
            members.Select(member => member.Name).ToArray());

        Assert.IsInstanceOfType<VBProcedureMemberSymbol>(members.Single(member => member.Name == "Randomize"));
        Assert.AreEqual(VBDoubleType.TypeInfo, ((VBFunctionMemberSymbol)members.Single(member => member.Name == "Sqr")).ResolvedType);
        Assert.AreEqual(VBSingleType.TypeInfo, ((VBFunctionMemberSymbol)members.Single(member => member.Name == "Rnd")).ResolvedType);
    }

    [TestMethod]
    public void TheDollarSuffixedPairs_AreTwoMembers_DifferingOnlyInReturnType()
    {
        // MS-VBAL 6.1.2.3.1.16: Function Hex(Number As Variant) / Function Hex$(Number As Variant) As
        // String. They take the same argument and differ only in what they return, so C# cannot overload
        // them - which is the one case StdLibMemberAttribute's name really earns.
        var members = MembersOf(Module("Conversion"));

        Assert.AreEqual(VBVariantType.TypeInfo, ((VBFunctionMemberSymbol)members.Single(member => member.Name == "Hex")).ResolvedType);
        Assert.AreEqual(VBStringType.TypeInfo, ((VBFunctionMemberSymbol)members.Single(member => member.Name == "Hex$")).ResolvedType);
    }

    [TestMethod]
    public void TheStringsModule_IsReadWithEveryFunctionOfItsSpecification()
    {
        // MS-VBAL 6.1.2.11.1 lists 43 numbered subsections, several of which declare more than one
        // function - the B-suffixed byte variants and the LTrim/RTrim/Trim group - for 50 members.
        var members = MembersOf(Module("Strings")).Select(member => member.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Asc", "AscB", "AscW", "Chr", "Chr$", "ChrB", "ChrB$", "ChrW", "ChrW$", "Filter",
                "Format", "Format$", "FormatCurrency", "FormatDateTime", "FormatNumber", "FormatPercent",
                "InStr", "InStrB", "InStrRev", "Join", "LCase", "LCase$", "Left", "LeftB", "Left$",
                "LeftB$", "Len", "LenB", "LTrim", "LTrim$", "RTrim", "RTrim$", "Trim", "Trim$", "Mid",
                "MidB", "Mid$", "MidB$", "MonthName", "Replace", "Right", "RightB", "Right$", "RightB$",
                "Space", "Space$", "Split", "StrComp", "StrConv", "String", "String$", "StrReverse",
                "UCase", "UCase$", "WeekdayName",
            },
            members);
    }

    [TestMethod]
    public void AnEnumTypedParameter_KeepsItsDefaultConstant()
    {
        // MS-VBAL 6.1.2.11.1.36: StrComp(..., Optional Compare As VbCompareMethod = vbBinaryCompare).
        // The declared type is the enum, and the <default-value> clause is the constant's own value -
        // which is what an unmapped argument at a call site takes (MS-VBAL 5.3.1.7).
        var strComp = (VBFunctionMemberSymbol)MembersOf(Module("Strings")).Single(member => member.Name == "StrComp");

        var compare = strComp.Parameters.Last();
        Assert.AreEqual("Compare", compare.Name);
        Assert.IsTrue(compare.IsOptional);
        Assert.AreEqual("VbCompareMethod", compare.ResolvedType.Name);
        Assert.AreEqual((int)VBCompareMethod.VBBinaryCompare, Convert.ToInt32(compare.DefaultValue!.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void AnArrayParameter_IsDeclaredAsAVariantArray()
    {
        // MS-VBAL 6.1.2.11.1.16: Join(SourceArray() As Variant, ...) - an array parameter declared with
        // empty parentheses and no bounds (MS-VBAL 5.3.1.5).
        var join = (VBFunctionMemberSymbol)MembersOf(Module("Strings")).Single(member => member.Name == "Join");

        Assert.IsInstanceOfType<VBResizableArrayType>(join.Parameters.First().ResolvedType);
    }

    [TestMethod]
    public void CLngPtrsReturnType_FollowsThePointerWidthOfTheEnvironment()
    {
        // MS-VBAL 3.3.2: LongPtr is a different type in each pointer width, so its width is the one thing
        // about a declaration that its signature cannot state - it belongs to the environment, not to the
        // library.
        static VBType ReturnTypeOfCLngPtr(bool is64Bit)
            => ((VBFunctionMemberSymbol)new StdLibSymbolReader(Root, null, is64Bit)
                .Read([typeof(IStdConversionModule)])
                .OfType<VBFunctionMemberSymbol>()
                .Single(member => member.Name == "CLngPtr")).ResolvedType;

        Assert.AreEqual(VBLongPtrType_x64.TypeInfo, ReturnTypeOfCLngPtr(is64Bit: true));
        Assert.AreEqual(VBLongPtrType_x86.TypeInfo, ReturnTypeOfCLngPtr(is64Bit: false));
    }

    [TestMethod]
    public void AnOptionalParameterWithoutADefault_HasNoDefaultValueClause()
    {
        // MS-VBAL 6.1.2.10.1.7: Round(Number As Variant, Optional NumDigitsAfterDecimal As Long). The
        // parameter is optional and has no <default-value> clause, so an unmapped argument falls back to
        // the declared type's own default rather than to a stated constant (MS-VBAL 5.3.1.11).
        var round = (VBFunctionMemberSymbol)MembersOf(Module("Math")).Single(member => member.Name == "Round");

        var digits = round.Parameters.Last();
        Assert.AreEqual("NumDigitsAfterDecimal", digits.Name);
        Assert.IsTrue(digits.IsOptional);
        Assert.IsNull(digits.DefaultValue);
        Assert.AreEqual(VBLongType.TypeInfo, digits.ResolvedType);
    }

    [TestMethod]
    public void APredefinedEnum_IsNamedByConvention_AndSoAreItsConstants()
    {
        // MS-VBAL 6.1.1.7 VbDayOfWeek. The C# spelling is VBDayOfWeek / VBSunday, which no VBA source
        // writes; the convention recovers both, so only FormShowConstants has to state a name.
        var dayOfWeek = Read().OfType<VBEnumMemberSymbol>().Single(symbol => symbol.Name == "VbDayOfWeek");

        var constants = ((VBEnumType)dayOfWeek.ResolvedType).Members.Select(member => member.Name).ToArray();
        Assert.Contains("vbSunday", constants);
        Assert.Contains("vbUseSystemDayOfWeek", constants);
    }

    [TestMethod]
    public void APredefinedEnum_MayStateItsOwnName()
    {
        // MS-VBAL 6.1.1.1: the one predefined enum with no Vb prefix at all.
        Assert.IsNotNull(Read().OfType<VBEnumMemberSymbol>().Single(symbol => symbol.Name == "FormShowConstants"));
    }

    [TestMethod]
    public void EveryPredefinedEnumOfTheSpecification_IsRead()
    {
        // MS-VBAL 6.1.1 lists sixteen, and a project gets all of them or none: they are the standard
        // library's, not any one module's, so nothing could reference a subset.
        var read = Read().OfType<VBEnumMemberSymbol>().Select(symbol => symbol.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "FormShowConstants", "VbAppWinStyle", "VbCalendar", "VbCallType", "VbCompareMethod",
                "VbDateTimeFormat", "VbDayOfWeek", "VbFileAttribute", "VbFirstWeekOfYear", "VbIMEStatus",
                "VbMsgBoxResult", "VbMsgBoxStyle", "VbQueryClose", "VbStrConv", "VbTriState", "VbVarType",
            },
            read);
    }

    [TestMethod]
    public void AnUnmarkedDeclaration_IsNotRead()
    {
        // the marker is what makes a declaration part of the library, so the reader can be handed a whole
        // assembly - which is how the set stays something nothing has to maintain a list of.
        var read = new StdLibSymbolReader(Root).Read([typeof(IUnmarked)]);

        Assert.IsEmpty(read);
    }

    [TestMethod]
    public void ADeclarationNamingNoVBAType_IsRefused()
    {
        // a symbol declared as a type nothing can bind is worse than no symbol: it resolves, and then
        // every use of it is wrong. A declaration is authored, so this is a mistake to fix, not a
        // condition to degrade over.
        var reader = new StdLibSymbolReader(Root);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read([typeof(IStdLibWithABadParameter)]));
        Assert.Contains("TimeSpan", exception.Message);
    }

    private interface IUnmarked
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Whatever();
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithABadParameter
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Nope(TimeSpan notAVBAType);
    }
}

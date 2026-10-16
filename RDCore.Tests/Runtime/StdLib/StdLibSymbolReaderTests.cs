using RDCore.SDK.Model.AST.Expressions;
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
    public void TheFinancialModule_IsReadWithEveryMemberOfItsSpecification()
    {
        // MS-VBAL 6.1.2.6.1: thirteen public functions, every one of them As Double.
        var members = MembersOf(Module("Financial")).Cast<VBFunctionMemberSymbol>().ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "DDB", "FV", "IPmt", "IRR", "MIRR", "NPer", "NPV", "Pmt", "PPmt", "PV", "Rate", "SLN", "SYD" },
            members.Select(member => member.Name).ToArray());
        Assert.IsTrue(members.All(member => member.ResolvedType == VBDoubleType.TypeInfo));
    }

    [TestMethod]
    public void FVsPresentValueAndDue_AreOptional_WhateverTheSpecificationsDeclarationSays()
    {
        // MS-VBAL 6.1.2.6.1.2 declares "PV As Variant, Due As Variant" without Optional, where its own table says
        // what an omitted one means - and every other annuity function declares its Due optional.
        var fv = (VBFunctionMemberSymbol)MembersOf(Module("Financial")).Single(member => member.Name == "FV");

        CollectionAssert.AreEqual(new[] { "PV", "Due" }, fv.Parameters.Where(parameter => parameter.IsOptional).Select(parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public void PmtsParameters_AreNamedAsTheSpecificationSpellsThem()
        // pV and fV in C#, so that they read as the specification's PV and FV rather than Pv and Fv.
        => CollectionAssert.AreEqual(
            new[] { "Rate", "NPer", "PV", "FV", "Due" },
            ((VBFunctionMemberSymbol)MembersOf(Module("Financial")).Single(member => member.Name == "Pmt")).Parameters.Select(parameter => parameter.Name).ToArray());

    [TestMethod]
    public void MIRRsRates_AreNamedWithTheSpecificationsUnderscores()
        // a named argument has to spell the name the specification declares: Finance_Rate:=, not FinanceRate:=.
        => CollectionAssert.AreEqual(
            new[] { "ValueArray", "Finance_Rate", "Reinvest_Rate" },
            ((VBFunctionMemberSymbol)MembersOf(Module("Financial")).Single(member => member.Name == "MIRR")).Parameters.Select(parameter => parameter.Name).ToArray());

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
    public void TheConstantsModule_IsReadWithEveryConstantOfItsSpecification()
    {
        // MS-VBAL 6.1.2.2: eleven constants and nothing else, in the order of the specification's table, which
        // is the order they are declared in.
        var members = MembersOf(Module("Constants"));

        CollectionAssert.AreEqual(
            new[]
            {
                "vbBack", "vbCr", "vbCrLf", "vbFormFeed", "vbLf", "vbNewLine", "vbNullChar", "vbTab",
                "vbVerticalTab", "vbNullString", "vbObjectError",
            },
            members.Select(member => member.Name).ToArray());
        Assert.IsTrue(members.All(member => member is VBConstantMemberSymbol));
    }

    [TestMethod]
    [DataRow("vbCrLf", VBTypeNames.VBString, "\r\n")]
    [DataRow("vbObjectError", VBTypeNames.VBLong, -2147221504)]
    public void AModuleConstant_IsAPublicConstant_WithItsTypeAndValue(string name, string typeName, object expected)
    {
        // Public, because a Const with no modifier would not reach the project scope, and an unqualified vbCrLf has
        // to resolve; its type is the type of the value it is declared as, and the symbol carries that value.
        var constant = (VBConstantMemberSymbol)MembersOf(Module("Constants")).Single(member => member.Name == name);

        Assert.AreEqual(AccessModifier.Public, constant.AccessModifier);
        Assert.AreEqual(typeName, constant.ResolvedType.Name);
        Assert.AreEqual(expected, Assert.IsInstanceOfType<LiteralExpressionNode>(constant.Value).StaticValue.Handle.Value.BoxedValue);
    }

    [TestMethod]
    public void TheNullStringConstant_IsANullPointerNotAZeroLengthString()
    {
        var constant = (VBConstantMemberSymbol)MembersOf(Module("Constants")).Single(member => member.Name == "vbNullString");

        var value = (VBStringValue)((LiteralExpressionNode)constant.Value!).StaticValue;
        Assert.IsTrue(value.IsNullString);
    }

    [TestMethod]
    public void TheNewLineConstant_IsCrLf_WhateverTheHost()
    {
        // MS-VBAL 6.1.2.2 leaves vbNewLine implementation-defined; a VBA String's line break is CrLf on any host.
        var constant = (VBConstantMemberSymbol)MembersOf(Module("Constants")).Single(member => member.Name == "vbNewLine");

        Assert.AreEqual("\r\n", ((VBStringValue)((LiteralExpressionNode)constant.Value!).StaticValue).Value);
    }

    [TestMethod]
    [DataRow(typeof(IStdLibWithAConstField), "Answer")]
    [DataRow(typeof(IStdLibWithAPrimitiveField), "Answer")]
    [DataRow(typeof(IStdLibWithAMutableField), "Greeting")]
    public void AModuleFieldThatIsNoConstantOfAVBAType_IsRefused(Type declaration, string field)
    {
        // a module constant is declared as the value it is: a C# const is a primitive, whose VBA type would be a
        // guess, a static readonly primitive is the same guess, and a field that is not readonly is no constant.
        var reader = new StdLibSymbolReader(Root);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read([declaration]));
        Assert.Contains(field, exception.Message);
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
        Assert.AreEqual((int)VBCompareMethod.VBBinaryCompare, Convert.ToInt32(((LiteralExpressionNode)compare.DefaultValue!).StaticValue.Handle.Value.BoxedValue));
    }

    [TestMethod]
    public void AnArrayParameter_IsDeclaredAsAVariantArray()
    {
        // an array parameter declared with empty parentheses and no bounds (MS-VBAL 5.3.1.5), of the element type a VBResizableArrayValue
        // alone reads as.
        var concat = (VBFunctionMemberSymbol)new StdLibSymbolReader(Root).Read([typeof(IStdLibWithTypedArrays)])
            .Single(symbol => symbol.Name == "Concat");

        var type = (VBResizableArrayType)concat.Parameters.Single().ResolvedType;
        Assert.AreEqual(VBVariantType.TypeInfo, type.ItemType);
    }

    [TestMethod]
    [DataRow("Join")]
    [DataRow("Filter")]
    public void JoinAndFiltersSourceArray_IsAVariant_WhateverTheSpecificationsDeclarationSays(string member)
    {
        // MS-VBAL 6.1.2.11.1.16 and 6.1.2.11.1.7 declare SourceArray() As Variant. An array parameter takes an array of its own element type,
        // which would refuse the String() Split returns: as a Variant, Join(Split(s)) and Join(Array(...)) both reach the member.
        var function = (VBFunctionMemberSymbol)MembersOf(Module("Strings")).Single(symbol => symbol.Name == member);

        Assert.AreEqual(VBVariantType.TypeInfo, function.Parameters.Single(p => p.Name == "SourceArray").ResolvedType);
    }

    [TestMethod]
    public void StrConvsLocaleID_IsOptional_WithNoDefaultClause_WhateverTheSpecificationsDeclarationSays()
    {
        // MS-VBAL 6.1.2.11.1.37 declares LocaleID required, where the documented call and most code pass two arguments.
        var strConv = (VBFunctionMemberSymbol)MembersOf(Module("Strings")).Single(symbol => symbol.Name == "StrConv");

        var localeID = strConv.Parameters.Single(p => p.Name == "LocaleID");
        Assert.IsTrue(localeID.IsOptional);
        Assert.IsNull(localeID.DefaultValue);
        Assert.AreEqual(VBLongType.TypeInfo, localeID.ResolvedType);
    }

    [TestMethod]
    [DataRow("Split", "Limit", -1)]
    [DataRow("Replace", "Start", 1)]
    [DataRow("Replace", "Count", -1)]
    // the declaration's -1, where the page's parameter table says 1.
    [DataRow("InStrRev", "Start", -1)]
    [DataRow("Filter", "Include", true)]
    public void ATypedOptionalOfTheStringsModule_KeepsItsDeclaredDefault(string member, string parameter, object expected)
    {
        var function = (VBFunctionMemberSymbol)MembersOf(Module("Strings")).Single(symbol => symbol.Name == member);

        var value = ((LiteralExpressionNode)function.Parameters.Single(p => p.Name == parameter).DefaultValue!).StaticValue;
        Assert.AreEqual(expected, value is VBBooleanValue flag ? (bool)flag.Value : value.Handle.Value.BoxedValue);
    }

    [TestMethod]
    public void EveryComparingMember_TakesTheCallersOptionCompare_WhenCompareIsOmitted()
    {
        // MS-VBAL 6.1.2.11.1: "if omitted, it uses the <option-compare-directive> of the calling module", for InStrRev too, whose parameter
        // table says otherwise and whose runtime semantics say so.
        var flagged = MembersOf(Module("Strings")).OfType<VBFunctionMemberSymbol>()
            .Where(member => member.Parameters.Any(p => p.GetProperty(SymbolProperties.DefaultsToOptionCompare)))
            .Select(member => member.Name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "Filter", "InStr", "InStrB", "InStrRev", "Replace", "Split", "StrComp" }, flagged);
    }

    [TestMethod]
    public void TheStringsModule_DeclaresExactlyTheStringTwinsOfItsSpecification()
    {
        // MS-VBAL 6.1.2.11.1 titles 15 subsections with a $ form, LTrim$ / RTrim$ / Trim$ being one of them. A typed name the library
        // does not declare is refused for the whole module, so a twin too many or too few changes what compiles.
        var twins = MembersOf(Module("Strings")).Select(member => member.Name).Where(name => name.EndsWith('$')).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Chr$", "ChrB$", "ChrW$", "Format$", "LCase$", "Left$", "LeftB$", "LTrim$", "Mid$", "MidB$",
                "Right$", "RightB$", "RTrim$", "Space$", "String$", "Trim$", "UCase$",
            },
            twins);
    }

    [TestMethod]
    public void VbStrConv_HasEveryConstantOfTheSpecification()
    {
        // MS-VBAL 6.1.1.14, vbNarrow among them.
        var strConv = Read().OfType<VBEnumMemberSymbol>().Single(symbol => symbol.Name == "VbStrConv");
        var constants = ((VBEnumType)strConv.ResolvedType).Members.OfType<VBEnumConstMemberSymbol>()
            .Select(constant => (constant.Name, constant.GetProperty(SymbolProperties.EnumValue))).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                ("vbUpperCase", 1L), ("vbLowerCase", 2L), ("vbProperCase", 3L), ("vbWide", 4L), ("vbNarrow", 8L),
                ("vbKatakana", 16L), ("vbHiragana", 32L), ("vbUnicode", 64L), ("vbFromUnicode", 128L),
            },
            constants);
    }

    [TestMethod]
    public void AnArrayParameter_MayStateItsElementType()
    {
        // MS-VBAL 6.1.2.6.1.7: NPV(Rate As Double, ValueArray() As Double). A VBResizableArrayValue alone reads
        // as Variant(), so the declaration says what the elements are - and the call site coerces an argument
        // to an array of those, rather than refusing a Double() for not being a Variant().
        var sum = (VBFunctionMemberSymbol)new StdLibSymbolReader(Root).Read([typeof(IStdLibWithTypedArrays)])
            .Single(symbol => symbol.Name == "Sum");

        var type = (VBResizableArrayType)sum.Parameters.Single().ResolvedType;
        Assert.AreEqual(VBDoubleType.TypeInfo, type.ItemType);
    }

    [TestMethod]
    [DataRow("Checksum")]
    [DataRow("Digest")]
    public void AByteArrayParameter_IsTheByteArrayType(string member)
    {
        // a Byte() is a type of its own - the one a Dim with the same element type declares - whether the
        // declaration takes the array of any element type or the byte array itself.
        var function = (VBFunctionMemberSymbol)new StdLibSymbolReader(Root).Read([typeof(IStdLibWithTypedArrays)])
            .Single(symbol => symbol.Name == member);

        Assert.IsInstanceOfType<VBResizableByteArrayType>(function.Parameters.Single().ResolvedType);
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
    [DataRow("VbCompareMethod", "vbTextCompare", 1)]
    [DataRow("VbStrConv", "vbProperCase", 3)]
    [DataRow("VbDayOfWeek", "vbUseSystemDayOfWeek", 0)]
    public void APredefinedEnumConstant_CarriesItsValue(string enumName, string constantName, int expected)
    {
        // a constant has no storage to read a value from, so the symbol carries the value the declaration
        // states (MS-VBAL 5.2.3.4). A zero is a value like any other, not the absence of one.
        var @enum = Read().OfType<VBEnumMemberSymbol>().Single(symbol => symbol.Name == enumName);
        var constant = ((VBEnumType)@enum.ResolvedType).Members.OfType<VBEnumConstMemberSymbol>().Single(member => member.Name == constantName);

        Assert.IsTrue(constant.TryGetProperty(SymbolProperties.EnumValue, out var value));
        Assert.AreEqual((long)expected, value);
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

    [TestMethod]
    [DataRow(typeof(IStdLibWithAScalarElementType), "Scale")]
    [DataRow(typeof(IStdLibWithAnOptionalArray), "Sum")]
    [DataRow(typeof(IStdLibWithAByRefArray), "Fill")]
    [DataRow(typeof(IStdLibWithAParamArrayElementType), "Total")]
    [DataRow(typeof(IStdLibWithAByteArrayOfDoubles), "Average")]
    public void AnElementTypeForAParameterThatIsNoRequiredArray_IsRefused(Type declaration, string member)
    {
        // an element type makes the parameter an array of it: stated for a scalar it would silently change the
        // parameter's type, for an optional array it would declare an omission nothing gives a value, for a ByRef
        // one a parameter no argument reaches, for a ParamArray a type it cannot have, and for a Byte() an array
        // that could never hold its argument.
        var reader = new StdLibSymbolReader(Root);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read([declaration]));
        Assert.Contains(member, exception.Message);
    }

    [TestMethod]
    [DataRow("Limit", "Long", -1)]
    [DataRow("Count", "Integer", (short)3)]
    [DataRow("Include", "Boolean", true)]
    [DataRow("Delimiter", "String", " ")]
    public void AStatedDefault_IsCarriedAsAValueOfTheDeclaredType(string parameter, string type, object expected)
    {
        // MS-VBAL 6.1.2.11.1.35: Split(..., Optional Limit As Long = -1, ...). A typed optional's C# default can only be null, which states
        // no <default-value> clause at all, so the declaration states the clause, and the symbol carries it the way an enumeration's is.
        var stated = (VBFunctionMemberSymbol)new StdLibSymbolReader(Root).Read([typeof(IStdLibWithStatedDefaults)]).Single(symbol => symbol.Name == "Stated");

        var symbol = stated.Parameters.Single(p => p.Name == parameter);
        var value = ((LiteralExpressionNode)symbol.DefaultValue!).StaticValue;
        Assert.IsTrue(symbol.IsOptional);
        Assert.AreEqual(type, value.TypeInfo.Name);
        Assert.AreEqual(symbol.ResolvedType, value.TypeInfo);
        Assert.AreEqual(expected, value switch
        {
            VBStringValue text => text.Value,
            VBBooleanValue flag => (bool)flag.Value,
            _ => value.Handle.Value.BoxedValue,
        });
    }

    [TestMethod]
    [DataRow(typeof(IStdLibWithARequiredDefault), "Start", DisplayName = "a required parameter")]
    [DataRow(typeof(IStdLibWithAParamArrayDefault), "Rest", DisplayName = "a ParamArray")]
    [DataRow(typeof(IStdLibWithAnEnumDefault), "Compare", DisplayName = "an enumeration, whose C# default states it")]
    [DataRow(typeof(IStdLibWithAMistypedDefault), "Limit", DisplayName = "a Boolean for a Long")]
    [DataRow(typeof(IStdLibWithAnOverflowingDefault), "Count", DisplayName = "70000 for an Integer")]
    [DataRow(typeof(IStdLibWithAVariantDefault), "Value", DisplayName = "a Variant")]
    public void AStatedDefaultForAParameterThatCannotTakeIt_IsRefused(Type declaration, string parameter)
    {
        // a default that is not a value of the parameter's type, or one stated where no default can be, would be a <default-value> clause no
        // VBA declaration could have written.
        var reader = new StdLibSymbolReader(Root);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read([declaration, typeof(VBCompareMethod)]));
        Assert.Contains($"'{parameter}' in '{declaration.Name}.", exception.Message);
    }

    [TestMethod]
    public void AnOptionCompareParameter_IsFlagged_AndKeepsItsDeclaredDefault()
    {
        // MS-VBAL 6.1.2.11.1.36: StrComp(..., Optional Compare As VbCompareMethod = vbBinaryCompare), "if omitted, it uses the
        // <option-compare-directive> of the calling module". The declaration says = vbBinaryCompare, which is what a caller with no calling
        // module gets; the flag says what a call from source fills in.
        var find = (VBFunctionMemberSymbol)new StdLibSymbolReader(Root).Read([typeof(IStdLibWithAnOptionCompare), typeof(VBCompareMethod)])
            .Single(symbol => symbol.Name == "Find");

        var compare = find.Parameters.Single(p => p.Name == "Compare");
        Assert.IsTrue(compare.GetProperty(SymbolProperties.DefaultsToOptionCompare));
        Assert.AreEqual(0, Convert.ToInt32(((LiteralExpressionNode)compare.DefaultValue!).StaticValue.Handle.Value.BoxedValue));
        Assert.IsFalse(find.Parameters.Single(p => p.Name == "Text").TryGetProperty(SymbolProperties.DefaultsToOptionCompare, out _));
    }

    [TestMethod]
    [DataRow(typeof(IStdLibWithALongOptionCompare), DisplayName = "a Long")]
    [DataRow(typeof(IStdLibWithARequiredOptionCompare), DisplayName = "a required parameter")]
    [DataRow(typeof(IStdLibWithAByRefOptionCompare), DisplayName = "a ByRef parameter")]
    [DataRow(typeof(IStdLibWithAParamArrayOptionCompare), DisplayName = "a ParamArray")]
    public void AnOptionCompareMarkerOnAParameterThatCannotTakeIt_IsRefused(Type declaration)
    {
        // what takes the caller's Option Compare is a comparing member's Compare, declared as the specification declares it.
        var reader = new StdLibSymbolReader(Root);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read([declaration, typeof(VBCompareMethod)]));
        Assert.Contains($"in '{declaration.Name}.Find'", exception.Message);
    }

    [StdLibModule("Compare")]
    private interface IStdLibWithAnOptionCompare
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Find(VBStringValue text, [StdLibOptionCompare] VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithALongOptionCompare
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Find([StdLibOptionCompare] VBLongValue? compare = default);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithARequiredOptionCompare
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Find([StdLibOptionCompare] VBCompareMethod compare);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAByRefOptionCompare
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Find([System.Runtime.InteropServices.Optional, StdLibOptionCompare] ref VBCompareMethod compare);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAParamArrayOptionCompare
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Find([StdLibOptionCompare] params VBVariantValue[] compare);
    }

    [StdLibModule("Defaults")]
    private interface IStdLibWithStatedDefaults
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Stated(
            [StdLibDefault(-1)] VBLongValue? limit = default, [StdLibDefault(3)] VBIntegerValue? count = default,
            [StdLibDefault(true)] VBBooleanValue? include = default, [StdLibDefault(" ")] VBStringValue? delimiter = default);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithARequiredDefault
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Find([StdLibDefault(1)] VBLongValue start);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAParamArrayDefault
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Total([StdLibDefault(1)] params VBVariantValue[] rest);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAnEnumDefault
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Compare([StdLibDefault(1)] VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAMistypedDefault
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Split([StdLibDefault(true)] VBLongValue? limit = default);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAnOverflowingDefault
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Repeat([StdLibDefault(70000)] VBIntegerValue? count = default);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAVariantDefault
    {
        RuntimeSemanticsEvaluationResult<VBLongValue> Hold([StdLibDefault(1)] VBVariantValue? value = default);
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

    [StdLibModule("Arrays")]
    private interface IStdLibWithTypedArrays
    {
        RuntimeSemanticsEvaluationResult<VBDoubleValue> Sum([StdLibArray(typeof(VBDoubleValue))] VBResizableArrayValue values);

        RuntimeSemanticsEvaluationResult<VBLongValue> Checksum([StdLibArray(typeof(VBByteValue))] VBResizableArrayValue bytes);

        RuntimeSemanticsEvaluationResult<VBLongValue> Digest([StdLibArray(typeof(VBByteValue))] VBResizableByteArrayValue bytes);

        RuntimeSemanticsEvaluationResult<VBStringValue> Concat(VBResizableArrayValue values);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAScalarElementType
    {
        RuntimeSemanticsEvaluationResult<VBDoubleValue> Scale([StdLibArray(typeof(VBDoubleValue))] VBDoubleValue value);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAByRefArray
    {
        RuntimeSemanticsEvaluationResult Fill([StdLibArray(typeof(VBDoubleValue))] ref VBResizableArrayValue values);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAParamArrayElementType
    {
        RuntimeSemanticsEvaluationResult<VBDoubleValue> Total([StdLibArray(typeof(VBDoubleValue))] params VBVariantValue[] values);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAByteArrayOfDoubles
    {
        RuntimeSemanticsEvaluationResult<VBDoubleValue> Average([StdLibArray(typeof(VBDoubleValue))] VBResizableByteArrayValue values);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAnOptionalArray
    {
        RuntimeSemanticsEvaluationResult<VBDoubleValue> Sum([StdLibArray(typeof(VBDoubleValue))] VBResizableArrayValue? values = default);
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAConstField
    {
        public const int Answer = 42;
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAPrimitiveField
    {
        public static readonly int Answer = 42;
    }

    [StdLibModule("Bad")]
    private interface IStdLibWithAMutableField
    {
        public static VBStringValue Greeting = new("hi");
    }
}

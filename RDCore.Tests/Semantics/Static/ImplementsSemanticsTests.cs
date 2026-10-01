using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Static;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// What an <c>Implements</c> directive may say (<strong>MS-VBAL §5.2.4.2</strong>) and requires of its class module, and what
/// an implemented name declaration must be (<strong>§5.3.1.9</strong>), checked on the symbols real source builds.
/// </summary>
/// <remarks>
/// <c>IShape</c> has <c>Draw</c> (a Sub), <c>Area(ByVal Scale As Double)</c> (a Function) and a public variable
/// <c>Title As String</c>; each test brings the class module <c>Disc</c> it checks.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.2.4.2 Implements Directive")]
public sealed class ImplementsSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    // module-level variables come before procedures, as do the directives of a class module.
    private const string Shape =
        "Public Title As String\r\n" +
        "Public Sub Draw()\r\nEnd Sub\r\n" +
        "Public Function Area(ByVal Scale As Double) As Double\r\nEnd Function\r\n";

    private const string FullDisc =
        "Implements IShape\r\n" +
        "Private Sub IShape_Draw()\r\nEnd Sub\r\n" +
        "Private Function IShape_Area(ByVal Scale As Double) As Double\r\nEnd Function\r\n" +
        "Private Property Get IShape_Title() As String\r\nEnd Property\r\n" +
        "Private Property Let IShape_Title(ByVal Value As String)\r\nEnd Property\r\n";

    private static ImmutableArray<VBCompileErrorInfo> Check(string body, params (string Name, string Body)[] others)
    {
        var modules = new List<(Uri, ModuleType, ModuleParseResult)> { Class("IShape", Shape), Class("Disc", body) };
        modules.AddRange(others.Select(other => Class(other.Name, other.Body)));

        var resolver = WorkspaceSymbolResolver.Compose(Root, modules, new IntrinsicSymbolResolver());
        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(resolver.ResolveType("Disc", ScopeKind.Global, ModuleUri("Disc")).Symbol);
        return ImplementsSemantics.Evaluate(module, resolver);
    }

    private static void AssertOnly(VBCompileErrorId expected, ImmutableArray<VBCompileErrorInfo> errors)
        => Assert.AreEqual(expected, errors.Single().VBCompileErrorId, string.Join("; ", errors.Select(error => error.Verbose)));

    // FullDisc with one declaration replaced.
    private static string WithoutAndWith(string removed, string added) => FullDisc.Replace(removed, string.Empty) + added;

    #region What the directive requires

    [TestMethod]
    public void AClassThatImplementsEveryMember_IsValid()
        => Assert.IsEmpty(Check(FullDisc));

    [TestMethod]
    public void AClassWithNoImplementsDirective_RequiresNothing()
        => Assert.IsEmpty(Check("Public Sub Anything()\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AMissingMethod_IsNotImplemented()
        => AssertOnly(VBCompileErrorId.InterfaceMemberNotImplemented, Check(FullDisc.Replace("Private Sub IShape_Draw()\r\nEnd Sub\r\n", string.Empty)));

    [TestMethod]
    public void AVariable_NeedsItsPropertyGet()
        => AssertOnly(VBCompileErrorId.InterfaceMemberNotImplemented,
            Check(FullDisc.Replace("Private Property Get IShape_Title() As String\r\nEnd Property\r\n", string.Empty)));

    [TestMethod]
    public void AVariable_NeedsItsPropertyLet()
        => AssertOnly(VBCompileErrorId.InterfaceMemberNotImplemented,
            Check(FullDisc.Replace("Private Property Let IShape_Title(ByVal Value As String)\r\nEnd Property\r\n", string.Empty)));

    [TestMethod]
    public void AnObjectVariable_NeedsAPropertyGetAndAPropertySet()
    {
        var circle = Check(
            "Implements IHolder\r\n" +
            "Private Property Get IHolder_Item() As Object\r\nEnd Property\r\n" +
            "Private Property Set IHolder_Item(ByVal Value As Object)\r\nEnd Property\r\n",
            ("IHolder", "Public Item As Object\r\n"));

        Assert.IsEmpty(circle);
    }

    [TestMethod]
    public void AnObjectVariable_ImplementedWithAPropertyLet_IsMissingItsPropertySet()
    {
        var errors = Check(
            "Implements IHolder\r\n" +
            "Private Property Get IHolder_Item() As Object\r\nEnd Property\r\n" +
            "Private Property Let IHolder_Item(ByVal Value As Object)\r\nEnd Property\r\n",
            ("IHolder", "Public Item As Object\r\n"));

        Assert.IsTrue(errors.Any(error => error.VBCompileErrorId == VBCompileErrorId.InterfaceMemberNotImplemented));
    }

    [TestMethod]
    public void AVariantVariable_NeedsAllThreeAccessors()
    {
        var errors = Check(
            "Implements IHolder\r\n" +
            "Private Property Get IHolder_Item() As Variant\r\nEnd Property\r\n" +
            "Private Property Let IHolder_Item(ByVal Value As Variant)\r\nEnd Property\r\n",
            ("IHolder", "Public Item As Variant\r\n"));

        AssertOnly(VBCompileErrorId.InterfaceMemberNotImplemented, errors);
        StringAssert.Contains(errors.Single().Verbose, "Property Set");
    }

    [TestMethod]
    public void APrivateMemberOfTheInterface_IsNotRequired()
        => Assert.IsEmpty(Check(
            "Implements IPartial\r\nPrivate Sub IPartial_Visible()\r\nEnd Sub\r\n",
            ("IPartial", "Public Sub Visible()\r\nEnd Sub\r\nPrivate Sub Hidden()\r\nEnd Sub\r\nPrivate Secret As Long\r\n")));

    #endregion

    #region What an implemented name declaration must be

    [TestMethod]
    public void AnImplementationOfAnotherKind_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(WithoutAndWith(
            "Private Sub IShape_Draw()\r\nEnd Sub\r\n", "Private Function IShape_Draw() As Long\r\nEnd Function\r\n")));

    [TestMethod]
    public void AVariable_ImplementedWithAProcedure_IsInvalid()
    {
        var errors = Check(FullDisc + "Private Sub IShape_Title()\r\nEnd Sub\r\n");

        AssertOnly(VBCompileErrorId.InvalidImplementedMember, errors);
        StringAssert.Contains(errors.Single().Verbose, "variable");
    }

    [TestMethod]
    public void AnImplementationWithAnotherNumberOfParameters_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(FullDisc.Replace("(ByVal Scale As Double)", "()")));

    [TestMethod]
    public void AnImplementationWithAParameterOfAnotherType_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(FullDisc.Replace("(ByVal Scale As Double)", "(ByVal Scale As Long)")));

    [TestMethod]
    public void AnImplementationWithAParameterOfAnotherMechanism_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(FullDisc.Replace("(ByVal Scale As Double)", "(ByRef Scale As Double)")));

    [TestMethod]
    public void AnImplementationMayRenameItsParameters_AndLeaveTheMechanismImplicitOrWriteItOut()
    {
        Assert.IsEmpty(Check(FullDisc.Replace("(ByVal Scale As Double)", "(ByVal Factor As Double)")));
        Assert.IsEmpty(Check(
            "Implements IRef\r\nPrivate Sub IRef_Fill(Total As Long)\r\nEnd Sub\r\n",
            ("IRef", "Public Sub Fill(ByRef Count As Long)\r\nEnd Sub\r\n")));
    }

    [TestMethod]
    public void AnOptionalParameter_MustBeOptionalInTheImplementationToo()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(
            "Implements IOpt\r\nPrivate Sub IOpt_Run(ByVal N As Long)\r\nEnd Sub\r\n",
            ("IOpt", "Public Sub Run(Optional ByVal N As Long)\r\nEnd Sub\r\n")));

    [TestMethod]
    public void AFunctionOfAnotherType_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(FullDisc.Replace("As Double\r\nEnd Function", "As Long\r\nEnd Function")));

    [TestMethod]
    public void APropertyOfAnotherTypeThanTheVariable_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(FullDisc.Replace("IShape_Title() As String", "IShape_Title() As Long")));

    [TestMethod]
    public void ALetOfAnotherTypeThanTheVariable_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementedMember, Check(FullDisc.Replace("IShape_Title(ByVal Value As String)", "IShape_Title(ByVal Value As Long)")));

    [TestMethod]
    public void AProcedureWithTheInterfacePrefixThatIsNoMemberOfIt_IsNoImplementation()
        => Assert.IsEmpty(Check(FullDisc + "Private Sub IShape_Other(ByVal Anything As String)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AnImplementationIsFoundWithoutRegardToCase()
        => Assert.IsEmpty(Check(FullDisc.Replace("IShape_Draw", "ISHAPE_draw")));

    #endregion

    #region The directive itself

    [TestMethod]
    public void ANameThatIsNoClass_IsUserDefinedTypeNotDefined()
        => AssertOnly(VBCompileErrorId.UserDefinedTypeNotDefined, Check("Implements Missing\r\n"));

    [TestMethod]
    public void ImplementingTheClassItself_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementsDirective, Check("Implements Disc\r\n"));

    [TestMethod]
    public void ImplementingTheSameClassTwice_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidImplementsDirective, Check("Implements IShape\r\n" + FullDisc));

    [TestMethod]
    public void AClassWithAnUnderscoreInAPublicMember_CannotBeAnInterface()
        => AssertOnly(VBCompileErrorId.InvalidImplementsDirective, Check("Implements IBad\r\n", ("IBad", "Public Sub Do_It()\r\nEnd Sub\r\n")));

    [TestMethod]
    public void AnUnderscoreInAPrivateMember_DoesNotMatter()
        => Assert.IsEmpty(Check("Implements IOk\r\n", ("IOk", "Private Sub Do_It()\r\nEnd Sub\r\n")));

    [TestMethod]
    public void AnInterfacePrefixThatBeginsAnother_IsInvalid()
        => Assert.IsTrue(Check(
            "Implements IA\r\nImplements IA_B\r\n",
            ("IA", "Private Sub Hidden()\r\nEnd Sub\r\n"), ("IA_B", "Private Sub Hidden()\r\nEnd Sub\r\n"))
            .Any(error => error.VBCompileErrorId == VBCompileErrorId.InvalidImplementsDirective));

    #endregion
}

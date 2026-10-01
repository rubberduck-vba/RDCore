using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// What a class module may declare about events (<strong>MS-VBAL §5.2.4.3</strong>, <strong>§5.2.3.1.2</strong>,
/// <strong>§5.3.1.8</strong>), checked on the symbols real source builds.
/// </summary>
/// <remarks>
/// <c>Source</c> declares <c>Event Changed(ByVal Value As Long)</c> and <c>Event Bump(ByRef Count As Long)</c>; each test
/// brings the class module <c>Sink</c> it checks.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.8 Event Handler Declarations")]
public sealed class ClassModuleEventSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private const string Source = "Public Event Changed(ByVal Value As Long)\r\nPublic Event Bump(ByRef Count As Long)\r\n";

    private static ImmutableArray<VBCompileErrorInfo> Check(string sinkBody, string name = "Sink")
    {
        var resolver = WorkspaceSymbolResolver.Compose(
            Root, [Class("Source", Source), Class("Silent", "Public Sub Quiet()\r\nEnd Sub\r\n"), Class(name, sinkBody)], new IntrinsicSymbolResolver());
        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(resolver.ResolveType(name, ScopeKind.Global, ModuleUri(name)).Symbol);
        return ClassModuleEventSemantics.Evaluate(module, resolver);
    }

    private static void AssertOnly(VBCompileErrorId expected, ImmutableArray<VBCompileErrorInfo> errors)
        => Assert.AreEqual(expected, errors.Single().VBCompileErrorId, string.Join("; ", errors.Select(error => error.Verbose)));

    #region Event declarations

    [TestMethod]
    public void AValidSink_HasNoErrors()
        => Assert.IsEmpty(Check(
            "Private WithEvents Src As Source\r\n" +
            "Private Sub Src_Changed(ByVal Value As Long)\r\nEnd Sub\r\n" +
            "Private Sub Src_Bump(ByRef Count As Long)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AnEventNameWithAnUnderscore_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventName, Check("Public Event Bad_Name()\r\n"));

    [TestMethod]
    public void AnEventDeclaredTwice_IsADuplicateDeclaration()
        => AssertOnly(VBCompileErrorId.DuplicateDeclaration, Check("Public Event Moved()\r\nEvent MOVED(ByVal X As Long)\r\n"));

    #endregion

    #region WithEvents variables

    [TestMethod]
    public void AWithEventsVariable_OfAClassWithEvents_IsValid()
        => Assert.IsEmpty(Check("Private WithEvents Src As Source\r\n"));

    [TestMethod]
    public void AWithEventsVariable_AsObject_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidWithEventsType, Check("Private WithEvents Src As Object\r\n"));

    [TestMethod]
    public void AWithEventsVariable_AsAnIntrinsicType_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidWithEventsType, Check("Private WithEvents Src As Long\r\n"));

    [TestMethod]
    public void AWithEventsVariable_OfAClassWithNoEvents_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidWithEventsType, Check("Private WithEvents Src As Silent\r\n"));

    [TestMethod]
    public void AWithEventsVariable_OfTheClassThatContainsIt_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidWithEventsType, Check("Public Event Moved()\r\nPrivate WithEvents Me2 As Sink\r\n"));

    [TestMethod]
    public void AWithEventsVariable_OfATypeThatIsNotKnown_IsNotReported()
        // nothing says it is wrong: it is not resolved, which is not the same thing.
        => Assert.IsEmpty(Check("Private WithEvents Src As Missing\r\n"));

    #endregion

    #region Handlers

    [TestMethod]
    public void AHandlerThatIsAFunction_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventHandler, Check(
            "Private WithEvents Src As Source\r\nPrivate Function Src_Changed(ByVal Value As Long) As Long\r\nEnd Function\r\n"));

    [TestMethod]
    public void AHandlerThatIsAPropertyLet_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventHandler, Check(
            "Private WithEvents Src As Source\r\nPublic Property Let Src_Changed(ByVal Value As Long)\r\nEnd Property\r\n"));

    [TestMethod]
    public void AHandlerWithTooFewParameters_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventHandler, Check("Private WithEvents Src As Source\r\nPrivate Sub Src_Changed()\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AHandlerWithTooManyParameters_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventHandler, Check(
            "Private WithEvents Src As Source\r\nPrivate Sub Src_Changed(ByVal Value As Long, ByVal More As Long)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AHandlerParameterOfAnotherType_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventHandler, Check(
            "Private WithEvents Src As Source\r\nPrivate Sub Src_Changed(ByVal Value As String)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AHandlerParameterOfAnotherMechanism_IsInvalid()
        => AssertOnly(VBCompileErrorId.InvalidEventHandler, Check(
            "Private WithEvents Src As Source\r\nPrivate Sub Src_Changed(ByRef Value As Long)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AHandlerMayRenameItsParameters_AndLeaveTheMechanismImplicit()
        // MS-VBAL §5.3.1.8: the parameters can differ in name and in whether the mechanism is written out.
        => Assert.IsEmpty(Check(
            "Private WithEvents Src As Source\r\n" +
            "Private Sub Src_Bump(Total As Long)\r\nEnd Sub\r\n" +
            "Private Sub Src_Changed(ByVal Other As Long)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AProcedureNamedForAVariableButNotForAnEventOfIt_IsNoHandler()
        => Assert.IsEmpty(Check("Private WithEvents Src As Source\r\nPrivate Sub Src_Other(ByVal Anything As String)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AProcedureNamedForAVariableThatIsNotWithEvents_IsNoHandler()
        => Assert.IsEmpty(Check("Private Plain As Source\r\nPrivate Sub Plain_Changed()\r\nEnd Sub\r\n"));

    [TestMethod]
    public void EachInvalidHandler_IsReportedOnItsOwn()
    {
        var errors = Check(
            "Private WithEvents Src As Source\r\n" +
            "Private Sub Src_Changed()\r\nEnd Sub\r\n" +
            "Private Sub Src_Bump(ByVal Count As Long)\r\nEnd Sub\r\n");

        Assert.HasCount(2, errors);
        Assert.IsTrue(errors.All(error => error.VBCompileErrorId == VBCompileErrorId.InvalidEventHandler));
    }

    #endregion
}

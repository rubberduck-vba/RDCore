using NSubstitute;
using RDCore.Runtime.Execution.External;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// 🎯 Every call to something outside the workspace passes an ordered set of interceptors before any provider
/// runs it, and one refusal stops it. A VBA environment that can call into native libraries is a way to run
/// arbitrary code, and the historical answer has been for administrators to disable the whole language
/// because nothing could see what a macro did; these are the assertions that say something can.
/// </summary>
[TestClass]
public sealed class ExternalCallPipelineTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static IRuntimeSession Session(bool allowDllImports = true)
    {
        var session = Substitute.For<IRuntimeSession>();
        session.Environment.Returns(new RuntimeEnvironmentProfile(
            Is64Bit: true, 0, 1252, false, AllowDllImports: allowDllImports));
        return session;
    }

    private static VBExternalFunctionMemberSymbol LibraryImport(string name = "Sleep", string library = "kernel32", string? alias = null)
        => new(Root, Root, name, ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public, IsPtrSafe: true, library, alias);

    private static VBProcedureMemberSymbol WorkspaceLikeMember(string name = "DoWork")
        => new(Root, Root, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

    private sealed class RecordingInterceptor(ExternalCallDecision decision) : IExternalCallInterceptor
    {
        public List<string> Seen { get; } = [];

        public ExternalCallDecision Intercept(ExternalCallRequest request, IRuntimeSession session)
        {
            Seen.Add(request.Describe());
            return decision;
        }
    }

    private sealed class AlwaysProvider : IExternalCallProvider
    {
        public bool Dispatched { get; private set; }

        public bool CanDispatch(ExternalCallRequest request) => true;

        public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
        {
            Dispatched = true;
            return RuntimeSemanticsEvaluationResult.Success(new VBLongValue(1));
        }
    }

    private static RuntimeSemanticsEvaluationResult Invoke(
        IRuntimeSession session,
        VBTypeMemberSymbol member,
        IExternalCallProvider? provider = null,
        IExternalCallInterceptor? interceptor = null,
        params IRuntimeValue[] arguments)
        => ExternalCallPipeline
            .For(session, provider is null ? [] : [provider], interceptor is null ? null : [interceptor])
            .Invoke(new ExternalCallRequest(member, arguments), Substitute.For<ISymbolResolver>());

    [TestMethod]
    public void WithLibraryImportsDisabled_ALibraryCall_IsRefused()
    {
        // the setting an administrator has that needs no extension installed to work.
        var provider = new AlwaysProvider();

        var result = Invoke(Session(allowDllImports: false), LibraryImport(), provider);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.PermissionDenied, result.ErrorInfo!.ErrorId);
        Assert.IsFalse(provider.Dispatched, "a refused call never reaches a provider");
    }

    [TestMethod]
    public void ARefusal_SaysWhichCallAndOnWhatGrounds()
    {
        // the difference between a diagnostic and a shrug: the library, the entry point, and the arguments.
        var result = Invoke(
            Session(allowDllImports: false),
            LibraryImport("Sleep", "kernel32"),
            new AlwaysProvider(),
            interceptor: null,
            new VBLongValue(5000).RuntimeValue);

        Assert.Contains("kernel32!Sleep(5000)", result.ErrorInfo!.Verbose);
        Assert.Contains("disabled for this environment", result.ErrorInfo.Verbose);
    }

    [TestMethod]
    public void ARefusal_NamesTheEntryPointALiasReallyReaches()
    {
        // an Alias is the exported name, and the VBA name may be nothing like it - a log that recorded only
        // the VBA name would not say what was actually called.
        var result = Invoke(
            Session(allowDllImports: false),
            LibraryImport("GetFile", "kernel32", alias: "CreateFileW"),
            new AlwaysProvider());

        Assert.Contains("kernel32!CreateFileW", result.ErrorInfo!.Verbose);
    }

    [TestMethod]
    public void WithLibraryImportsAllowed_ALibraryCall_ReachesTheProvider()
    {
        var provider = new AlwaysProvider();

        var result = Invoke(Session(allowDllImports: true), LibraryImport(), provider);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(provider.Dispatched);
    }

    [TestMethod]
    public void TheSetting_HasNoOpinionAboutAnythingButLibraryImports()
    {
        // automating a host application is not the same risk as calling an arbitrary export, and one switch
        // for both would be useless to anyone who needs one and not the other.
        var provider = new AlwaysProvider();

        Assert.IsTrue(Invoke(Session(allowDllImports: false), WorkspaceLikeMember(), provider).IsSuccess);
        Assert.IsTrue(provider.Dispatched);
    }

    [TestMethod]
    public void AnInterceptor_SeesEveryCall_WithItsArguments()
    {
        // what makes logging possible: the call describes itself before it happens.
        var interceptor = new RecordingInterceptor(ExternalCallDecision.Allow);

        Invoke(Session(), LibraryImport("Sleep", "kernel32"), new AlwaysProvider(), interceptor, new VBLongValue(250).RuntimeValue);

        Assert.AreEqual("kernel32!Sleep(250)", interceptor.Seen.Single());
    }

    [TestMethod]
    public void AnInterceptorThatRefuses_StopsTheCall()
    {
        // the mechanism a policy extension uses, with no setting involved.
        var provider = new AlwaysProvider();
        var interceptor = new RecordingInterceptor(ExternalCallDecision.Block("this one ain't going through"));

        var result = Invoke(Session(), LibraryImport(), provider, interceptor);

        Assert.AreEqual((int)VBRuntimeErrorId.PermissionDenied, result.ErrorInfo!.ErrorId);
        Assert.Contains("this one ain't going through", result.ErrorInfo.Verbose);
        Assert.IsFalse(provider.Dispatched);
    }

    [TestMethod]
    public void ALibraryCallWithNoProvider_ReportsThatTheLibraryCouldNotBeCalled()
    {
        // where a Declare lands today: allowed, and nothing on this platform can run it. Error 48 is what VBA
        // says when a library call could not be made.
        var result = Invoke(Session(allowDllImports: true), LibraryImport());

        Assert.AreEqual((int)VBRuntimeErrorId.ErrorInLoadingDll, result.ErrorInfo!.ErrorId);
        Assert.Contains("no provider for library imports", result.ErrorInfo.Verbose);
    }

    [TestMethod]
    public void TheEnvironmentPolicy_RunsBeforeAnyOtherInterceptor()
    {
        // so that nothing an extension does can let through what an administrator refused: the refusal is the
        // platform's, and the extension never gets asked.
        var interceptor = new RecordingInterceptor(ExternalCallDecision.Allow);

        Invoke(Session(allowDllImports: false), LibraryImport(), new AlwaysProvider(), interceptor);

        Assert.IsEmpty(interceptor.Seen);
    }
}

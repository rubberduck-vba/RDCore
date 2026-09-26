using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Which engine a member is bound to. One fact decides it: a member of the workspace has an instruction
/// list somewhere, and a member of anything else carries the key of the declaration it came from
/// (<see cref="SymbolProperties.ExternalTarget"/>). Every call site asks for a binding and invokes it, so
/// none of them carries that decision.
/// </summary>
[TestClass]
public sealed class RuntimeCallableBindingFactoryTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static VBProcedureMemberSymbol Procedure(string name = "DoWork")
        => new(Root, Root, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

    private static RuntimeCallableBindingFactory Factory(bool withExternal = true)
        => new(Substitute.For<IProcedureInvoker>(), withExternal ? Substitute.For<IExternalDispatcher>() : null);

    [TestMethod]
    public void AMemberOfTheWorkspace_BindsToTheInvoker()
    {
        var binding = Factory().ForMember(Procedure());

        Assert.IsInstanceOfType<CallableBindingHandle>(binding);
    }

    [TestMethod]
    public void AMemberWithAnExternalTarget_BindsToTheDispatcher()
    {
        var member = (VBProcedureMemberSymbol)Procedure("Erl").With(SymbolProperties.ExternalTarget, "IStdInformationModule.Erl/0");

        var binding = Factory().ForMember(member);

        Assert.IsInstanceOfType<ExternalBindingHandle>(binding);
    }

    [TestMethod]
    public void AnExternalMember_WithNoDispatcher_BindsToTheInvoker()
    {
        // a session composed without a standard library. It reports the missing procedure it is, rather than
        // binding to nothing and failing somewhere less obvious.
        var member = (VBProcedureMemberSymbol)Procedure("Erl").With(SymbolProperties.ExternalTarget, "IStdInformationModule.Erl/0");

        var binding = Factory(withExternal: false).ForMember(member);

        Assert.IsInstanceOfType<CallableBindingHandle>(binding);
    }

    [TestMethod]
    public void EitherBinding_CarriesTheMemberItInvokes()
    {
        // what makes them interchangeable at a call site: ICallableBinding says which member, whichever
        // engine is behind it.
        var external = (VBProcedureMemberSymbol)Procedure("Erl").With(SymbolProperties.ExternalTarget, "IStdInformationModule.Erl/0");

        Assert.AreEqual("DoWork", Factory().ForMember(Procedure()).Member.Name);
        Assert.AreEqual("Erl", Factory().ForMember(external).Member.Name);
    }

    [TestMethod]
    public void AReceiver_IsCarriedByTheBinding_NotByTheCallSite()
    {
        // the Me of the call. Both bindings pass it themselves at argument index 0, so a call site hands over
        // only the arguments source wrote.
        var member = (VBProcedureMemberSymbol)Procedure("Number").With(SymbolProperties.ExternalTarget, "IStdErrClass.Number/0");
        var receiver = Substitute.For<SDK.Model.Values.Runtime.IRuntimeValue>();

        var binding = (ExternalBindingHandle)Factory().ForMember(member, receiver);

        Assert.AreSame(receiver, binding.Receiver);
    }
}

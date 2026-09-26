using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Byte-level access to a session's memory: what <c>PEEK</c> and <c>POKE</c> are built on. A value
/// bound at an address really does occupy bytes there, and writing one of them really does change the
/// value — unchecked, which is the point.
/// </summary>
[TestClass]
public sealed class SessionMemoryAccessTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri ModuleUri = TestUri.TestModuleUri();
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    /// <summary>
    /// Composes a session holding one module field of the given type, and hands back the address its
    /// storage was allocated at.
    /// </summary>
    private static (IRuntimeSession Session, Symbol Field, MemoryAddress Address) WithField(VBType type)
    {
        var field = new VBModuleFieldVariableMemberSymbol(Root, ModuleUri, "Value", ScopeKind.Module, type, R, R, AccessModifier.Implicit);
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider([field]));

        Assert.IsTrue(session.Symbols.Resolver.TryGetAddress(field, out var address),
            "a module field is allocated storage when it is defined");
        return (session, field, address);
    }

    private static object? Read(IRuntimeSession session, Symbol field)
        => session.Symbols.Resolver.GetValue(field).Value.BoxedValue;

    [TestMethod]
    public void Peek_ReadsTheBytesOfALiveValue_LeastSignificantFirst()
    {
        var (session, field, address) = WithField(VBLongType.TypeInfo);
        session.Storage.TryPoke(address, 0x2A);

        Assert.IsTrue(session.Storage.TryPeek(address, out var first));
        Assert.IsTrue(session.Storage.TryPeek(address + 1, out var second));
        Assert.AreEqual(0x2A, first);
        Assert.AreEqual(0, second);
    }

    [TestMethod]
    public void Poke_ChangesTheValueTheNameResolvesTo()
    {
        // the whole point: the byte and the variable are the same storage, not two views that have to
        // be kept in step.
        var (session, field, address) = WithField(VBLongType.TypeInfo);

        Assert.IsTrue(session.Storage.TryPoke(address, 65));

        Assert.AreEqual(65, Read(session, field));
    }

    [TestMethod]
    public void Poke_IntoTheMiddleOfAValue_ChangesTheWholeValue()
    {
        // unchecked: byte 1 of a Long is the 256s, so writing 1 there makes the Long 256.
        var (session, field, address) = WithField(VBLongType.TypeInfo);

        Assert.IsTrue(session.Storage.TryPoke(address + 1, 1));

        Assert.AreEqual(256, Read(session, field));
    }

    [TestMethod]
    public void ABoolean_OccupiesTwoBytes_AndAnyNonZeroPatternIsTrue()
    {
        // MS-VBAL §2.1: Boolean is two bytes, and True is -1, not 1.
        var (session, field, address) = WithField(VBBooleanType.TypeInfo);

        Assert.IsTrue(session.Storage.TryPoke(address, 1));

        Assert.AreEqual(-1, Read(session, field));
        Assert.IsTrue(session.Storage.TryPeek(address + 1, out var high));
        Assert.AreEqual(0, high);
    }

    [TestMethod]
    public void AnAddressWithNothingAllocatedAtIt_IsReportedAsSuch_NotGuessedAt()
    {
        var (session, _, address) = WithField(VBLongType.TypeInfo);

        // well past the one allocation the session has made.
        Assert.IsFalse(session.Storage.TryPeek(address + 1000, out var value));
        Assert.AreEqual(0, value);
        Assert.IsFalse(session.Storage.TryPoke(address + 1000, 1));
    }

    [TestMethod]
    public void Peek_PastTheValueButInsideItsAllocation_ReadsZero()
    {
        // an Integer is two bytes; a read of the rest of whatever was reserved for it is a read of
        // memory that exists and holds nothing, which is not the same as an unallocated address.
        var (session, _, address) = WithField(VBIntegerType.TypeInfo);

        Assert.IsTrue(session.Storage.TryPeek(address + 1, out _));
    }
}

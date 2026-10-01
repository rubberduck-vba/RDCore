using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Redefining a symbol in a live session: a module read again replaces the definitions the session holds, and a
/// variable that is declared as it was keeps what it holds. <see cref="ISessionSymbols.TryUndefine"/> is the other
/// way to replace one, and discards the value — a redefinition, not a rename.
/// </summary>
[TestClass]
public sealed class SessionRedefinitionTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();
    private static readonly Uri ModuleUri = TestUri.TestModuleUri();

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession Session()
        => RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new Provider([new VBStandardModuleSymbol(Root, Root, "TestModule1")]));

    private static VBModuleFieldVariableMemberSymbol Field(string name, VBType type)
        => new(Root, ModuleUri, name, ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static object? ValueOf(IRuntimeSession session, Symbol symbol)
        => session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue;

    [TestMethod]
    public void AVariableDeclaredAsItWas_KeepsItsValue()
    {
        var session = Session();
        var before = Field("Counter", VBLongType.TypeInfo);
        Assert.IsTrue(session.Symbols.TryDefine(before, ScopeKind.Module));
        session.Symbols.Resolver.GetValue(before).SetValue(session.Symbols.Resolver, new VBRuntimeValue<int>(7));

        // written again somewhere else in the module, which is all that changed.
        var after = before with { Range = new SourceRange(new SourcePosition(3, 0), new SourcePosition(3, 20)) };
        Assert.IsTrue(session.Symbols.TryRedefine(after, ScopeKind.Module));

        Assert.AreEqual(7, ValueOf(session, after));
    }

    [TestMethod]
    public void AVariableDeclaredAsAnotherType_IsAnotherVariable_AndStartsAtItsDefault()
    {
        var session = Session();
        var before = Field("Counter", VBLongType.TypeInfo);
        Assert.IsTrue(session.Symbols.TryDefine(before, ScopeKind.Module));
        session.Symbols.Resolver.GetValue(before).SetValue(session.Symbols.Resolver, new VBRuntimeValue<int>(7));

        var after = Field("Counter", VBStringType.TypeInfo);
        Assert.IsTrue(session.Symbols.TryRedefine(after, ScopeKind.Module));

        // a String's own default is vbNullString, which is no 7 and not even a number: it is the new type's.
        Assert.IsNull(ValueOf(session, after));
    }

    [TestMethod]
    public void ASymbolThatWasNeverDefined_IsNotRedefined()
        => Assert.IsFalse(Session().Symbols.TryRedefine(Field("Counter", VBLongType.TypeInfo), ScopeKind.Module));

    [TestMethod]
    public void UndefiningAndDefiningAgain_StillDiscardsTheValue()
    {
        // the contrast that makes TryRedefine its own operation: replacing the long way is a new variable.
        var session = Session();
        var counter = Field("Counter", VBLongType.TypeInfo);
        Assert.IsTrue(session.Symbols.TryDefine(counter, ScopeKind.Module));
        session.Symbols.Resolver.GetValue(counter).SetValue(session.Symbols.Resolver, new VBRuntimeValue<int>(7));

        Assert.IsTrue(session.Symbols.TryUndefine(counter, ScopeKind.Module));
        Assert.IsTrue(session.Symbols.TryDefine(counter, ScopeKind.Module));

        Assert.AreEqual(0, ValueOf(session, counter));
    }
}

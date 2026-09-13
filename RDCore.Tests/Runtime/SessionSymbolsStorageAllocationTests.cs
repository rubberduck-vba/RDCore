using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime;

/// <summary>
/// rubberduck-vba/RDCore#124 — a program-lifetime declaration (a standard module's or the global
/// scope's field/variable) is allocated run-time storage the moment it's bound, so
/// <c>ISessionSymbols.Resolver.GetValue</c> can read it back. A local, an instance field, and a
/// <c>Const</c> are deliberately excluded: locals live on the call stack frame, instance fields need
/// a live object to allocate into, and a constant is a compile-time substitution, never an address.
/// </summary>
[TestClass]
public sealed class SessionSymbolsStorageAllocationTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static ISessionSymbols Compose(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols)).Symbols;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol ModuleField(Uri moduleUri, string name)
        => new(Root, moduleUri, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

    [TestMethod]
    public void ModuleField_IsAllocatedStorage_ReadableThroughTheResolver()
    {
        var module = Module("Mod1");
        var field = ModuleField(module.Uri, "Total");
        var symbols = Compose(module, field);

        var handle = symbols.Resolver.GetValue(field);

        Assert.AreSame(VBLongType.TypeInfo.DefaultValue.Handle, handle, "should hold the type's default value until assigned");
    }

    [TestMethod]
    public void GlobalScopeVariable_IsAllocatedStorage_ReadableThroughTheResolver()
    {
        // no production symbol provider yields a Global-scope Field/Variable today (Global is
        // reserved for StaticSymbol/library-provided members, mostly Unallocated pseudo-symbols) —
        // this exercises the scope directly per its own documented "lives in the globals heap" intent.
        var global = new VBModuleFieldVariableMemberSymbol(Root, Root, "MaxUsers", ScopeKind.Global, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var symbols = Compose(global);

        var handle = symbols.Resolver.GetValue(global);

        Assert.AreSame(VBLongType.TypeInfo.DefaultValue.Handle, handle);
    }

    [TestMethod]
    public void InstanceField_IsNotAllocated_NeedsALiveObjectFirst()
    {
        var field = new VBInstanceFieldVariableMemberSymbol(Root, Root, "State", R, R, VBLongType.TypeInfo, AccessModifier.Implicit);
        var symbols = Compose(field);

        Assert.ThrowsExactly<KeyNotFoundException>(() => symbols.Resolver.GetValue(field));
    }

    [TestMethod]
    public void ProcedureLocal_IsNotAllocatedHere_BelongsToTheCallStackFrame()
    {
        var local = new VBLocalVariableSymbol(Root, Root, "i", ScopeKind.Local, R, R, ResolvedType: VBLongType.TypeInfo);
        var symbols = Compose(local);

        Assert.ThrowsExactly<KeyNotFoundException>(() => symbols.Resolver.GetValue(local));
    }

    [TestMethod]
    public void ModuleConstant_IsNotAllocated_ItsAComptimeSubstitutionNotAnAddress()
    {
        var module = Module("Mod1");
        var constant = new VBConstantMemberSymbol(Root, module.Uri, "Pi", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var symbols = Compose(module, constant);

        Assert.ThrowsExactly<KeyNotFoundException>(() => symbols.Resolver.GetValue(constant));
    }

    [TestMethod]
    public void TheModuleSymbolItself_IsNotAllocated_OnlyItsMembersHoldValues()
    {
        var module = Module("Mod1");
        var symbols = Compose(module);

        Assert.ThrowsExactly<KeyNotFoundException>(() => symbols.Resolver.GetValue(module));
    }

    [TestMethod]
    public void RedefiningTheSameFieldInstance_DoesNotThrow_AndStaysReadable()
    {
        var module = Module("Mod1");
        var field = ModuleField(module.Uri, "Total");
        var symbols = Compose(module);

        Assert.IsTrue(symbols.TryDefine(field, ScopeKind.Module));
        Assert.IsFalse(symbols.TryDefine(field, ScopeKind.Module), "an identical redefinition is a no-op, not a re-allocation");

        Assert.AreSame(VBLongType.TypeInfo.DefaultValue.Handle, symbols.Resolver.GetValue(field));
    }

    [TestMethod]
    public void TwoModuleFields_BothResolveIndependently()
    {
        var module = Module("Mod1");
        var first = ModuleField(module.Uri, "First");
        var second = ModuleField(module.Uri, "Second");
        var symbols = Compose(module, first, second);

        Assert.IsNotNull(symbols.Resolver.GetValue(first));
        Assert.IsNotNull(symbols.Resolver.GetValue(second));
    }
}

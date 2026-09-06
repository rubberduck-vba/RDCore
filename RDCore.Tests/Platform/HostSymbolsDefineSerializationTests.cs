using System.Collections.Immutable;
using System.Text.Json;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

/// <summary>
/// <see cref="DefineSymbolsParams"/> crosses the language-server → environment-host boundary as JSON.
/// </summary>
[TestClass]
public sealed class HostSymbolsDefineSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static DefineSymbolsParams Sample() => new()
    {
        WorkspaceRoot = new Uri("file:///c:/ws/"),
        ModuleUri = new Uri("file:///c:/ws/#Mod1"),
        ModuleName = "Mod1",
        Symbols =
        [
            new SymbolDescriptor
            {
                Name = "Add",
                Kind = SymbolDescriptorKind.Function,
                AccessModifier = AccessModifier.Public,
                Scope = ScopeKind.Instance,
                DeclaredTypeName = "Long",
                Range = new SourceRange(3, 0, 6, 11),
                SelectionRange = new SourceRange(3, 15, 3, 18),
                Parameters =
                [
                    new ParameterDescriptor { Name = "a", ParameterKind = ParameterKind.ExplicitByVal, DeclaredTypeName = "Long", Range = new SourceRange(3, 19, 3, 30) },
                    new ParameterDescriptor { Name = "rest", IsParamArray = true, Range = new SourceRange(3, 32, 3, 50) },
                ],
            },
            new SymbolDescriptor
            {
                Name = "Direction",
                Kind = SymbolDescriptorKind.Enum,
                AccessModifier = AccessModifier.Private,
                Range = new SourceRange(8, 0, 11, 8),
                SelectionRange = new SourceRange(8, 13, 8, 22),
                Members =
                [
                    new SymbolDescriptor { Name = "North", Kind = SymbolDescriptorKind.EnumMember, Range = new SourceRange(9, 4, 9, 9) },
                    new SymbolDescriptor { Name = "South", Kind = SymbolDescriptorKind.EnumMember, Range = new SourceRange(10, 4, 10, 9) },
                ],
            },
            new SymbolDescriptor
            {
                Name = "GetTickCount",
                Kind = SymbolDescriptorKind.ExternalFunction,
                AccessModifier = AccessModifier.Public,
                Scope = ScopeKind.External,
                DeclaredTypeName = "Long",
                Range = new SourceRange(13, 0, 13, 80),
                SelectionRange = new SourceRange(13, 40, 13, 52),
                External = new ExternalDescriptor { IsPtrSafe = true, Library = "kernel32", Alias = "GetTickCount64" },
            },
            new SymbolDescriptor
            {
                Name = "MaxItems",
                Kind = SymbolDescriptorKind.ModuleConstant,
                AccessModifier = AccessModifier.Private,
                Scope = ScopeKind.Module,
                DeclaredTypeName = "Integer",
                Range = new SourceRange(1, 0, 1, 30),
                SelectionRange = new SourceRange(1, 14, 1, 22),
            },
        ],
    };

    [TestMethod]
    public void DefineSymbolsParams_RoundTripsThroughJson()
    {
        var original = Sample();

        var once = JsonSerializer.Deserialize<DefineSymbolsParams>(JsonSerializer.Serialize(original, Options), Options)!;
        var twice = JsonSerializer.Deserialize<DefineSymbolsParams>(JsonSerializer.Serialize(once, Options), Options)!;

        Assert.AreEqual(
            JsonSerializer.Serialize(once, Options),
            JsonSerializer.Serialize(twice, Options),
            "serialization is not stable across a round trip");

        Assert.AreEqual("Mod1", once.ModuleName);
        Assert.AreEqual(4, once.Symbols.Length);

        var add = once.Symbols[0];
        Assert.AreEqual(SymbolDescriptorKind.Function, add.Kind);
        Assert.AreEqual("Long", add.DeclaredTypeName);
        Assert.AreEqual(2, add.Parameters.Length);
        Assert.AreEqual(ParameterKind.ExplicitByVal, add.Parameters[0].ParameterKind);
        Assert.IsTrue(add.Parameters[1].IsParamArray);
        Assert.AreEqual(new SourceRange(3, 0, 6, 11), add.Range);

        Assert.AreEqual(2, once.Symbols[1].Members.Length);
        Assert.AreEqual("North", once.Symbols[1].Members[0].Name);

        Assert.AreEqual("kernel32", once.Symbols[2].External!.Library);
        Assert.AreEqual("GetTickCount64", once.Symbols[2].External!.Alias);
        Assert.IsTrue(once.Symbols[2].External!.IsPtrSafe);

        Assert.AreEqual(SymbolDescriptorKind.ModuleConstant, once.Symbols[3].Kind);
        Assert.AreEqual(ScopeKind.Module, once.Symbols[3].Scope);
    }

    [TestMethod]
    public void DefineSymbolsResult_RoundTripsThroughJson()
    {
        var original = new DefineSymbolsResult
        {
            Defined = 7,
            Skipped = ["Foo", "Bar"],
            UnresolvedTypeNames = ["Widget"],
        };

        var result = JsonSerializer.Deserialize<DefineSymbolsResult>(JsonSerializer.Serialize(original, Options), Options)!;

        Assert.AreEqual(7, result.Defined);
        CollectionAssert.AreEqual(new[] { "Foo", "Bar" }, result.Skipped.ToArray());
        CollectionAssert.AreEqual(new[] { "Widget" }, result.UnresolvedTypeNames.ToArray());
    }

    [TestMethod]
    public void EmptySymbols_SerializesAndDeserializes()
    {
        var empty = new DefineSymbolsParams { ModuleName = "Empty", Symbols = ImmutableArray<SymbolDescriptor>.Empty };

        var result = JsonSerializer.Deserialize<DefineSymbolsParams>(JsonSerializer.Serialize(empty, Options), Options)!;

        Assert.AreEqual("Empty", result.ModuleName);
        Assert.IsTrue(result.Symbols.IsDefaultOrEmpty);
    }
}

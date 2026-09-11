using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The workspace resolver is composed over every parsed module, so a module's <c>As SomeType</c>
/// binds to a sibling module's <c>Type</c> / <c>Enum</c> — not just an intrinsic type name.
/// </summary>
[TestClass]
public sealed class WorkspaceSymbolResolverTests
{
    private static readonly Uri WorkspaceRoot = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(WorkspaceRoot) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleParseResult Parse) Module(string name, string body)
        => (ModuleUri(name), new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.bas"), ModuleType.StdModule, $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static List<Symbol> Resolve(string moduleName, string moduleBody, params (Uri Uri, ModuleParseResult Parse)[] siblings)
    {
        var target = Module(moduleName, moduleBody);
        var resolver = WorkspaceSymbolResolver.Compose(
            WorkspaceRoot, [.. siblings, target], new IntrinsicSymbolResolver());

        return [.. new SyntaxTreeSymbolProvider(WorkspaceRoot, target.Uri, target.Parse, resolver).ProvideSymbols()];
    }

    [TestMethod]
    public void ASiblingModulesUserDefinedType_BindsThroughTheWorkspaceResolver()
    {
        var types = Module("Types", "Public Type TPoint\r\n    X As Long\r\n    Y As Long\r\nEnd Type\r\n");

        var field = Resolve("Consumer", "Public Origin As TPoint\r\n", types)
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        var udt = Assert.IsInstanceOfType<VBUserDefinedType>(field.ResolvedType);
        Assert.AreEqual("TPoint", udt.Name);
        Assert.HasCount(2, udt.Members);
    }

    [TestMethod]
    public void ASiblingModulesEnum_BindsThroughTheWorkspaceResolver()
    {
        var enums = Module("Enums", "Public Enum Colour\r\n    Red\r\n    Green\r\nEnd Enum\r\n");

        var field = Resolve("Consumer", "Public Selected As Colour\r\n", enums)
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        Assert.AreEqual("Colour", Assert.IsInstanceOfType<VBEnumType>(field.ResolvedType).Name);
    }

    [TestMethod]
    public void AnIntrinsicTypeName_StillBinds_ThroughTheFallback()
    {
        var field = Resolve("Consumer", "Public Total As Long\r\n")
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        Assert.AreEqual(VBTypeNames.VBLong, field.ResolvedType.Name);
    }

    [TestMethod]
    public void AnUnknownTypeName_StaysUnknown()
    {
        var field = Resolve("Consumer", "Public Widget As CWidget\r\n")
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        Assert.AreEqual(VBTypeNames.VBUnknown, field.ResolvedType.Name);
    }
}

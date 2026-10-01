using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a class module's events and automatic instantiation variables are once they have travelled to the
/// environment host as descriptors and been read back: the host runs them, so it has to be told.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1.2 WithEvents Variable Declarations")]
public sealed class EventDescriptorRoundTripTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Symbol[] RoundTrip(string body)
    {
        var moduleUri = new UriBuilder(Root) { Fragment = "Sink" }.Uri;
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Sink.cls"), $"Attribute VB_Name = \"Sink\"\r\n{body}");
        var declared = new SyntaxTreeSymbolProvider(
            Root, moduleUri, ModuleType.ClassModule, parse, new IntrinsicSymbolResolver(), withImplicitDeclarations: false).ProvideSymbols();

        return [.. SymbolDescriptorReader.Read(
            new DefineSymbolsParams
            {
                WorkspaceRoot = Root,
                ModuleUri = moduleUri,
                ModuleName = "Sink",
                Symbols = SymbolDescriptorProjector.Project(declared, moduleUri),
            },
            typeName => IntrinsicVBTypes.TryResolve(typeName, out var type) ? type : null)];
    }

    [TestMethod]
    public void AWithEventsField_IsStillWithEvents()
    {
        var read = RoundTrip("Private WithEvents Src As Object\r\nPrivate Plain As Object\r\n");

        Assert.IsTrue(read.Single(symbol => symbol.Name == "Src").GetProperty(SymbolProperties.WithEvents));
        Assert.IsFalse(read.Single(symbol => symbol.Name == "Plain").GetProperty(SymbolProperties.WithEvents));
    }

    [TestMethod]
    public void AnAsNewField_IsStillAutoInstantiated()
    {
        var read = RoundTrip("Private Auto As New Collection\r\nPrivate Plain As Object\r\n");

        Assert.IsTrue(read.Single(symbol => symbol.Name == "Auto").GetProperty(SymbolProperties.AutoInstantiated));
        Assert.IsFalse(read.Single(symbol => symbol.Name == "Plain").GetProperty(SymbolProperties.AutoInstantiated));
    }

    [TestMethod]
    public void AnAsNewLocal_IsStillAutoInstantiated()
    {
        var read = RoundTrip("Public Sub Run()\r\nDim Auto As New Collection\r\nDim Plain As Object\r\nEnd Sub\r\n");

        var locals = read.OfType<VBProcedureMemberSymbol>().Single().Locals;
        Assert.IsTrue(locals.Single(local => local.Name == "Auto").GetProperty(SymbolProperties.AutoInstantiated));
        Assert.IsFalse(locals.Single(local => local.Name == "Plain").GetProperty(SymbolProperties.AutoInstantiated));
    }

    [TestMethod]
    public void AnEvent_ArrivesWithItsParameters()
    {
        var read = RoundTrip("Public Event Changed(ByVal Value As Long, Name As String)\r\n");

        var declared = read.OfType<VBEventMemberSymbol>().Single();
        CollectionAssert.AreEqual(new[] { "Value", "Name" }, declared.Parameters.Select(parameter => parameter.Name).ToArray());
        Assert.AreEqual(ParameterKind.ExplicitByVal, declared.Parameters[0].ParameterKind);
        Assert.AreEqual(VBLongType.TypeInfo, declared.Parameters[0].ResolvedType);
    }
}

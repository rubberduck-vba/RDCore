using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Tests.Model;

[TestClass]
public class SymbolKindExtTests
{
    [TestMethod]
    [DataRow(SymbolKindExt.Module, SymbolKind.Module)]
    [DataRow(SymbolKindExt.Project, SymbolKind.Namespace)]
    [DataRow(SymbolKindExt.Class, SymbolKind.Class)]
    [DataRow(SymbolKindExt.Procedure, SymbolKind.Method)]
    [DataRow(SymbolKindExt.Property, SymbolKind.Property)]
    [DataRow(SymbolKindExt.Field, SymbolKind.Field)]
    [DataRow(SymbolKindExt.Enum, SymbolKind.Enum)]
    [DataRow(SymbolKindExt.Interface, SymbolKind.Interface)]
    [DataRow(SymbolKindExt.Function, SymbolKind.Function)]
    [DataRow(SymbolKindExt.Variable, SymbolKind.Variable)]
    [DataRow(SymbolKindExt.Constant, SymbolKind.Constant)]
    [DataRow(SymbolKindExt.StringLiteral, SymbolKind.String)]
    [DataRow(SymbolKindExt.NumberLiteral, SymbolKind.Number)]
    [DataRow(SymbolKindExt.BooleanLiteral, SymbolKind.Boolean)]
    [DataRow(SymbolKindExt.Array, SymbolKind.Array)]
    [DataRow(SymbolKindExt.Object, SymbolKind.Object)]
    [DataRow(SymbolKindExt.Key, SymbolKind.Key)]
    [DataRow(SymbolKindExt.Null, SymbolKind.Null)]
    [DataRow(SymbolKindExt.EnumMember, SymbolKind.EnumMember)]
    [DataRow(SymbolKindExt.UserDefinedType, SymbolKind.Struct)]
    [DataRow(SymbolKindExt.Event, SymbolKind.Event)]
    [DataRow(SymbolKindExt.Operator, SymbolKind.Operator)]
    public void LspValue_ConvertsToProtocolSymbolKind(SymbolKindExt kind, SymbolKind expected)
    {
        Assert.AreEqual(expected, kind.ToLsp());
    }

    [TestMethod]
    public void LspValues_AreAllDefinedProtocolValues()
    {
        var lspValues = Enum.GetValues<SymbolKindExt>().Where(kind => (int)kind < 128);
        foreach (var kind in lspValues)
        {
            Assert.IsTrue(Enum.IsDefined(kind.ToLsp()), $"{kind} ({(int)kind}) is not a defined LSP SymbolKind value.");
        }
    }

    [TestMethod]
    public void ExtensionValues_DoNotCollideWithProtocolValues()
    {
        var extensionValues = Enum.GetValues<SymbolKindExt>().Where(kind => (int)kind >= 128);
        foreach (var kind in extensionValues)
        {
            Assert.IsFalse(Enum.IsDefined(kind.ToLsp()), $"{kind} ({(int)kind}) collides with a defined LSP SymbolKind value.");
        }
    }
}

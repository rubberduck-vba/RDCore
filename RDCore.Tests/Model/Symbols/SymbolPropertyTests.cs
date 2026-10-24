using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// A key of a symbol's properties is its name and the type of its value: a symbol read back from elsewhere carries keys of its own, which are the same keys.
/// </summary>
[TestClass]
public sealed class SymbolPropertyTests
{
    [TestMethod]
    public void TwoKeysOfTheSameNameAndType_AreTheSameKey()
    {
        var key = new SymbolProperty<string>(nameof(SymbolProperties.ExternalTarget));

        Assert.AreEqual(SymbolProperties.ExternalTarget, key);
        Assert.AreEqual(SymbolProperties.ExternalTarget.GetHashCode(), key.GetHashCode());
    }

    [TestMethod]
    public void TwoKeysOfTheSameName_AndOfTypesThatDiffer_AreNot()
        => Assert.AreNotEqual<object>(new SymbolProperty<string>("Key"), new SymbolProperty<int>("Key"));

    [TestMethod]
    public void AKeyOfItsOwn_ReadsThePropertyItNames()
    {
        var symbol = new SDK.Model.Symbols.VBStandardModuleSymbol(new Uri("file://rdcore-test"), new Uri("file://rdcore-test"), "Module1")
            .With(SymbolProperties.ExternalTarget, "IStdStringsModule.Len/1");

        Assert.AreEqual("IStdStringsModule.Len/1", symbol.GetProperty(new SymbolProperty<string>(nameof(SymbolProperties.ExternalTarget))));
    }
}

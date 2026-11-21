using RDCore.Runtime.LanguageCore.StdLib;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// A function of the standard library states its <c>$</c> form where the specification says that form is the same function returning a String
/// (MS-VBAL §6.1.2), and only there.
/// </summary>
[TestClass]
public sealed class StdLibStringFormTests
{
    private static readonly List<Symbol> Symbols = [.. new StdLibSymbolProvider(new Uri("file:///c:/ws/")).ProvideSymbols()];

    // the form each symbol of the name states: a property's Get and Let are two symbols.
    private static string?[] StringFormOf(string module, string name)
    {
        var owner = Symbols.Single(symbol => symbol is VBStandardModuleSymbol && symbol.Name == module);
        return [.. Symbols.Where(symbol => symbol.ParentUri == owner.Uri && symbol.Name == name)
            .Select(symbol => symbol.GetProperty(SymbolProperties.StringForm))];
    }

    [TestMethod]
    [DataRow("Strings", "Left", "Left$")]
    [DataRow("Strings", "MidB", "MidB$")]
    [DataRow("Strings", "String", "String$")]
    [DataRow("Conversion", "Hex", "Hex$")]
    [DataRow("Conversion", "Error", "Error$")]
    [DataRow("FileSystem", "CurDir", "CurDir$")]
    [DataRow("Interaction", "Environ", "Environ$")]
    public void AFunctionWithAStringForm_StatesIt(string module, string name, string form)
        => Assert.AreEqual(form, Assert.ContainsSingle(StringFormOf(module, name)));

    [TestMethod]
    [DataRow("DateTime", "Date", DisplayName = "Date$ is text of its own form")]
    [DataRow("DateTime", "Time", DisplayName = "Time$ is text of its own form")]
    [DataRow("Strings", "Left$", DisplayName = "the String form itself")]
    [DataRow("Strings", "Len", DisplayName = "no $ form")]
    public void AnyOtherMember_StatesNone(string module, string name)
    {
        var forms = StringFormOf(module, name);
        Assert.IsNotEmpty(forms);
        Assert.IsTrue(forms.All(form => form is null), string.Join(", ", forms));
    }

    // every member that states a form names a member of its module, which returns a String.
    [TestMethod]
    public void EveryStatedForm_IsAStringFunctionOfTheSameModule()
    {
        var stated = Symbols.Where(symbol => symbol.GetProperty(SymbolProperties.StringForm) is not null).ToArray();

        Assert.AreEqual(26, stated.Length);
        foreach (var member in stated)
        {
            var form = Symbols.SingleOrDefault(symbol
                => symbol.ParentUri == member.ParentUri && symbol.Name == member.GetProperty(SymbolProperties.StringForm));
            Assert.IsNotNull(form, member.Name);
            Assert.AreEqual("String", (form as VBReturningMemberSymbol)?.ResolvedType?.Name, member.Name);
        }
    }
}

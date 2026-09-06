using NSubstitute;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SyntaxTreeSymbolProviderTests
{
    private static readonly Uri WorkspaceRoot = TestUri.WorkspaceRoot();
    private static readonly Uri ModuleUri = TestUri.TestModuleUri();

    private static List<Symbol> Provide(string source, ISymbolResolver? resolver = null)
    {
        var parseResult = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, source);
        var provider = new SyntaxTreeSymbolProvider(
            WorkspaceRoot, ModuleUri, parseResult, resolver ?? Substitute.For<ISymbolResolver>());
        return [.. provider.ProvideSymbols()];
    }

    private static T Single<T>(IEnumerable<Symbol> symbols) where T : Symbol
        => symbols.OfType<T>().Single();

    [TestMethod]
    public void UnparsableModule_YieldsNothing()
        => Assert.IsEmpty(Provide("not a module"));

    [TestMethod]
    public void Sub_YieldsProcedureSymbol()
    {
        var symbol = Single<VBProcedureMemberSymbol>(Provide("Public Sub Foo()\r\nEnd Sub"));

        Assert.AreEqual("Foo", symbol.Name);
        Assert.AreEqual(SymbolKindExt.Procedure, symbol.Kind);
        Assert.AreEqual(AccessModifier.Public, symbol.AccessModifier);
        Assert.AreEqual(VBTypeNames.VBVoid, symbol.ResolvedType.Name);
        Assert.IsEmpty(symbol.Parameters);
    }

    [TestMethod]
    public void Function_UnresolvedReturnType_StaysUnknown()
    {
        var symbol = Single<VBFunctionMemberSymbol>(Provide("Private Function Bar() As Long\r\nEnd Function"));

        Assert.AreEqual("Bar", symbol.Name);
        Assert.AreEqual(AccessModifier.Private, symbol.AccessModifier);
        Assert.AreEqual(VBTypeNames.VBUnknown, symbol.ResolvedType.Name);
    }

    [TestMethod]
    public void Function_ReturnType_ResolvesThroughSymbolResolver()
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.Resolve("Long", ScopeKind.Global, Arg.Any<Uri>())
            .Returns(new UnboundVBModuleFieldVariableMemberSymbol(WorkspaceRoot, WorkspaceRoot, "Long", VBLongType.TypeInfo));

        var symbol = Single<VBFunctionMemberSymbol>(Provide("Function Bar() As Long\r\nEnd Function", resolver));

        Assert.IsInstanceOfType<VBLongType>(symbol.ResolvedType);
    }

    [TestMethod]
    public void IntrinsicReturnType_ResolvesThroughTheIntrinsicResolver()
    {
        var symbol = Single<VBFunctionMemberSymbol>(Provide(
            "Function Bar() As Long\r\nEnd Function", new IntrinsicSymbolResolver()));

        Assert.AreEqual(VBTypeNames.VBLong, symbol.ResolvedType.Name);
    }

    [TestMethod]
    public void Sub_Parameters_AreYieldedWithKindAndArity()
    {
        var symbol = Single<VBProcedureMemberSymbol>(Provide(
            "Public Sub Baz(ByVal First As Long, Optional Second As String, ParamArray Rest())\r\nEnd Sub"));

        Assert.HasCount(3, symbol.Parameters);

        Assert.AreEqual("First", symbol.Parameters[0].Name);
        Assert.AreEqual(ParameterKind.ExplicitByVal, symbol.Parameters[0].ParameterKind);
        Assert.IsFalse(symbol.Parameters[0].IsOptional);

        Assert.AreEqual("Second", symbol.Parameters[1].Name);
        Assert.IsTrue(symbol.Parameters[1].IsOptional);

        Assert.AreEqual("Rest", symbol.Parameters[2].Name);
        Assert.IsInstanceOfType<ParamArrayParameterSymbol>(symbol.Parameters[2]);

        // parameters parent to the member, not the module
        Assert.AreEqual(symbol.Uri, symbol.Parameters[0].ParentUri);
    }

    [TestMethod]
    public void ModuleField_YieldsFieldSymbol()
    {
        var symbol = Single<VBModuleFieldVariableMemberSymbol>(Provide("Private Amount As Currency"));

        Assert.AreEqual("Amount", symbol.Name);
        Assert.AreEqual(SymbolKindExt.Field, symbol.Kind);
        Assert.AreEqual(AccessModifier.Private, symbol.AccessModifier);
        Assert.AreEqual(ModuleUri, symbol.ParentUri);
    }

    [TestMethod]
    public void ConditionalCompilation_YieldsBothLiveAndDeadBranches()
    {
        const string source = """
            #If DEBUG Then
            Dim Foo As Long
            Dim Bar As Integer
            #Else
            Dim Foo As Double
            #End If
            """;

        var fields = Provide(source).OfType<VBModuleFieldVariableMemberSymbol>().ToArray();

        Assert.HasCount(3, fields);
        Assert.HasCount(2, fields.Where(f => f.Name == "Foo").ToArray());
    }

    [TestMethod]
    public void Enum_YieldsEnumSymbolAndItsMembers()
    {
        const string source = """
            Public Enum Color
                Red
                Green = 5
            End Enum
            """;

        var symbols = Provide(source);
        var enumSymbol = Single<VBEnumMemberSymbol>(symbols);
        var members = symbols.OfType<VBEnumConstMemberSymbol>().ToArray();

        Assert.AreEqual("Color", enumSymbol.Name);
        Assert.AreEqual(SymbolKindExt.Enum, enumSymbol.Kind);

        Assert.HasCount(2, members);
        CollectionAssert.AreEquivalent(new[] { "Red", "Green" }, members.Select(m => m.Name).ToArray());
        Assert.AreEqual(enumSymbol.Uri, members[0].ParentUri);
    }

    [TestMethod]
    public void DeclareFunction_YieldsExternalFunctionSymbol()
    {
        var symbol = Single<VBExternalFunctionMemberSymbol>(Provide(
            "Public Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long"));

        Assert.AreEqual("GetTickCount", symbol.Name);
        Assert.IsTrue(symbol.IsPtrSafe);
        Assert.IsTrue(symbol.Lib.Contains("kernel32"));
        Assert.AreEqual(ScopeKind.External, symbol.ScopeKind);
    }

    [TestMethod]
    public void Properties_YieldGetAndLetSymbols()
    {
        const string source = """
            Public Property Get Label() As String
            End Property
            Public Property Let Label(ByVal Value As String)
            End Property
            """;

        var symbols = Provide(source);

        var getter = Single<VBPropertyGetMemberSymbol>(symbols);
        var setter = Single<VBPropertyLetMemberSymbol>(symbols);
        Assert.AreEqual("Label", getter.Name);
        Assert.AreEqual(SymbolKindExt.Property, getter.Kind);
        Assert.AreEqual("Label", setter.Name);
        Assert.HasCount(1, setter.Parameters);
    }

    [TestMethod]
    public void Event_YieldsEventSymbol()
    {
        var symbol = Single<VBEventMemberSymbol>(Provide("Public Event Changed(ByVal NewValue As Long)"));

        Assert.AreEqual("Changed", symbol.Name);
        Assert.AreEqual(SymbolKindExt.Event, symbol.Kind);
    }

    [TestMethod]
    public void ModuleConst_YieldsConstantSymbol()
    {
        var symbol = Single<VBConstantMemberSymbol>(Provide("Public Const MaxItems As Long = 10"));

        Assert.AreEqual("MaxItems", symbol.Name);
        Assert.AreEqual(SymbolKindExt.Constant, symbol.Kind);
        Assert.AreEqual(AccessModifier.Public, symbol.AccessModifier);
    }

    [TestMethod]
    public void UserDefinedType_YieldsTypeSymbolAndItsFields()
    {
        const string source = """
            Public Type TPoint
                X As Long
                Y As Long
            End Type
            """;

        var symbols = Provide(source);
        var type = Single<VBUserDefinedTypeMemberSymbol>(symbols);
        var fields = symbols.OfType<VBModuleFieldVariableMemberSymbol>().ToArray();

        Assert.AreEqual("TPoint", type.Name);
        Assert.AreEqual(SymbolKindExt.UserDefinedType, type.Kind);

        Assert.HasCount(2, fields);
        CollectionAssert.AreEquivalent(new[] { "X", "Y" }, fields.Select(f => f.Name).ToArray());
        Assert.AreEqual(type.Uri, fields[0].ParentUri);
    }

    [TestMethod]
    public void Symbol_RangeComesFromTheDeclarationNode()
    {
        var symbol = Single<VBProcedureMemberSymbol>(Provide("Sub Foo()\r\nEnd Sub"));

        Assert.AreNotEqual(SourceRange.Empty, symbol.Range);
        Assert.AreEqual(symbol.Range, symbol.SelectionRange);
    }
}

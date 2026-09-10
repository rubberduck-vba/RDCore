using NSubstitute;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SyntaxTreeSymbolProviderTests
{
    private static readonly Uri WorkspaceRoot = TestUri.WorkspaceRoot();
    private static readonly Uri ModuleUri = TestUri.TestModuleUri();

    private static List<Symbol> Provide(string source, ISymbolResolver? resolver = null, ModuleType moduleType = ModuleType.StdModule)
    {
        var parseResult = new ModuleParser().Parse(TestUri.TestModuleUri(), moduleType, source);
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
            .Returns(SymbolResolutionResult.Resolved(
                new UnboundVBModuleFieldVariableMemberSymbol(WorkspaceRoot, WorkspaceRoot, "Long", ScopeKind.Global, VBLongType.TypeInfo)));

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
    public void ConditionalCompilation_MergesDuplicateNameIntoOneSymbolWithDefinitions()
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

        // Foo is declared in both branches -> one symbol, two definition sites; Bar stays single.
        Assert.HasCount(2, fields);

        var foo = fields.Single(f => f.Name == "Foo");
        Assert.HasCount(2, foo.Definitions);
        Assert.IsTrue(foo.Definitions.All(d => d.State == DefinitionState.Unknown));
        // sites are in source order: the #If branch is first, so it is the primary.
        Assert.IsTrue(foo.Definitions[0].Range.CompareTo(foo.Definitions[1].Range) < 0);
        Assert.AreEqual(foo.Range, foo.Definitions[0].Range);

        var bar = fields.Single(f => f.Name == "Bar");
        Assert.IsEmpty(bar.Definitions);
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
        // a Declare in a standard module is module-scoped (the "external" part is the Lib/Alias, not the symbol scope).
        Assert.AreEqual(ScopeKind.Module, symbol.ScopeKind);
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
    public void Event_YieldsEventSymbol_WithParameters()
    {
        var symbol = Single<VBEventMemberSymbol>(Provide("Public Event Changed(ByVal NewValue As Long)"));

        Assert.AreEqual("Changed", symbol.Name);
        Assert.AreEqual(SymbolKindExt.Event, symbol.Kind);
        Assert.HasCount(1, symbol.Parameters);
        Assert.AreEqual("NewValue", symbol.Parameters[0].Name);
        Assert.AreEqual(symbol.Uri, symbol.Parameters[0].ParentUri);
    }

    [TestMethod]
    public void PropertyGet_ReturnType_ResolvesThroughTheResolver()
    {
        var getter = Single<VBPropertyGetMemberSymbol>(Provide(
            "Public Property Get Label() As String\r\nEnd Property", new IntrinsicSymbolResolver()));

        Assert.AreEqual(VBTypeNames.VBString, getter.ResolvedType.Name);
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
    public void ModuleConst_TypeDeclarationCharacter_ResolvesTheType()
    {
        var symbol = Single<VBConstantMemberSymbol>(Provide(
            "Public Const Greeting$ = \"hi\"", new IntrinsicSymbolResolver()));

        Assert.AreEqual("Greeting", symbol.Name);
        Assert.AreEqual(VBTypeNames.VBString, symbol.ResolvedType.Name);
    }

    [TestMethod]
    public void MemberScope_FollowsTheModuleKind()
    {
        var inStdModule = Single<VBProcedureMemberSymbol>(Provide("Public Sub Foo()\r\nEnd Sub", moduleType: ModuleType.StdModule));
        Assert.AreEqual(ScopeKind.Module, inStdModule.ScopeKind);

        var inClassModule = Single<VBProcedureMemberSymbol>(Provide("Public Sub Foo()\r\nEnd Sub", moduleType: ModuleType.ClassModule));
        Assert.AreEqual(ScopeKind.Instance, inClassModule.ScopeKind);
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
        var fields = symbols.OfType<VBUserDefinedTypeFieldSymbol>().ToArray();

        Assert.AreEqual("TPoint", type.Name);
        Assert.AreEqual(SymbolKindExt.UserDefinedType, type.Kind);

        Assert.HasCount(2, fields);
        CollectionAssert.AreEquivalent(new[] { "X", "Y" }, fields.Select(f => f.Name).ToArray());
        Assert.AreEqual(type.Uri, fields[0].ParentUri);
        // the fields also ride on the type symbol, so a resolver can return a whole VBUserDefinedType
        CollectionAssert.AreEquivalent(fields, type.Members.ToArray());
    }

    [TestMethod]
    public void Symbol_RangeComesFromTheDeclarationNode()
    {
        var symbol = Single<VBProcedureMemberSymbol>(Provide("Sub Foo()\r\nEnd Sub"));

        Assert.AreNotEqual(SourceRange.Empty, symbol.Range);
        Assert.AreEqual(symbol.Range, symbol.SelectionRange);
    }

    // --- procedure-local declarations (MS-VBAL 5.4.3.1-2) ---

    [TestMethod]
    public void LocalVariable_YieldsLocalSymbol_ParentedToProcedure()
    {
        var symbols = Provide("""
            Public Sub Foo()
                Dim Total As Long
            End Sub
            """, new IntrinsicSymbolResolver());

        var procedure = Single<VBProcedureMemberSymbol>(symbols);
        var local = Single<VBLocalVariableSymbol>(symbols);

        Assert.AreEqual("Total", local.Name);
        Assert.AreEqual(ScopeKind.Local, local.ScopeKind);
        Assert.AreEqual(procedure.Uri, local.ParentUri);
        Assert.IsFalse(local.IsStatic);
        Assert.AreEqual(VBTypeNames.VBLong, local.ResolvedType.Name);
    }

    [TestMethod]
    public void LocalStatic_SetsIsStatic()
    {
        var local = Single<VBLocalVariableSymbol>(Provide("""
            Public Sub Foo()
                Static Counter As Long
            End Sub
            """, new IntrinsicSymbolResolver()));

        Assert.IsTrue(local.IsStatic);
    }

    [TestMethod]
    // MS-VBAL 5.2.3.1: a bounds list declares a fixed-size array; element type resolves, bounds don't.
    public void LocalFixedSizeArray_YieldsFixedSizeArrayType()
    {
        var local = Single<VBLocalVariableSymbol>(Provide("""
            Public Sub Foo()
                Dim Grid(1 To 3, 0 To 4) As Long
            End Sub
            """, new IntrinsicSymbolResolver()));

        var array = Assert.IsInstanceOfType<VBFixedSizeArrayType>(local.ResolvedType);
        Assert.AreEqual(VBTypeNames.VBLong, array.ItemType.Name);
    }

    [TestMethod]
    // MS-VBAL 5.2.3.1: an empty `()` clause, or a trailing `()` on the As-clause, declares a dynamic array.
    [DataRow("Dim Buffer() As Long")]
    [DataRow("Dim Buffer As Long()")]
    public void LocalDynamicArray_YieldsResizableArrayType(string declaration)
    {
        var local = Single<VBLocalVariableSymbol>(Provide($"""
            Public Sub Foo()
                {declaration}
            End Sub
            """, new IntrinsicSymbolResolver()));

        var array = Assert.IsInstanceOfType<VBResizableArrayType>(local.ResolvedType);
        Assert.AreEqual(VBTypeNames.VBLong, array.ItemType.Name);
    }

    [TestMethod]
    // RD-VBAL 2.4.1.3: a resizable Byte() array binds the specialized VBResizableByteArrayType.
    public void LocalDynamicByteArray_YieldsResizableByteArrayType()
    {
        var local = Single<VBLocalVariableSymbol>(Provide("""
            Public Sub Foo()
                Dim Buffer() As Byte
            End Sub
            """, new IntrinsicSymbolResolver()));

        Assert.IsInstanceOfType<VBResizableByteArrayType>(local.ResolvedType);
    }

    [TestMethod]
    public void LocalConstant_YieldsLocalConstantSymbol_ParentedToProcedure()
    {
        var symbols = Provide("""
            Public Sub Foo()
                Const Factor As Long = 2
            End Sub
            """, new IntrinsicSymbolResolver());

        var procedure = Single<VBProcedureMemberSymbol>(symbols);
        var constant = Single<VBLocalConstantSymbol>(symbols);

        Assert.AreEqual("Factor", constant.Name);
        Assert.AreEqual(ScopeKind.Local, constant.ScopeKind);
        Assert.AreEqual(procedure.Uri, constant.ParentUri);
        Assert.AreEqual(VBTypeNames.VBLong, constant.ResolvedType.Name);
    }

    [TestMethod]
    public void LocalDeclaration_InConditionalBranches_MergesIntoOneSymbolWithDefinitions()
    {
        const string source = """
            Public Sub Foo()
            #If DEBUG Then
                Dim Temp As Long
            #Else
                Dim Temp As Double
            #End If
            End Sub
            """;

        var locals = Provide(source).OfType<VBLocalVariableSymbol>().ToArray();

        var temp = Assert.ContainsSingle(locals);
        Assert.AreEqual("Temp", temp.Name);
        Assert.HasCount(2, temp.Definitions);
    }

    [TestMethod]
    public void AssignmentStatement_CreatesNoSymbol()
    {
        var symbols = Provide("""
            Public Sub Foo()
                Dim Total As Long
                Total = 42
                Undeclared = 1
            End Sub
            """, new IntrinsicSymbolResolver());

        Assert.AreEqual("Total", Single<VBLocalVariableSymbol>(symbols).Name);
    }

    // --- ReDim symbol discovery (MS-VBAL 5.4.3.3) ---

    [TestMethod]
    public void Redim_UndeclaredUnqualifiedName_IntroducesResizableArrayLocal()
    {
        var symbols = Provide("""
            Public Sub Foo()
                ReDim Buffer(1 To 10) As Long
            End Sub
            """, new IntrinsicSymbolResolver());

        var procedure = Single<VBProcedureMemberSymbol>(symbols);
        var local = Single<VBLocalVariableSymbol>(symbols);

        Assert.AreEqual("Buffer", local.Name);
        Assert.AreEqual(ScopeKind.Local, local.ScopeKind);
        Assert.AreEqual(procedure.Uri, local.ParentUri);
        Assert.AreEqual(LocalDeclarationKind.ReDim, local.DeclaredBy);

        var array = Assert.IsInstanceOfType<VBResizableArrayType>(local.ResolvedType);
        Assert.AreEqual(VBTypeNames.VBLong, array.ItemType.Name);
    }

    [TestMethod]
    public void Redim_ByteElement_IntroducesResizableByteArrayLocal()
    {
        var local = Single<VBLocalVariableSymbol>(Provide("""
            Public Sub Foo()
                ReDim Buffer(4) As Byte
            End Sub
            """, new IntrinsicSymbolResolver()));

        Assert.IsInstanceOfType<VBResizableByteArrayType>(local.ResolvedType);
        Assert.AreEqual(LocalDeclarationKind.ReDim, local.DeclaredBy);
    }

    [TestMethod]
    public void Redim_ExistingLocal_IsNotReintroduced()
    {
        var locals = Provide("""
            Public Sub Foo()
                Dim Buffer() As Long
                ReDim Buffer(10)
            End Sub
            """, new IntrinsicSymbolResolver()).OfType<VBLocalVariableSymbol>().ToArray();

        var buffer = Assert.ContainsSingle(locals);
        Assert.AreEqual("Buffer", buffer.Name);
        Assert.AreEqual(LocalDeclarationKind.Dim, buffer.DeclaredBy);
    }

    [TestMethod]
    // VBA hoists declarations, so a later Dim still owns a name a ReDim mentions earlier.
    public void Redim_BeforeDim_IsNotReintroduced()
    {
        var locals = Provide("""
            Public Sub Foo()
                ReDim Buffer(10)
                Dim Buffer() As Long
            End Sub
            """, new IntrinsicSymbolResolver()).OfType<VBLocalVariableSymbol>().ToArray();

        var buffer = Assert.ContainsSingle(locals);
        Assert.AreEqual(LocalDeclarationKind.Dim, buffer.DeclaredBy);
    }

    [TestMethod]
    public void Redim_ModuleField_IntroducesNoLocal()
    {
        var symbols = Provide("""
            Private SharedBuffer() As Long

            Public Sub Foo()
                ReDim SharedBuffer(10)
            End Sub
            """, new IntrinsicSymbolResolver());

        Assert.IsEmpty(symbols.OfType<VBLocalVariableSymbol>());
        Assert.ContainsSingle(symbols.OfType<VBModuleFieldVariableMemberSymbol>());
    }

    [TestMethod]
    public void Redim_Parameter_IntroducesNoLocal()
    {
        var symbols = Provide("""
            Public Sub Foo(Buffer() As Long)
                ReDim Buffer(10)
            End Sub
            """, new IntrinsicSymbolResolver());

        Assert.IsEmpty(symbols.OfType<VBLocalVariableSymbol>());
    }

    [TestMethod]
    public void Redim_QualifiedTarget_IntroducesNoLocal()
    {
        var symbols = Provide("""
            Public Sub Foo()
                ReDim Me.Buffer(10)
            End Sub
            """, new IntrinsicSymbolResolver(), ModuleType.ClassModule);

        Assert.IsEmpty(symbols.OfType<VBLocalVariableSymbol>());
    }

    [TestMethod]
    public void Redim_MultipleUndeclaredTargets_IntroduceOneLocalEach()
    {
        var locals = Provide("""
            Public Sub Foo()
                ReDim a(1), b(2 To 4)
            End Sub
            """, new IntrinsicSymbolResolver()).OfType<VBLocalVariableSymbol>().ToArray();

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, locals.Select(l => l.Name).ToArray());
        Assert.IsTrue(locals.All(l => l.DeclaredBy == LocalDeclarationKind.ReDim));
    }

    [TestMethod]
    public void Redim_NestedInABlock_IntroducesLocal()
    {
        var local = Single<VBLocalVariableSymbol>(Provide("""
            Public Sub Foo(ByVal Flag As Boolean)
                If Flag Then
                    ReDim Buffer(5)
                End If
            End Sub
            """, new IntrinsicSymbolResolver()));

        Assert.AreEqual("Buffer", local.Name);
        Assert.AreEqual(LocalDeclarationKind.ReDim, local.DeclaredBy);
    }
}

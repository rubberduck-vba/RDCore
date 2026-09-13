using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SymbolDescriptorProjectorTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Uri ModuleUri = new("file:///c:/ws/#Mod1");

    private static SymbolDescriptor[] Project(string source)
    {
        var parseResult = new ModuleParser().Parse(new Uri("file:///c:/ws/src/Mod1.bas"), source);
        var symbols = new SyntaxTreeSymbolProvider(WorkspaceRoot, ModuleUri, ModuleType.StdModule, parseResult, new IntrinsicSymbolResolver()).ProvideSymbols();
        return [.. SymbolDescriptorProjector.Project(symbols, ModuleUri)];
    }

    [TestMethod]
    public void Function_ProjectsWithIntrinsicReturnTypeNameAndParameters()
    {
        var descriptor = Project("Public Function Add(ByVal a As Long, ByVal b As Long) As Long\r\nEnd Function").Single();

        Assert.AreEqual(SymbolDescriptorKind.Function, descriptor.Kind);
        Assert.AreEqual("Add", descriptor.Name);
        Assert.AreEqual(AccessModifier.Public, descriptor.AccessModifier);
        Assert.AreEqual("Long", descriptor.DeclaredTypeName);
        Assert.AreEqual(2, descriptor.Parameters.Length);
        Assert.AreEqual("Long", descriptor.Parameters[0].DeclaredTypeName);
        Assert.AreEqual(ParameterKind.ExplicitByVal, descriptor.Parameters[0].ParameterKind);
    }

    [TestMethod]
    public void UnresolvedType_ProjectsWithNullDeclaredTypeName()
    {
        var descriptor = Project("Private Widget As CWidget").Single();

        Assert.AreEqual(SymbolDescriptorKind.ModuleField, descriptor.Kind);
        Assert.IsNull(descriptor.DeclaredTypeName);
    }

    [TestMethod]
    public void Sub_ProjectsWithNoDeclaredTypeName()
    {
        var descriptor = Project("Public Sub DoWork()\r\nEnd Sub").Single();

        Assert.AreEqual(SymbolDescriptorKind.Procedure, descriptor.Kind);
        Assert.IsNull(descriptor.DeclaredTypeName);
    }

    [TestMethod]
    public void Enum_NestsItsMembersUnderTheEnumDescriptor()
    {
        var descriptors = Project("""
            Public Enum Direction
                North
                South
            End Enum
            """);

        var direction = descriptors.Single();
        Assert.AreEqual(SymbolDescriptorKind.Enum, direction.Kind);
        Assert.AreEqual(2, direction.Members.Length);
        Assert.IsTrue(direction.Members.All(m => m.Kind == SymbolDescriptorKind.EnumMember));
        CollectionAssert.AreEquivalent(new[] { "North", "South" }, direction.Members.Select(m => m.Name).ToArray());
    }

    [TestMethod]
    public void UserDefinedType_NestsItsFieldsAsUdtFieldDescriptors()
    {
        var descriptors = Project("""
            Public Type TPoint
                X As Long
                Y As Long
            End Type
            """);

        var point = descriptors.Single();
        Assert.AreEqual(SymbolDescriptorKind.UserDefinedType, point.Kind);
        Assert.AreEqual(2, point.Members.Length);
        Assert.IsTrue(point.Members.All(m => m.Kind == SymbolDescriptorKind.UserDefinedTypeField));
        Assert.IsTrue(point.Members.All(m => m.DeclaredTypeName == "Long"));
    }

    [TestMethod]
    public void DeclareFunction_ProjectsAsExternalWithMetadata()
    {
        var descriptor = Project("Public Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long").Single();

        Assert.AreEqual(SymbolDescriptorKind.ExternalFunction, descriptor.Kind);
        Assert.IsNotNull(descriptor.External);
        Assert.IsTrue(descriptor.External!.IsPtrSafe);
        Assert.IsTrue(descriptor.External.Library.Contains("kernel32"));
    }

    [TestMethod]
    public void PropertyGet_CarriesIntrinsicReturnTypeName()
    {
        var descriptor = Project("Public Property Get Label() As String\r\nEnd Property").Single();

        Assert.AreEqual(SymbolDescriptorKind.PropertyGet, descriptor.Kind);
        Assert.AreEqual("String", descriptor.DeclaredTypeName);
    }

    [TestMethod]
    public void Event_CarriesItsParameters()
    {
        var descriptor = Project("Public Event Changed(ByVal NewValue As Long)").Single();

        Assert.AreEqual(SymbolDescriptorKind.Event, descriptor.Kind);
        Assert.AreEqual(1, descriptor.Parameters.Length);
        Assert.AreEqual("NewValue", descriptor.Parameters[0].Name);
        Assert.AreEqual("Long", descriptor.Parameters[0].DeclaredTypeName);
    }

    [TestMethod]
    public void PropertyGetAndLet_ProjectAsDistinctKinds()
    {
        var descriptors = Project("""
            Public Property Get Label() As String
            End Property
            Public Property Let Label(ByVal value As String)
            End Property
            """);

        Assert.ContainsSingle(descriptors.Where(d => d.Kind == SymbolDescriptorKind.PropertyGet));
        var letter = descriptors.Single(d => d.Kind == SymbolDescriptorKind.PropertyLet);
        Assert.AreEqual(1, letter.Parameters.Length);
    }

    [TestMethod]
    public void UnmappedSymbolKind_IsSkippedRatherThanThrowingOrDefaultingToModuleField()
        // regression, escalated: KindOf's default arm first silently mislabeled any unrecognized
        // symbol type as ModuleField, then (post-#204) was made to throw instead. Adversarial review
        // PRs #208-224 (item 7) found the throw crashes projection for the WHOLE module over one
        // stray top-level symbol - e.g. a local wrongly parented to the module by a Uri collision.
        // Neither extreme is right: skip just that one symbol, project everything else normally. A
        // hand-built symbol of a kind this projector was never taught about is the only way to
        // exercise it -- a class-module symbol standing in for "a module", not "a module member".
        => Assert.IsEmpty(SymbolDescriptorProjector.Project(
            [new VBClassModuleSymbol(WorkspaceRoot, ModuleUri, "Whatever")], ModuleUri));

    [TestMethod]
    public void UnmappedSymbolKind_DoesNotPreventSiblingSymbolsFromProjecting()
    {
        var parseResult = new ModuleParser().Parse(new Uri("file:///c:/ws/src/Mod1.bas"), "Public Sub DoWork()\r\nEnd Sub");
        var real = new SyntaxTreeSymbolProvider(WorkspaceRoot, ModuleUri, ModuleType.StdModule, parseResult, new IntrinsicSymbolResolver()).ProvideSymbols();
        var stray = new VBClassModuleSymbol(WorkspaceRoot, ModuleUri, "Whatever");

        var descriptor = SymbolDescriptorProjector.Project([.. real, stray], ModuleUri).Single();

        Assert.AreEqual("DoWork", descriptor.Name);
        Assert.AreEqual(SymbolDescriptorKind.Procedure, descriptor.Kind);
    }

    [TestMethod]
    public void ProcedureWithALocalVariable_DoesNotThrow_AndTheLocalIsNotProjectedAsAMember()
        // regression: KindOf's throwing default (post-#204) had no arm for VBLocalVariableSymbol, so
        // any procedure declaring a local Dim aborted projection for the whole module.
    {
        var descriptor = Project("""
            Public Sub DoWork()
                Dim total As Long
            End Sub
            """).Single();

        Assert.AreEqual(SymbolDescriptorKind.Procedure, descriptor.Kind);
        Assert.AreEqual(0, descriptor.Members.Length);
    }

    [TestMethod]
    public void ProcedureWithALocalConstant_DoesNotThrow_AndTheLocalIsNotProjectedAsAMember()
        // same regression, VBLocalConstantSymbol has no KindOf arm either.
    {
        var descriptor = Project("""
            Public Sub DoWork()
                Const Max As Long = 10
            End Sub
            """).Single();

        Assert.AreEqual(SymbolDescriptorKind.Procedure, descriptor.Kind);
        Assert.AreEqual(0, descriptor.Members.Length);
    }

    [TestMethod]
    public void ConditionalCompilation_ProjectsOneDescriptorCarryingEveryBranch()
    {
        var descriptors = Project("""
            #If DEBUG Then
            Dim Foo As Long
            #Else
            Dim Foo As Double
            #End If
            """);

        var foo = descriptors.Single(d => d.Name == "Foo");
        Assert.AreEqual(2, foo.Definitions.Length);
        Assert.IsTrue(foo.Definitions.All(d => d.State == DefinitionState.Unknown));
        Assert.AreEqual(foo.Range, foo.Definitions[0].Range);
    }
}

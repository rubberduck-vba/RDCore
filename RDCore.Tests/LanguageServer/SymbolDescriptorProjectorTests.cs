using System.Linq;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SymbolDescriptorProjectorTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Uri ModuleUri = new("file:///c:/ws/#Mod1");

    private static SymbolDescriptor[] Project(string source)
    {
        var parseResult = new ModuleParser().Parse(new Uri("file:///c:/ws/src/Mod1.bas"), ModuleType.StdModule, source);
        var symbols = new SyntaxTreeSymbolProvider(WorkspaceRoot, ModuleUri, parseResult, new IntrinsicSymbolResolver()).ProvideSymbols();
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
    public void ConditionalCompilation_ProjectsBothBranches()
    {
        var descriptors = Project("""
            #If DEBUG Then
            Dim Foo As Long
            #Else
            Dim Foo As Double
            #End If
            """);

        Assert.AreEqual(2, descriptors.Count(d => d.Name == "Foo"));
    }
}

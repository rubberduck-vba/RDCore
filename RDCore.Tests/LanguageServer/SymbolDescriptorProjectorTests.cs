using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

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

    // Mod1, projected beside the other modules of its workspace, every one of them bound against all of them as the language server binds them.
    private static SymbolDescriptor[] ProjectBeside(string source, params (string Name, ModuleType Type, string Source)[] others)
    {
        var modules = others
            .Select(other => (
                Uri: new UriBuilder(WorkspaceRoot) { Fragment = other.Name }.Uri,
                other.Type,
                Parse: new ModuleParser().Parse(new Uri($"file:///c:/ws/src/{other.Name}.{(other.Type == ModuleType.ClassModule ? "cls" : "bas")}"), other.Source)))
            .Append((Uri: ModuleUri, Type: ModuleType.StdModule, Parse: new ModuleParser().Parse(new Uri("file:///c:/ws/src/Mod1.bas"), "Attribute VB_Name = \"Mod1\"\r\n" + source)))
            .ToArray();
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, modules.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(WorkspaceRoot, ModuleUri, ModuleType.StdModule, modules[^1].Parse, resolver).ProvideSymbols();
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
    public void UnresolvedType_ProjectsWithTheNameItWasWrittenWith()
    {
        // the host resolves it again once it has the whole workspace, and says so if it still does not.
        var descriptor = Project("Private Widget As CWidget").Single();

        Assert.AreEqual(SymbolDescriptorKind.ModuleField, descriptor.Kind);
        Assert.AreEqual("CWidget", descriptor.DeclaredTypeName);
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
    public void AnotherModulesTypeAndEnum_ProjectWithThatModulesName()
        // the host resolves the name again, from this module: by its bare name it binds whatever ranks first there, which can be a procedural module, a
        // library's class or another module's type of that name, and not the type the language server bound.
    {
        var descriptors = ProjectBeside("""
            Private Rec As TRec
            Private Items(1 To 2) As Mod2.TRec
            Private Const Initial As Colours = 1
            Public Function Paint(ByVal c As Mod2.Colours) As Colours
                Dim r As TRec
                Const k As Mod2.Colours = 1
            End Function
            """, ("Mod2", ModuleType.StdModule, "Attribute VB_Name = \"Mod2\"\r\nPublic Type TRec\r\nX As Long\r\nEnd Type\r\nPublic Enum Colours\r\ncRed = 1\r\nEnd Enum\r\n"));

        Assert.AreEqual("Mod2.TRec", descriptors.Single(descriptor => descriptor.Name == "Rec").DeclaredTypeName);
        var items = descriptors.Single(descriptor => descriptor.Name == "Items");
        Assert.AreEqual("Mod2.TRec", items.DeclaredTypeName);
        Assert.IsNotNull(items.Array);
        var initial = descriptors.Single(descriptor => descriptor.Name == "Initial");
        Assert.AreEqual("Mod2.Colours", initial.DeclaredTypeName);
        Assert.AreEqual("Mod2.Colours", initial.Constants.Single().DeclaredTypeName);
        var paint = descriptors.Single(descriptor => descriptor.Name == "Paint");
        Assert.AreEqual("Mod2.Colours", paint.DeclaredTypeName);
        Assert.AreEqual("Mod2.Colours", paint.Parameters.Single().DeclaredTypeName);
        Assert.AreEqual("Mod2.TRec", paint.Locals.Single().DeclaredTypeName);
        Assert.AreEqual("Mod2.Colours", paint.Constants.Single().DeclaredTypeName);
    }

    [TestMethod]
    public void TheModulesOwnTypes_AndAClassOfTheWorkspace_ProjectWithTheirOwnName()
        // the host binds the module's own types first, a Private one included, so their bare name is the type; a class travels by its own name.
    {
        var descriptors = ProjectBeside("""
            Private Type TOwn
                Inner As Mod2.TRec
                Shade As EOwn
            End Type
            Private Enum EOwn
                eA = 1
            End Enum
            Private Mine As TOwn
            Private Same As Mod1.TOwn
            Private Widget As CWidget
            """,
            ("Mod2", ModuleType.StdModule, "Attribute VB_Name = \"Mod2\"\r\nPublic Type TRec\r\nX As Long\r\nEnd Type\r\n"),
            ("CWidget", ModuleType.ClassModule, "Attribute VB_Name = \"CWidget\"\r\nPublic Total As Long\r\n"));

        Assert.AreEqual("TOwn", descriptors.Single(descriptor => descriptor.Name == "Mine").DeclaredTypeName);
        Assert.AreEqual("TOwn", descriptors.Single(descriptor => descriptor.Name == "Same").DeclaredTypeName);
        Assert.AreEqual("CWidget", descriptors.Single(descriptor => descriptor.Name == "Widget").DeclaredTypeName);
        var own = descriptors.Single(descriptor => descriptor.Name == "TOwn");
        Assert.AreEqual("Mod2.TRec", own.Members.Single(field => field.Name == "Inner").DeclaredTypeName);
        Assert.AreEqual("EOwn", own.Members.Single(field => field.Name == "Shade").DeclaredTypeName);
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
    public void ALocalConstantOfAnUnresolvedType_ProjectsWithTheNameItWasWrittenWith()
        // as a variable's does: the host resolves it again, and says so if it does not resolve to a type. It travelled as no type at all.
    {
        var descriptor = Project("""
            Public Sub DoWork()
                Const Max As CWidget = 10
                Const Min As Long = 1
            End Sub
            """).Single();

        Assert.AreEqual("CWidget", descriptor.Constants.Single(constant => constant.Name == "Max").DeclaredTypeName);
        Assert.AreEqual("Long", descriptor.Constants.Single(constant => constant.Name == "Min").DeclaredTypeName);
    }

    [TestMethod]
    public void ALocalConstantOfALibrarysEnum_ProjectsWithTheLibrarysName_AsAVariableDoes()
        // the host resolves the name again: bare, it binds whatever ranks first there, which can be a procedural module of that name and not the library's type.
    {
        var parseResult = new ModuleParser().Parse(new Uri("file:///c:/ws/src/Mod1.bas"), """
            Public Sub DoWork()
                Const Initial As Widgets.WidgetMode = 1
                Dim Current As Widgets.WidgetMode
            End Sub
            """);
        var resolver = WorkspaceSymbolResolver.Compose(
            WorkspaceRoot, [(ModuleUri, ModuleType.StdModule, parseResult)], new IntrinsicSymbolResolver(),
            libraries: new LibrarySymbolReader(WorkspaceRoot).Read([LibraryFixtures.Widgets]));
        var symbols = new SyntaxTreeSymbolProvider(WorkspaceRoot, ModuleUri, ModuleType.StdModule, parseResult, resolver).ProvideSymbols();

        var descriptor = SymbolDescriptorProjector.Project(symbols, ModuleUri).Single();

        Assert.AreEqual("Widgets.WidgetMode", descriptor.Locals.Single().DeclaredTypeName);
        Assert.AreEqual("Widgets.WidgetMode", descriptor.Constants.Single().DeclaredTypeName);
    }

    [TestMethod]
    public void ConditionalCompilation_ProjectsTheFirstBranchCarryingEveryBranch_AndWhatEachOtherDeclaresBesideIt()
    {
        var descriptors = Project("""
            #If DEBUG Then
            Dim Foo As Long
            #Else
            Dim Foo As Double
            #End If
            """);

        // the host defines the one of the live branch (DefineSymbolsParams.PrecompilerTriviaJson).
        var foos = descriptors.Where(d => d.Name == "Foo").ToList();
        Assert.HasCount(2, foos);
        var foo = foos[0];
        Assert.AreEqual("Long", foo.DeclaredTypeName);
        Assert.AreEqual(2, foo.Definitions.Length);
        Assert.IsTrue(foo.Definitions.All(d => d.State == DefinitionState.Unknown));
        Assert.AreEqual(foo.Range, foo.Definitions[0].Range);
        Assert.AreEqual("Double", foos[1].DeclaredTypeName);
        Assert.AreEqual(foo.Definitions[1].Range, foos[1].Range);
    }
}

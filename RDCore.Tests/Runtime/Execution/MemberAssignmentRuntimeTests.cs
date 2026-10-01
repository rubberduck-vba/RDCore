using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Assignment to a member of an object (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>): a public variable of its
/// class, which is its storage, and a property, which the assignment invokes - its <c>Property Let</c>, or its
/// <c>Property Set</c> in a <c>Set</c> assignment, with the index arguments written after its name.
/// </summary>
/// <remarks>
/// <c>Box</c> has a public <c>Size</c>, a public <c>Link</c> (a <c>Box</c>), <c>Level</c> (a property whose <c>Let</c> sets <c>Size</c>
/// to ten times the value), <c>Item(i)</c> (an indexed one), <c>Partner</c> (a <c>Property Set</c> that sets <c>Link</c>), and a
/// <c>Class_Terminate</c> that prints. <c>IBox</c> is an interface with a public <c>Size</c>, and <c>Impl</c> implements it with
/// properties over a private variable. A module's <c>Main</c> has <c>b</c> and <c>c</c> (<c>Box</c>), and <c>i</c> (<c>IBox</c>).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class MemberAssignmentRuntimeTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(IEnumerable<Symbol> symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static InstructionList Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var result = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Verbose)));
        return result.InstructionList;
    }

    private sealed class World
    {
        public Dictionary<SemanticId, InstructionList> Bodies { get; } = [];
        public List<Symbol> Symbols { get; } = [];

        public VBParameterSymbol Me(Symbol owner) => new(Root, owner.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);

        public VBParameterSymbol Parameter(Symbol owner, string name, VBType type)
            => new(Root, owner.Uri, name, R, R, ParameterKind.ExplicitByVal, type);

        public VBInstanceFieldVariableMemberSymbol Field(VBClassModuleSymbol owner, string name, VBType type, AccessModifier access)
            => new(Root, owner.Uri, name, R, R, type, access);

        public VBProcedureMemberSymbol Sub(VBClassModuleSymbol owner, string name, AccessModifier access, string[] body)
        {
            var procedure = new VBProcedureMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);
            procedure = procedure with { Parameters = [Me(procedure)] };
            Bodies[procedure.SemanticId] = Lower(body);
            return procedure;
        }

        public VBPropertyGetMemberSymbol Get(VBClassModuleSymbol owner, string name, VBType type, AccessModifier access, string[] body, params (string, VBType)[] indexes)
        {
            var property = new VBPropertyGetMemberSymbol(Root, owner.Uri, ScopeKind.Instance, name, R, R, access);
            property = property with
            {
                ResolvedType = type,
                Parameters = [Me(property), .. indexes.Select(index => Parameter(property, index.Item1, index.Item2))],
            };
            Bodies[property.SemanticId] = Lower(body);
            return property;
        }

        public VBPropertyLetMemberSymbol Let(VBClassModuleSymbol owner, string name, AccessModifier access, string[] body, params (string, VBType)[] parameters)
        {
            var property = new VBPropertyLetMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, access);
            property = property with { Parameters = [Me(property), .. parameters.Select(p => Parameter(property, p.Item1, p.Item2))] };
            Bodies[property.SemanticId] = Lower(body);
            return property;
        }

        public VBPropertySetMemberSymbol Set(VBClassModuleSymbol owner, string name, AccessModifier access, string[] body, params (string, VBType)[] parameters)
        {
            var property = new VBPropertySetMemberSymbol(Root, owner.Uri, name, ScopeKind.Instance, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, access);
            property = property with { Parameters = [Me(property), .. parameters.Select(p => Parameter(property, p.Item1, p.Item2))] };
            Bodies[property.SemanticId] = Lower(body);
            return property;
        }
    }

    private static string[] Run(params string[] body) => Run(out _, body);

    private static string[] Run(out RuntimeSemanticsEvaluationResult outcome, params string[] body)
    {
        var world = new World();

        // Box
        var box = new VBClassModuleSymbol(Root, Root, "Box");
        var size = world.Field(box, "Size", VBLongType.TypeInfo, AccessModifier.Public);
        var boxLink = world.Field(box, "Link", VBObjectType.TypeInfo, AccessModifier.Public);
        var show = world.Sub(box, "Show", AccessModifier.Public, ["Debug.Print Size"]);
        // every box terminates when Main ends; only one that was told to say so does.
        var terminate = world.Sub(box, "Class_Terminate", AccessModifier.Private, ["If Size = -1 Then Debug.Print \"term\""]);
        var showLink = world.Sub(box, "ShowLink", AccessModifier.Public, ["Debug.Print Link.Size"]);
        var levelLet = world.Let(box, "Level", AccessModifier.Public, ["Size = v * 10"], ("v", VBLongType.TypeInfo));
        var levelGet = world.Get(box, "Level", VBLongType.TypeInfo, AccessModifier.Public, ["Level = Size"]);
        var itemLet = world.Let(box, "Item", AccessModifier.Public, ["Size = i * 100 + v"], ("i", VBLongType.TypeInfo), ("v", VBLongType.TypeInfo));
        var partnerSet = world.Set(box, "Partner", AccessModifier.Public, ["Set Link = b"], ("b", VBObjectType.TypeInfo));
        // a public array a box holds, which is sized by the box itself.
        var cells = world.Field(box, "Cells", new VBResizableArrayType(VBLongType.TypeInfo), AccessModifier.Public);
        var links = world.Field(box, "Links", new VBResizableArrayType(VBObjectType.TypeInfo), AccessModifier.Public);
        var allocate = world.Sub(box, "Allocate", AccessModifier.Public, ["ReDim Cells(1 To 3)", "ReDim Links(1 To 2)"]);
        box = box with
        {
            Members = [size, boxLink, show, terminate, showLink, levelLet, levelGet, itemLet, partnerSet, cells, links, allocate],
            DefaultInterfaceMembers = [size, boxLink, show, showLink, levelLet, levelGet, itemLet, partnerSet, cells, links, allocate],
        };
        var boxType = VBClassType.FromClassModule(box);

        // IBox, and Impl implementing it with properties over a private variable.
        var iBox = new VBClassModuleSymbol(Root, Root, "IBox");
        var iSize = world.Field(iBox, "Size", VBLongType.TypeInfo, AccessModifier.Public);
        iBox = iBox with { Members = [iSize], DefaultInterfaceMembers = [iSize] };
        var iBoxType = VBClassType.FromClassModule(iBox);

        var impl = new VBClassModuleSymbol(Root, Root, "Impl") { ImplementedInterfaces = [iBox] };
        var stored = world.Field(impl, "mSize", VBLongType.TypeInfo, AccessModifier.Private);
        var implLet = world.Let(impl, "IBox_Size", AccessModifier.Private, ["mSize = v + 1000"], ("v", VBLongType.TypeInfo));
        var implGet = world.Get(impl, "IBox_Size", VBLongType.TypeInfo, AccessModifier.Private, ["IBox_Size = mSize"]);
        var implShow = world.Sub(impl, "Show", AccessModifier.Public, ["Debug.Print mSize"]);
        impl = impl with { Members = [stored, implLet, implGet, implShow], DefaultInterfaceMembers = [implShow] };
        var implType = VBClassType.FromClassModule(impl);

        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var main = new VBProcedureMemberSymbol(Root, module.Uri, "Main", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        BoundTypedSymbol Local(string name, VBType type) => new VBLocalVariableSymbol(main.Uri, main.Uri, name, ScopeKind.Local, R, R, ResolvedType: type);
        main = main with { Locals = [Local("b", boxType), Local("c", boxType), Local("i", iBoxType), Local("m", implType)] };
        world.Bodies[main.SemanticId] = Lower(body);

        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [],
            [new StdLibSymbolProvider(Root), new Provider([module, box, iBox, impl, size, boxLink, show, terminate, showLink, levelLet, levelGet, itemLet, partnerSet, cells, links, allocate,
                iSize, stored, implLet, implGet, implShow, main])],
            output: output);
        var pipeline = RuntimeExecutionPipeline.Create(session, world.Bodies, Substitute.For<IVerboseMessageBuilder>());

        outcome = pipeline.Invoker.Invoke(main, session.Symbols.Resolver, []);
        return [.. output.Lines.Select(line => line.Trim())];
    }

    private static string[] RunOk(params string[] body)
    {
        var printed = Run(out var outcome, body);
        Assert.IsTrue(outcome.IsSuccess, $"{outcome.ErrorInfo?.Description} (internal error: {outcome.IsInternalError})");
        return printed;
    }

    #region A public variable

    [TestMethod]
    public void APublicVariable_IsAssignedThroughAMemberAccess()
        => CollectionAssert.AreEqual(new[] { "5" }, RunOk("Set b = New Box", "b.Size = 5", "b.Show"));

    [TestMethod]
    public void AWithBlock_AssignsAMemberOfItsTarget()
        => CollectionAssert.AreEqual(new[] { "6", "60" }, RunOk("Set b = New Box", "With b", ".Size = 6", ".Show", ".Level = 6", "End With", "b.Show"));

    [TestMethod]
    public void AnAssignmentToAVariable_IsLetCoercedToItsType()
        => CollectionAssert.AreEqual(new[] { "7" }, RunOk("Set b = New Box", "b.Size = \"7\"", "b.Show"));

    [TestMethod]
    public void TwoObjects_HaveTheirOwnVariables()
        => CollectionAssert.AreEqual(new[] { "1", "2" }, RunOk("Set b = New Box", "Set c = New Box", "b.Size = 1", "c.Size = 2", "b.Show", "c.Show"));

    [TestMethod]
    public void AnObjectVariable_IsSetThroughAMemberAccess_AndReadBack()
        => CollectionAssert.AreEqual(new[] { "9" }, RunOk("Set b = New Box", "Set c = New Box", "c.Size = 9", "Set b.Link = c", "Debug.Print b.Link.Size"));

    [TestMethod]
    public void SettingAnObjectVariableOfAnObjectToNothing_ReleasesWhatItHeld()
        // the box is held by the Link only: setting the variable to Nothing is the last reference going.
        => CollectionAssert.AreEqual(new[] { "term", "after" }, RunOk("Set b = New Box", "Set b.Link = New Box", "b.Link.Size = -1", "Set b.Link = Nothing", "Debug.Print \"after\""));

    [TestMethod]
    public void AnAssignmentThroughAnObjectThatIsNothing_IsError91()
    {
        var printed = Run(out var outcome, "b.Size = 5");

        Assert.IsEmpty(printed);
        Assert.IsTrue(outcome.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AnAssignmentToAMemberTheObjectDoesNotHave_IsError438()
    {
        _ = Run(out var outcome, "Set b = New Box", "b.NoSuchThing = 5");

        Assert.IsTrue(outcome.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, outcome.ErrorInfo!.ErrorId);
    }

    #endregion

    #region An array a variable holds

    [TestMethod]
    public void AnElementOfAnArrayAnObjectHolds_IsAssigned_AndReadBack()
        => CollectionAssert.AreEqual(new[] { "7", "0" }, RunOk("Set b = New Box", "b.Allocate", "b.Cells(2) = 7", "Debug.Print b.Cells(2)", "Debug.Print b.Cells(3)"));

    [TestMethod]
    public void AnElementOfAnArrayAnObjectHolds_IsAssignedThroughAWithBlock()
        => CollectionAssert.AreEqual(new[] { "5" }, RunOk("Set b = New Box", "b.Allocate", "With b", ".Cells(1) = 5", "End With", "Debug.Print b.Cells(1)"));

    [TestMethod]
    public void AnElementOfAnArrayAnObjectHolds_ThatIsOutOfBounds_IsError9()
    {
        _ = Run(out var outcome, "Set b = New Box", "b.Allocate", "b.Cells(4) = 1");

        Assert.IsTrue(outcome.IsError);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AnObjectIsSetAsAnElementOfAnArrayAnObjectHolds_AndReleasedWhenTheElementIsSetToNothing()
        // the second box is held by the element only: setting it to Nothing is the last reference going.
        => CollectionAssert.AreEqual(new[] { "9", "term", "after" }, RunOk(
            "Set b = New Box", "b.Allocate", "Set c = New Box", "c.Size = 9", "Set b.Links(1) = c", "Debug.Print b.Links(1).Size",
            "c.Size = -1", "Set c = Nothing", "Set b.Links(1) = Nothing", "Debug.Print \"after\""));

    #endregion

    #region A property

    [TestMethod]
    public void AnAssignmentToAProperty_InvokesItsPropertyLet()
        => CollectionAssert.AreEqual(new[] { "30", "30" }, RunOk("Set b = New Box", "b.Level = 3", "b.Show", "Debug.Print b.Level"));

    [TestMethod]
    public void AnIndexedProperty_IsAssignedWithItsIndexArguments()
        => CollectionAssert.AreEqual(new[] { "205" }, RunOk("Set b = New Box", "b.Item(2) = 5", "b.Show"));

    [TestMethod]
    public void ASetAssignmentToAProperty_InvokesItsPropertySet()
        => CollectionAssert.AreEqual(new[] { "8" }, RunOk("Set b = New Box", "Set c = New Box", "c.Size = 8", "Set b.Partner = c", "b.ShowLink"));

    [TestMethod]
    public void AValueAssignedToAProperty_IsLetCoercedToItsValueParameter()
        => CollectionAssert.AreEqual(new[] { "40" }, RunOk("Set b = New Box", "b.Level = \"4\"", "b.Show"));

    #endregion

    #region Through an interface

    [TestMethod]
    public void AnAssignmentThroughAnInterfaceVariable_InvokesThePropertyLetThatImplementsIt()
        // IBox.Size is a public variable; Impl implements it with a Property Let that adds a thousand to what it is given.
        => CollectionAssert.AreEqual(new[] { "1009" }, RunOk("Set m = New Impl", "Set i = m", "i.Size = 9", "m.Show"));

    [TestMethod]
    public void TheVariableOfTheInterface_IsReadBackThroughThePropertyGetThatImplementsIt()
        => CollectionAssert.AreEqual(new[] { "1009" }, RunOk("Set m = New Impl", "Set i = m", "i.Size = 9", "Debug.Print i.Size"));

    [TestMethod]
    public void AWithBlockOnTheInterfaceVariable_AssignsAndReadsThroughTheProperties()
        => CollectionAssert.AreEqual(new[] { "1009" }, RunOk("Set m = New Impl", "Set i = m", "With i", ".Size = 9", "Debug.Print .Size", "End With"));

    #endregion
}

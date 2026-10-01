using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
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
/// What the members of a class module can do with the object they are a call on and with the objects they are given:
/// a bare name that is a field of the class is that field of <c>Me</c> (<strong>MS-VBAL §2.3</strong>: the variables of
/// an object have the extent of the object), and an object passed to a parameter declared as a class is Set-assigned to
/// it (<strong>MS-VBAL §5.3.1.11</strong>).
/// </summary>
/// <remarks>
/// A class <c>Box</c> with a public <c>Size</c> field, <c>Grow(By)</c> (<c>Size = Size + By</c>), <c>Show</c> (prints
/// <c>Size</c>) and <c>Copy(Other As Box)</c> (<c>Size = Other.Size</c>); a module's <c>Main</c> has two boxes, <c>p</c> and
/// <c>q</c>.
/// </remarks>
[TestClass]
public sealed class ClassMemberRuntimeTests
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

    private static string[] Run(params string[] body)
    {
        var bodies = new Dictionary<SemanticId, InstructionList>();
        var box = new VBClassModuleSymbol(Root, Root, "Box");

        VBProcedureMemberSymbol Sub(string name, string[] statements, params (string Name, VBType Type)[] parameters)
        {
            var procedure = new VBProcedureMemberSymbol(Root, box.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
            procedure = procedure with
            {
                Parameters = [new VBParameterSymbol(Root, procedure.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo),
                    .. parameters.Select(p => new VBParameterSymbol(Root, procedure.Uri, p.Name, R, R, ParameterKind.ExplicitByVal, p.Type))],
            };
            bodies[procedure.SemanticId] = Lower(statements);
            return procedure;
        }

        var size = new VBInstanceFieldVariableMemberSymbol(Root, box.Uri, "Size", R, R, VBLongType.TypeInfo, AccessModifier.Public);
        var grow = Sub("Grow", ["Size = Size + By"], ("By", VBLongType.TypeInfo));
        var show = Sub("Show", ["Debug.Print Size"]);
        box = box with { Members = [size, grow, show] };
        var boxType = VBClassType.FromClassModule(box);
        var copy = Sub("Copy", ["Size = Other.Size"], ("Other", boxType));
        box = box with { Members = [size, grow, show, copy], DefaultInterfaceMembers = [size, grow, show, copy] };

        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var main = new VBProcedureMemberSymbol(Root, module.Uri, "Main", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        BoundTypedSymbol Local(string name) => new VBLocalVariableSymbol(main.Uri, main.Uri, name, ScopeKind.Local, R, R, ResolvedType: VBClassType.FromClassModule(box));
        main = main with { Locals = [Local("p"), Local("q")] };

        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [],
            [new StdLibSymbolProvider(Root), new Provider([module, box, size, grow, show, copy, main])], output: output);
        var pipeline = RuntimeExecutionPipeline.Create(session, bodies, Substitute.For<IVerboseMessageBuilder>());
        bodies[main.SemanticId] = Lower(body);

        var outcome = pipeline.Invoker.Invoke(main, session.Symbols.Resolver, []);
        Assert.IsTrue(outcome.IsSuccess, $"{outcome.ErrorInfo?.Description} (internal error: {outcome.IsInternalError})");
        return [.. output.Lines.Select(line => line.Trim())];
    }

    [TestMethod]
    public void ABareFieldName_InAMethod_IsThatFieldOfTheObjectTheMethodIsCalledOn()
        => CollectionAssert.AreEqual(new[] { "5" }, Run("Set p = New Box", "p.Grow 2", "p.Grow 3", "p.Show"));

    [TestMethod]
    public void TwoObjectsOfAClass_HaveTheirOwnFields()
        => CollectionAssert.AreEqual(new[] { "2", "7" }, Run("Set p = New Box", "Set q = New Box", "p.Grow 2", "q.Grow 7", "p.Show", "q.Show"));

    [TestMethod]
    public void AnObjectPassedToAClassParameter_IsTheObject_NotItsDefaultMember()
        => CollectionAssert.AreEqual(new[] { "9" }, Run("Set p = New Box", "Set q = New Box", "p.Grow 9", "q.Copy p", "q.Show"));

    [TestMethod]
    public void ASecondVariableSetToAnObject_IsTheSameObject_NotACopy()
        =>CollectionAssert.AreEqual(new[] { "4" }, Run("Set p = New Box", "Set q = p", "q.Grow 4", "p.Show"));
}

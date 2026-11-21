using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of a member of a class of the standard library on an object it reaches: the member called as the interpreter calls it
// (CompiledActivation.CallLibraryMember), on the object of an object variable of the activation, of an object field of one of its records, or of the
// enclosing With block.
internal sealed partial class ProcedureAnalysis
{
    // the members of the standard library's classes compiled code calls on an object, each pure: what it returns is computed from the object and the
    // arguments, and the call changes nothing, so the interpreter makes it again when it is handed the instruction. Count and Item of a Collection
    // (MS-VBAL §6.1.3.1.1.1, §6.1.3.1.1.2).
    private static readonly ImmutableHashSet<string> CompiledLibraryMembers = ImmutableHashSet.Create(StringComparer.Ordinal,
        "IStdCollectionClass.Count/0",
        "IStdCollectionClass.Item/1");

    // whether symbol is a member of a class of the standard library compiled code calls on an object.
    private static bool IsCompiledLibraryMember(Symbol? symbol)
        => symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol && symbol.GetProperty(SymbolProperties.ExternalTarget) is { } target
            && CompiledLibraryMembers.Contains(target) && StandardLibrary.IsLibraryMember(symbol);

    // a call of such a member, as the binder binds it: named (`C.Count`, `C.Item(1)`, `.Count` in a With block), or the default member of an object an
    // object variable or a record's object field holds, given the arguments that index it (`C(1)`, `this.Values(this.Index)`, MS-VBAL §5.6.13). The
    // member, its arguments, and the object it is called on; none for anything else.
    private (VBTypeMemberSymbol Member, ImmutableArray<BoundArgument> Arguments, int? Receiver, int? With, FieldPath? Held)? LibraryMemberCallOf(BoundNode node)
    {
        switch (node)
        {
            case BoundMember { IsLateBound: false, DeclaredClass: not null, Member: var member } named when IsCompiledLibraryMember(member)
                && TryReceiverOf(named, out var receiver, out var with, out var held):
                return named.Arguments is null ? (member!, [], receiver, with, held)
                    : named.BoundArguments is { } bound ? (member!, bound, receiver, with, held)
                    : null;

            case BoundIndex { Callee: BoundVariable variable, Index: var index }
                when ObjectSlotOf(variable, receiver: true) is { } slot
                    && variable.Variable is ITypedSymbol { ResolvedType: VBClassType { Symbol: var declared } }
                    && DefaultLibraryMemberOf(declared) is { } member && ArgumentsOf(member, index) is { } indexing:
                return (member, indexing, slot, null, null);

            case BoundMember { IsLateBound: false, DeclaredClass: null, Member: null, Arguments: { } index, Access: var access }
                when ObjectFieldOf(Bind(access)) is { } path
                    && _evaluator.DeclaredTypeOf(_session, access, new RuntimeEvaluationContext(_procedure.Uri)) is VBClassType { Symbol: var declared }
                    && DefaultLibraryMemberOf(declared) is { } member && ArgumentsOf(member, index) is { } fieldIndexing:
                return (member, fieldIndexing, null, null, path);

            default:
                return null;
        }
    }

    // the default member of a class of the standard library, when it is one compiled code calls: the member the class marks with VB_UserMemId = 0, which
    // the interpreter finds of the object (TryResolveDefaultMember).
    private VBTypeMemberSymbol? DefaultLibraryMemberOf(VBClassModuleSymbol declared)
        => (VBProjectSymbol.ResolveClass(_session.Symbols.Resolver, declared) ?? declared).DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol
            && candidate.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) && userMemId == WellKnownDispIds.Value) is { } member
            && IsCompiledLibraryMember(member) ? member : null;

    // the arguments of an index expression, given to the parameters of a default member after its implicit Me, each positional and passed by value:
    // a named or an omitted one is the interpreter's, and so are more than the member has.
    private ImmutableArray<BoundArgument>? ArgumentsOf(VBTypeMemberSymbol member, IndexExpressionNode index)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(member).RemoveAt(0);
        if (index.Arguments.Length > parameters.Length || index.Arguments.Any(argument => argument is NamedArgumentNode or MissingArgumentNode))
        {
            return null;
        }

        return [.. index.Arguments.Select((argument, i) => new BoundArgument(parameters[i], ArgumentPassing.Value, argument, Bind(argument), Plan: null, Elements: []))];
    }

    // whether node is a call compiled code makes of a member of a class of the standard library (LibraryMemberCallOf), which returns a Variant.
    private bool IsLibraryMemberVariant(BoundNode node)
        => node is BoundMember or BoundIndex && LibraryMemberCallOf(node) is { Member: var member } && ReturnedType(member) is SDK.Model.Types.VBVariantType;

    // the call, its arguments given as a function's of the standard library are (LibraryArgumentsOf): what it returns is a Variant of its own subtype,
    // a value of a scalar type the member returns held in one.
    private LibraryMemberNode? LibraryMemberOf(BoundNode node)
    {
        if (LibraryMemberCallOf(node) is not var (member, arguments, receiver, with, held))
        {
            return null;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(member).RemoveAt(0);
        if (LibraryArgumentsOf(parameters, arguments) is not { } values)
        {
            Unsupported(node);
            return null;
        }

        Libraries.Add(new CompiledLibraryCall(member, parameters, node.Expression.SourceLocation) { OnAnObject = true });
        return new LibraryMemberNode(Libraries.Count - 1, receiver, values) { WithAt = with, ReceiverField = held };
    }
}

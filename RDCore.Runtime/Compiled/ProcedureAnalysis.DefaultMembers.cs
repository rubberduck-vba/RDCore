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

// what compiled code does of a call of the default member of an object of a class of the workspace, which an index expression makes (MS-VBAL §5.6.13):
// `F(K)` of an object variable of the activation, `this.State(K)` of an object field of one of its records. The call is made as the call of a member by
// its name is (MemberCallOf), on an object of the class the object is declared as; one of another class is the interpreter's (CompiledCallee.DefaultOf).
internal sealed partial class ProcedureAnalysis
{
    // the default member an index expression calls, the class the object is declared as, the arguments bound for the member's parameters, and the object
    // it is called on: an object variable of the activation (Receiver) or an object field of a record (Held). None for anything else, nor for a class of
    // the standard library (LibraryMemberCallOf).
    private (VBTypeMemberSymbol Member, VBClassModuleSymbol Declared, ImmutableArray<BoundArgument> Arguments, int? Receiver, FieldPath? Held)? DefaultCallOf(
        BoundNode node)
    {
        switch (node)
        {
            case BoundIndex { Callee: BoundVariable variable, Index: var index }
                when ObjectSlotOf(variable, receiver: true) is { } slot
                    && variable.Variable is ITypedSymbol { ResolvedType: VBClassType { Symbol: var declared } }
                    && DefaultMemberOf(declared) is ({ } member, { } resolved) && DefaultArgumentsOf(member, index) is { } arguments:
                return (member, resolved, arguments, slot, null);

            case BoundMember { IsLateBound: false, DeclaredClass: null, Member: null, Arguments: { } index, Access: var access }
                when ObjectFieldOf(Bind(access)) is { } path
                    && _evaluator.DeclaredTypeOf(_session, access, new RuntimeEvaluationContext(_procedure.Uri)) is VBClassType { Symbol: var declared }
                    && DefaultMemberOf(declared) is ({ } member, { } resolved) && DefaultArgumentsOf(member, index) is { } arguments:
                return (member, resolved, arguments, null, path);

            default:
                return null;
        }
    }

    // the member a class of the workspace marks VB_UserMemId = 0, a function or a Property Get given Me, as the interpreter finds it of the object
    // (TryResolveDefaultMember), and the class as the session resolves it.
    private (VBTypeMemberSymbol Member, VBClassModuleSymbol Class)? DefaultMemberOf(VBClassModuleSymbol declared)
    {
        var resolved = VBProjectSymbol.ResolveClass(_session.Symbols.Resolver, declared) ?? declared;
        return resolved.DefaultInterfaceMembers.FirstOrDefault(candidate => candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol
                && candidate.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) && userMemId == WellKnownDispIds.Value) is VBTypeMemberSymbol member
            && !StandardLibrary.IsLibraryMember(member) && RuntimeProcedureInvoker.GetParameters(member) is [{ Name: Tokens.Me }, ..]
                ? (member, resolved)
                : null;
    }

    // the arguments of the index expression, bound for the parameters of the member after Me as the binder binds the arguments of a call of it
    // (MS-VBAL §5.3.1.11).
    private ImmutableArray<BoundArgument>? DefaultArgumentsOf(VBTypeMemberSymbol member, IndexExpressionNode index)
        => ArgumentMapping.Bind(RuntimeProcedureInvoker.GetParameters(member).RemoveAt(0), index.Arguments, Bind,
            argument => _evaluator.DeclaredTypeOf(_session, argument, new RuntimeEvaluationContext(_procedure.Uri)));

    // the call of the default member, made as MemberCallOf makes the call of a member by its name: on the object of an object variable, or of an object
    // field of a record, found where the record holds it.
    private CallStep? DefaultMemberCallOf(BoundNode node, int result, int offset, ScalarKind? resultKind, bool returnsValue = false)
    {
        if (DefaultCallOf(node) is not ({ } member, { } declared, var arguments, var receiver, var held))
        {
            return null;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(member);
        var made = CallWith(member, parameters.AsSpan()[1..], arguments, result, offset, resultKind, site: null, receiver, assigns: false,
            node.Expression.SourceLocation, returnsValue: returnsValue, defaultOf: declared);
        return made is null || held is null ? made : made with { ReceiverField = held };
    }
}

using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a member of an object read in an expression that is no call of a procedure of the workspace: a scalar public variable, of an
// object variable of the activation, of the default instance of a class, or of the object of the enclosing With block; and Err.Number.
internal sealed partial class ProcedureAnalysis
{
    // Err.Number (MS-VBAL §6.1.3.2.2.5), of the standard library's Err: the number of the session's error, which compiled code reads where the session
    // holds it, as the error object reads it. Err named otherwise - qualified, or a procedure of the workspace named Err - is the interpreter's.
    private static PayloadNode? ErrNumberOf(BoundMember member)
        => member is { Owner: BoundName { Symbol: VBFunctionMemberSymbol err }, Member: VBPropertyGetMemberSymbol number, Arguments: null, IsLateBound: false }
            && string.Equals(err.Name, "Err", StringComparison.OrdinalIgnoreCase) && string.Equals(number.Name, "Number", StringComparison.OrdinalIgnoreCase)
            && err.GetProperty(SymbolProperties.ExternalTarget) is not null && number.GetProperty(SymbolProperties.ExternalTarget) is not null
                ? new ErrNumberNode()
                : null;

    /// <summary>
    /// The public variables of objects the body reads, by the number compiled code knows each by: each as it is written, and the kind it is read in.
    /// </summary>
    public IReadOnlyList<CompiledMemberField> MemberFields => _memberFields;

    private readonly List<CompiledMemberField> _memberFields = [];

    // a scalar public variable of an object declared as a class, read (MS-VBAL §5.6.12): the variable the object's class has for it is found when it is read
    // (CompiledActivation.TryReadMemberField), as the interpreter finds it (EvaluateBoundMember), and read in its cell. Given arguments, it is an array the
    // arguments index, which is the interpreter's; so is a variable of Me's own class, named through Me.
    private PayloadNode? MemberFieldOf(BoundMember member)
    {
        if (member is not { DeclaredClass: not null, Member: VBModuleFieldVariableMemberSymbol field, Arguments: null, IsLateBound: false }
            || ScalarKinds.Of(field.ResolvedType) is not { } kind)
        {
            return null;
        }

        int? receiver = null;
        int? with = null;
        FieldPath? held = null;
        switch (member.Owner)
        {
            case null when _enclosingWith is { } opener:
                with = opener;
                break;

            case BoundVariable owner when (owner.Variable is VBPredeclaredInstanceSymbol ? FieldSlotOf(owner, receiver: true) : ObjectSlotOf(owner)) is { } slot:
                receiver = slot;
                break;

            case BoundMember owner when ObjectFieldOf(owner) is { } path:
                held = path;
                break;

            default:
                return null;
        }

        var number = _memberFields.Count;
        _memberFields.Add(new CompiledMemberField(member, kind));
        return new MemberFieldNode(number, receiver, kind) { WithAt = with, ReceiverField = held };
    }
}

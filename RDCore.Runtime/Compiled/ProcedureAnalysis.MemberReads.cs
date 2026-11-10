using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a member of an object read in an expression that is no call of a procedure of the workspace: a scalar public variable, or a
// String one, of an object variable of the activation, of the default instance of a class, or of the object of the enclosing With block; and Err.Number,
// Err.Description and Err.Source.
internal sealed partial class ProcedureAnalysis
{
    // Err.Number (MS-VBAL §6.1.3.2.2.5), of the standard library's Err: the number of the session's error, which compiled code reads where the session
    // holds it, as the error object reads it. Err named otherwise - qualified, or a procedure of the workspace named Err - is the interpreter's.
    private static PayloadNode? ErrNumberOf(BoundMember member) => IsErrMember(member, "Number") ? new ErrNumberNode() : null;

    // Err.Description and Err.Source (MS-VBAL §6.1.3.2.2.1, §6.1.3.2.2.6), of the standard library's Err: the text of the session's error, read where the
    // session holds it, as the error object reads it.
    private static TextNode? ErrTextOf(BoundMember member)
        => IsErrMember(member, "Description") ? new ErrTextNode(Source: false)
            : IsErrMember(member, "Source") ? new ErrTextNode(Source: true)
            : null;

    private static bool IsErrMember(BoundMember member, string name)
        => member is { Owner: BoundName { Symbol: VBFunctionMemberSymbol err }, Member: VBPropertyGetMemberSymbol read, Arguments: null, IsLateBound: false }
            && string.Equals(err.Name, "Err", StringComparison.OrdinalIgnoreCase) && string.Equals(read.Name, name, StringComparison.OrdinalIgnoreCase)
            && err.GetProperty(SymbolProperties.ExternalTarget) is not null && read.GetProperty(SymbolProperties.ExternalTarget) is not null;

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
            || ScalarKinds.Of(field.ResolvedType) is not { } kind || !TryReceiverOf(member, out var receiver, out var with, out var held))
        {
            return null;
        }

        var number = _memberFields.Count;
        _memberFields.Add(new CompiledMemberField(member, kind));
        return new MemberFieldNode(number, receiver, kind) { WithAt = with, ReceiverField = held };
    }

    // a String public variable of variable length of an object declared as a class, read as a scalar one is (MemberFieldOf): the text its cell holds
    // (CompiledActivation.TryReadMemberText).
    private TextNode? MemberTextOf(BoundMember member)
    {
        if (member is not { DeclaredClass: not null, Member: VBModuleFieldVariableMemberSymbol field, Arguments: null, IsLateBound: false }
            || !IsText(field.ResolvedType) || !TryReceiverOf(member, out var receiver, out var with, out var held))
        {
            return null;
        }

        var number = _memberFields.Count;
        _memberFields.Add(new CompiledMemberField(member, default));
        return new MemberTextNode(number, receiver) { WithAt = with, ReceiverField = held };
    }

    // the object a variable of an object is read of: the object of the enclosing With block, of an object variable of the activation or of the default
    // instance of a class, or of an object field of one of its records.
    private bool TryReceiverOf(BoundMember member, out int? receiver, out int? with, out FieldPath? held)
    {
        (receiver, with, held) = (null, null, null);
        switch (member.Owner)
        {
            case null when _enclosingWith is { } opener:
                with = opener;
                return true;

            case BoundVariable owner when (owner.Variable is VBPredeclaredInstanceSymbol ? FieldSlotOf(owner, receiver: true) : ObjectSlotOf(owner)) is { } slot:
                receiver = slot;
                return true;

            case BoundMember owner when ObjectFieldOf(owner) is { } path:
                held = path;
                return true;

            default:
                return false;
        }
    }
}

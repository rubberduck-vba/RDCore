using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// which fields compiled code reaches, and where their cells are held: the scope rule. What compiled code holds a field it reaches as is
// ProcedureAnalysis.FieldKinds.cs's.
internal sealed partial class ProcedureAnalysis
{
    /// <summary>
    /// The fields compiled code reaches, by the number it knows each by: each a variable the activation does not hold, numbered
    /// by a slot past the slots of the procedure's layout, held in the cell the interpreter resolves it to, which compiled code reads and writes it in.
    /// </summary>
    public IReadOnlyList<CompiledField> Fields => _fields;

    private readonly List<CompiledField> _fields = [];

    /// <summary>
    /// The slots of the <see cref="Fields"/>.
    /// </summary>
    public IReadOnlySet<int> FieldSlots => _fieldSlots;

    private readonly HashSet<int> _fieldSlots = [];

    // the slot of each field named, or none for one compiled code does not hold.
    private readonly Dictionary<SemanticId, int?> _slotOfField = [];

    // what the slot of a field is numbered from: past every slot of any layout.
    private const int FirstFieldSlot = 1 << 24;

    // whether a field is held on Me: an activation of the body is then a call on an object, which the frame is made for.
    private bool HasObjectFields => _fields.Exists(reached => reached.Scope is FieldScope.Object);

    // the slot of a field compiled code reaches and holds (TakesFieldOfKind), numbered the first time it is named.
    private int? FieldSlotOf(BoundVariable variable)
    {
        if (variable.IsAutoInstantiated || ScopeOf(variable.Variable) is not { } scope || variable.Variable is not ITypedSymbol typed)
        {
            return null;
        }

        var field = variable.Variable;
        if (_slotOfField.TryGetValue(field.SemanticId, out var known))
        {
            return known;
        }

        var slot = FirstFieldSlot + _fields.Count;
        int? taken = TakesFieldOfKind(typed, slot) ? slot : null;
        _slotOfField[field.SemanticId] = taken;
        if (taken is not null)
        {
            _fields.Add(new CompiledField(slot, field, scope));
            _ = _fieldSlots.Add(slot);
            _slotSymbols[slot] = field;
        }

        return taken;
    }

    // where a variable the activation does not hold is held, as the interpreter resolves it (CallStackAwareSymbolResolver, RuntimeExpressionEvaluator.BindingOf):
    // a variable of the class the procedure is a member of, on Me (MS-VBAL §5.6.10); a variable of a standard module, of any, in the session; a Static
    // variable of the procedure, on Me for a member of a class (MS-VBA: each object has its own) and in the session otherwise. Any other is the interpreter's.
    // A Static is given its storage by the call that first needs it (RuntimeProcedureInvoker.HoistLocals), which a procedure that has one is called through
    // (NeedsFrame), so its cell is there when compiled code looks for it.
    private FieldScope? ScopeOf(Symbol variable) => variable switch
    {
        VBModuleFieldVariableMemberSymbol { ScopeKind: ScopeKind.Instance } field when field.ParentUri.AbsoluteUri == _procedure.ParentUri.AbsoluteUri => FieldScope.Object,
        VBModuleFieldVariableMemberSymbol { ScopeKind: ScopeKind.Module or ScopeKind.Global } => FieldScope.Session,
        VBLocalVariableSymbol { IsStatic: true } local when local.ParentUri.AbsoluteUri == _procedure.Uri.AbsoluteUri
            => MeSlot is null ? FieldScope.Session : FieldScope.Object,
        _ => null,
    };
}

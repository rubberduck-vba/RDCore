using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.Plans;
using RDCore.Runtime.LanguageCore.Static;
using System.Collections.Immutable;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Whether a procedure is compiled, and what it compiles to: the instructions of its body, each as the payload operations
/// it is made of, in the order the interpreter's evaluation on payloads makes them.
/// </summary>
/// <remarks>
/// <para>
/// Each expression is translated as <c>RuntimeExpressionEvaluator</c> evaluates it on payloads - <c>TryEvaluatePayload</c>, <c>TryEvaluatePayloadAs</c>,
/// <c>TryEvaluateWide</c> - case for case: what the interpreter evaluates boxed, compiled code does not do, and a procedure that has any is interpreted, with
/// the reason recorded. What the interpreter's evaluation declines at run time - a conversion that is not exact, an operation that overflows - compiled code
/// declines too, and hands the activation to the interpreter at that instruction (<see cref="CompiledActivation.Interpret"/>).
/// </para>
/// <para>
/// Compiled code does, of the scalar parameters and variables of a procedure: literals, constants, arithmetic, comparisons and the
/// logical operators; <c>Let</c>; <c>If</c>, <c>Do</c>, <c>While</c> and <c>For</c>; <c>Exit</c>; and calls of procedures of the workspace whose
/// parameters are scalars - a call statement that gives arguments (a bare <c>Foo</c> binds as a name, which is not compiled), or a function's result
/// assigned whole; a function called as a statement returns a scalar.
/// </para>
/// <para>
/// It does the <c>On Error</c> statements, <c>GoSub</c> and <c>Return</c> too (IR lowering S2.4), and has the interpreter run a <c>Resume</c>, an
/// <c>Error</c> statement, an <c>On...GoTo</c> and an <c>On...GoSub</c> alone; the activation goes on compiled where the instruction left it. In a body
/// with an <c>On Error</c> statement, so does an instruction that fails: the interpreter runs it alone and gives its error the activation's handler.
/// </para>
/// <para>
/// Any other instruction compiled code does not do, the interpreter runs alone the same way (IR lowering S2.5a), with the reason recorded
/// (<see cref="Stepped"/>): a statement, a condition, a <c>GoTo</c>, a <c>For</c> that is not compiled code's. So does a procedure of a class, whose
/// <c>Me</c> is in its frame, and one that has what compiled code holds nothing of - an object, a <c>String</c>, a <c>Variant</c>, an array, a record, a
/// <c>Static</c> (<see cref="NeedsFrame"/>): the instructions that read or write it are the interpreter's, but for a Variant's (below). A body none of whose instructions that do
/// something are compiled code's is kept in the interpreter.
/// </para>
/// <para>
/// The interpreter steps a block whose state the frame holds too (IR lowering S2.5b): a <c>With</c> and its <c>End With</c>, and each statement in it
/// that uses its object; a <c>For Each</c> and its <c>Next</c>; a <c>Select Case</c> of a selector that is no scalar, or that compiled code does not
/// evaluate, and its <c>Case</c>s; and a <c>Case</c> compiled code does not compare. What a <c>For Each</c> holds is let go of where an <c>Exit For</c> leaves it (<see cref="LeavesForEach"/>).
/// </para>
/// <para>
/// A <c>Variant</c> parameter or variable is held in the cell its frame holds it in, which compiled code reads and writes
/// (<see cref="VariantSlots"/>): a <c>Let</c> of it, and an operation of a Variant operand, whose plan is deferred: each operand evaluated on payloads, and the
/// operation planned for their types, as <c>TryEvaluatePayload</c> plans it. What that evaluation declines (a Variant that holds no payload, a sum that
/// overflows its subtype) the interpreter runs alone. So does the <c>For</c> statement of a Variant counter, which decides the type its loop counts in;
/// its <c>Next</c> is stepped on payloads, with the state the statement gave the frame.
/// </para>
/// <para>
/// A <c>String</c> parameter or variable of variable length is held in the cell its frame holds it in too (<see cref="TextSlots"/>):
/// a <c>Let</c> of an expression of Strings (a literal, a String variable, two joined by <c>&amp;</c> or by <c>+</c>) writes the text it is, and two Strings
/// are compared under the module's comparison mode, as <c>TryEvaluateText</c> and <c>TryCompareText</c> evaluate them. A String that holds no text compiled
/// code reads (one that ends with a lone byte) the interpreter runs the statement of.
/// </para>
/// <para>
/// A field of <c>Me</c> of a scalar type is held in the cell the object holds it in (<see cref="Fields"/>), found where the
/// interpreter finds it (through the frame, whose object is <c>Me</c>) as the activation begins and again wherever it goes on after the interpreter ran
/// an instruction: compiled code reads and writes it as it does a variable held in a cell. So is a field of any standard module, in the cell the
/// session holds it in. Which fields compiled code reaches is one rule (<c>ProcedureAnalysis.Fields.cs</c>), and what it holds each as
/// is another (<c>ProcedureAnalysis.FieldKinds.cs</c>).
/// </para>
/// <para>
/// A <c>Select Case</c> of a scalar selector is compiled code's (IR lowering S2.5b): the selector is evaluated once into a local, and each <c>Case</c>
/// compares it on payloads, as the interpreter does (<c>CaseMatchEvaluator.TryMatchUnboxed</c>).
/// </para>
/// <para>
/// A parameter passed by reference, and a variable the procedure passes by reference, is held in a cell for the whole activation (IR lowering S2.3c),
/// which the frame holds too: the cell of a parameter is its caller's variable (MS-VBAL §5.3.1.11), and what the interpreter, a debugger or a frame made
/// later reads of either is what compiled code wrote. Every other variable is a local of the compiled code.
/// </para>
/// </remarks>
internal sealed partial class ProcedureAnalysis
{
    private readonly IRuntimeSession _session;
    private readonly RuntimeExpressionEvaluator _evaluator;
    private readonly VBTypeMemberSymbol _procedure;
    private readonly FrameLayout _layout;
    private readonly List<Ineligibility> _reasons = [];
    private readonly Dictionary<int, ScalarKind> _slots = [];
    private readonly List<CompiledForLoop> _loops = [];
    private readonly Dictionary<int, int> _loopOfOpener = [];

    private ProcedureAnalysis(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure)
    {
        (_session, _evaluator, _procedure) = (session, evaluator, procedure);
        _layout = FrameLayout.For(procedure);
    }

    /// <summary>Why the procedure is not compiled; empty when it is.</summary>
    public IReadOnlyList<Ineligibility> Reasons => _reasons;

    /// <summary>The kind of each slot: its parameters', then its variables'.</summary>
    public IReadOnlyDictionary<int, ScalarKind> Slots => _slots;

    /// <summary>The parameter or the variable each slot holds.</summary>
    public IReadOnlyDictionary<int, Symbol> SlotSymbols => _slotSymbols;

    private readonly Dictionary<int, Symbol> _slotSymbols = [];

    /// <summary>The slot of each parameter, in the order they are declared, its kind, and whether it is passed by reference.</summary>
    public List<(int Slot, ScalarKind Kind, bool ByReference)> Parameters { get; } = [];

    /// <summary>
    /// The slots held in a cell rather than a local of compiled code, in order: each parameter passed by reference, and each parameter or variable passed by
    /// reference to a procedure the body calls.
    /// </summary>
    public IReadOnlyList<int> CellSlots => [.. _cellSlots];

    private readonly SortedSet<int> _cellSlots = [];

    /// <summary>
    /// The slots of the <c>Variant</c> parameters and variables, in order: each is held in the cell its frame holds it in, which compiled
    /// code reads and writes it in.
    /// </summary>
    public IReadOnlyList<int> VariantSlots => [.. _variantSlots];

    private readonly SortedSet<int> _variantSlots = [];

    /// <summary>
    /// The slots of the object parameters and variables, in order: each is held in the cell its frame holds it in, which compiled code
    /// reads the reference it holds from, for <c>Is</c> and <c>TypeOf</c>. A <c>Set</c> of one is the interpreter's.
    /// </summary>
    public IReadOnlyList<int> ObjectSlots => [.. _objectSlots];

    private readonly SortedSet<int> _objectSlots = [];

    /// <summary>
    /// The classes the procedure's <c>TypeOf</c> expressions test against, by the number compiled code knows each by.
    /// </summary>
    public IReadOnlyList<VBClassModuleSymbol> Classes => _classes;

    private readonly List<VBClassModuleSymbol> _classes = [];

    // the For statements of a Variant counter, which the interpreter runs, and the counter's slot; their Next is compiled code's (VariantForNextStep).
    private readonly Dictionary<int, int> _variantLoops = [];

    /// <summary>
    /// The subtype each <c>Variant</c> slot is speculated to hold (<see cref="VariantSpeculation"/>): the join of the subtypes of what
    /// compiled code assigns it, and of the type a loop it counts counts in, where those are known when the procedure is compiled. A slot assigned anything
    /// else, or that compiled code assigns nothing of a known subtype, has no speculation, and is evaluated as any Variant is.
    /// </summary>
    public IReadOnlyDictionary<int, ScalarKind> SpeculatedKinds => _speculatedKinds ??= Speculate();

    private Dictionary<int, ScalarKind>? _speculatedKinds;

    // what compiled code assigns each Variant slot (VariantLetStep), and the kind each Variant counter's loop counts in.
    private readonly List<(int Slot, VariantNode Value)> _variantAssignments = [];
    private readonly List<(int Slot, ScalarKind Kind)> _variantCountings = [];

    // the speculations, joined until none changes: an assignment of an operation of the slot itself (S = S + I) is of the subtype the slot is speculated to
    // hold, which is first what its other assignments make it. A join only widens, so few passes are made.
    private Dictionary<int, ScalarKind> Speculate()
    {
        var kinds = new Dictionary<int, ScalarKind>();
        foreach (var (slot, kind) in _variantCountings)
        {
            kinds[slot] = kinds.TryGetValue(slot, out var held) ? VariantSpeculation.Join(held, kind) : kind;
        }

        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;
            foreach (var (slot, value) in _variantAssignments)
            {
                if (VariantSpeculation.KindOf(value, kinds) is not { } assigned)
                {
                    continue;
                }

                var joined = kinds.TryGetValue(slot, out var held) ? VariantSpeculation.Join(held, assigned) : assigned;
                if (!kinds.TryGetValue(slot, out var before) || before != joined)
                {
                    kinds[slot] = joined;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return kinds;
    }

    /// <summary>The kind of the function result variable; <see langword="null"/> for a <c>Sub</c>.</summary>
    public ScalarKind? ResultKind { get; private set; }

    /// <summary>
    /// Whether the function result variable is a <c>Variant</c>, which its frame holds: compiled code assigns it a payload where the interpreter assigns
    /// it (<see cref="VariantResultLetStep"/>), and reads the payload it holds (<see cref="VariantResultNode"/>).
    /// </summary>
    public bool ResultIsVariant { get; private set; }

    /// <summary>
    /// Whether the function result variable is a <c>String</c> of variable length, which its frame holds: compiled code assigns it a text where the
    /// interpreter assigns it (<see cref="TextResultLetStep"/>), and reads nothing of it.
    /// </summary>
    public bool ResultIsText { get; private set; }

    /// <summary>The <c>For</c> loops, numbered.</summary>
    public IReadOnlyList<CompiledForLoop> Loops => _loops;

    /// <summary>The instructions, compiled.</summary>
    public List<CompiledStep> Steps { get; } = [];

    /// <summary>The instruction count of the body: the offset its end is at.</summary>
    public int Length { get; private set; }

    /// <summary>
    /// Whether the body has an <c>On Error</c> statement: an instruction of it that fails is then run by the interpreter alone, its error given the
    /// activation's handler, and the activation goes on compiled where the handler sends it (<see cref="CompiledActivation.Recover"/>).
    /// </summary>
    public bool HandlesErrors { get; private set; }

    /// <summary>
    /// Analyzes <paramref name="procedure"/>, whose body is <paramref name="list"/>, as the session binds it now.
    /// </summary>
    public static ProcedureAnalysis Of(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, VBTypeMemberSymbol procedure, InstructionList list)
    {
        var analysis = new ProcedureAnalysis(session, evaluator, procedure);
        analysis.Analyze(list);

        // Me, and the fields found through it, are what a frame holds of a member of a class that nothing else makes it hold: such a member is entered
        // without one, and its activation holds Me. Any other member is called with its frame.
        analysis.EntersWithoutFrame = analysis.MeSlot is not null && !analysis.NeedsFrame;
        analysis.NeedsFrame |= analysis.MeSlot is not null || analysis.HasObjectFields;
        return analysis;
    }

    private void Analyze(InstructionList list)
    {
        Length = list.Items.Length;
        if (!AnalyzeSignature())
        {
            return;
        }

        foreach (var instruction in list.Items)
        {
            var before = _reasons.Count;
            if (instruction is { Kind: InstructionKind.ForEachOpener, End: { } past })
            {
                _forEachBlocks.Add((instruction.Offset, past));
            }

            EnterBlockOf(list, instruction);

            // a Set to Nothing of the variable a For Each walks, inside the loop, is the interpreter's: it leaves the variable as it is while the loop runs.
            var step = ForEachCollectionStaticSemantics.LoopsWhoseCollectionItSetsToNothing(list, instruction.Offset).Any()
                ? SupportCallOf(instruction, before)
                : AnalyzeInstruction(instruction);

            // what compiled code does not do of an instruction the interpreter can run alone, the interpreter does (IR lowering S2.5a): the reasons are
            // the instruction's, and not the procedure's.
            if (step is null && IsStepped(instruction))
            {
                // a Next has no statement of its own: it is at its For, as its error is.
                var location = instruction.Node?.SourceLocation ?? (instruction.Matching is { } opener ? list.Items[opener].Node?.SourceLocation : null);
                _stepped.AddRange(_reasons.Count > before
                    ? _reasons.Skip(before).Select(reason => reason.Location is null ? reason with { Location = location } : reason)
                    : [new Ineligibility(IneligibilityKind.Instruction, instruction.Kind.ToString(), location)]);
                _reasons.RemoveRange(before, _reasons.Count - before);
                step = new SteppedStep(instruction.Offset);
            }

            if (step is not null)
            {
                Steps.Add(step);
            }

            // the reasons of one instruction are enough to say why: the rest is not translated, which would only find more.
            if (_reasons.Count > 0)
            {
                return;
            }
        }

        // a loop counts in a local of compiled code: one that counts with a variable held in a cell - passed by reference, by its caller or to a callee,
        // which may be found only after the loop - is the interpreter's.
        for (var i = 0; i < _loops.Count; i++)
        {
            if (_cellSlots.Contains(_counterSlots[i]))
            {
                Refuse(IneligibilityKind.ForCounter, _loops[i].Counter.Name, _loops[i].Control.Location);
                return;
            }
        }

        // a body that does nothing of its own in compiled code where it spends its time is the interpreter's whole: what it steps is why, and compiled code
        // would only write its variables into the frame and read them back around each instruction it steps.
        if (_stepped.Count > 0 && !GainsAnything(list))
        {
            _reasons.AddRange(_stepped);
            _stepped.Clear();
        }
    }

    // what compiled code does of a body that steps instructions. For each instruction it steps in a loop, the innermost loop around that instruction must
    // have compiled code do something at each pass (a Let, a call, a Property Let or Set of an object included, a condition, a Case it compares and its
    // Select Case, the loop's back edge with its condition or a Next counting, an End With, a Debug.Print), which saves more than a step around the rest costs; a jump back
    // saves nothing, nor does a For statement, run once, nor a For Each's Next, which the interpreter runs for compiled code. What another loop does saves
    // nothing of this one's passes. When it steps nothing in a loop, anything at all will do.
    private bool GainsAnything(InstructionList list)
    {
        var loops = new List<(int First, int Last)>();
        foreach (var instruction in list.Items)
        {
            if (instruction.Kind is InstructionKind.LoopBack or InstructionKind.Jump && instruction.Target is { } target && target <= instruction.Offset)
            {
                loops.Add((target, instruction.Offset));
            }
            else if (instruction.Kind is InstructionKind.ForNext or InstructionKind.ForEachNext && instruction.Matching is { } opener)
            {
                loops.Add((opener, instruction.Offset));
            }
        }

        // a Select gains only when compiled code compares one of its Cases: a selector evaluated compiled, then each Case stepped, saves nothing.
        var compared = Steps.OfType<CaseStep>().Select(step => list.Items[step.Offset].Matching).ToHashSet();
        bool Gains(CompiledStep step) => step is LetStep or VariantLetStep or TextLetStep or TextResultLetStep or FieldLetStep or VariantFieldLetStep or ElementLetStep or VariantElementLetStep
            or SetStep or FieldSetStep or FieldSourceSetStep or CallStep or BranchStep or CaseStep or LoopBackStep or ForNextStep or VariantForNextStep or EndWithStep
            or PrintStep
            || (step is SelectStep && compared.Contains(step.Offset));
        var steppedInALoop = false;
        foreach (var stepped in Steps)
        {
            if (stepped is not SteppedStep)
            {
                continue;
            }

            // the innermost loop around the instruction: the shortest range that holds it.
            (int First, int Last)? innermost = null;
            foreach (var loop in loops)
            {
                if (stepped.Offset >= loop.First && stepped.Offset <= loop.Last && (innermost is not { } shortest || loop.Last - loop.First < shortest.Last - shortest.First))
                {
                    innermost = loop;
                }
            }

            if (innermost is not { } around)
            {
                continue;
            }

            steppedInALoop = true;
            if (!Steps.Exists(step => Gains(step) && step.Offset >= around.First && step.Offset <= around.Last))
            {
                return false;
            }
        }

        return steppedInALoop || Steps.Exists(step => Gains(step) || step is ForOpenStep or ForEachOpenStep);
    }

    private void Refuse(IneligibilityKind kind, string detail, SourceLocation? location = null) => _reasons.Add(new Ineligibility(kind, detail, location));

    // an instruction the interpreter runs alone, compiled code going on where it leaves the activation: every one whose state the frame holds - a With's
    // target, a For Each's enumerator, a Select's selector, which compiled code writes into the frame before a Case it does not compare.
    private static bool IsStepped(Instruction instruction) => instruction.Kind is InstructionKind.Simple or InstructionKind.ConditionalBranch
        or InstructionKind.LoopBack or InstructionKind.Jump or InstructionKind.ExitLoop or InstructionKind.ForOpener or InstructionKind.ForNext
        or InstructionKind.Halt or InstructionKind.Break or InstructionKind.With or InstructionKind.EndWith or InstructionKind.ForEachOpener
        or InstructionKind.ForEachNext or InstructionKind.Select;


    private readonly List<Ineligibility> _stepped = [];

    /// <summary>
    /// What the interpreter runs alone of a compiled procedure, and why: each instruction compiled code does not do, which is no reason for the procedure to
    /// be interpreted (IR lowering S2.5a).
    /// </summary>
    public IReadOnlyList<Ineligibility> Stepped => _stepped;

    /// <summary>
    /// Whether the activation holds what compiled code does not - <c>Me</c>, an object, a <c>String</c>, a <c>Variant</c>, an array, a record, a
    /// <c>Static</c> - in its frame, from its first instruction: the body is not called directly, without one, and the instructions that read or write any of
    /// it are the interpreter's.
    /// </summary>
    public bool NeedsFrame { get; private set; }

    /// <summary>
    /// The slot of <c>Me</c>, an object slot (<see cref="ObjectSlots"/>), when the procedure is a member of a class; <see langword="null"/> otherwise.
    /// </summary>
    public int? MeSlot { get; private set; }

    /// <summary>
    /// Whether the procedure is a member of a class whose frame holds nothing compiled code does not but <c>Me</c>: a call compiled
    /// code makes of it on an object enters it without a frame, as it enters a procedure (<see cref="CompiledActivation.EnterMemberDirectly"/>), its
    /// activation holding <c>Me</c>, and its fields found on the object.
    /// </summary>
    public bool EntersWithoutFrame { get; private set; }

    // where a declaration is, which is what a reason it gives is reported at.
    private static SourceLocation LocationOf(BoundTypedSymbol declared) => new(declared.Uri, declared.Range);

    private static SourceLocation LocationOf(VBTypeMemberSymbol declared) => new(declared.Uri, declared.Range);

    // the procedure itself: a Sub, a Function or a Property procedure, of a standard module or a class. Its scalar parameters and variables are compiled
    // code's; what else it has is its frame's (NeedsFrame).
    private bool AnalyzeSignature()
    {
        if (_procedure is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol
            or VBPropertySetMemberSymbol))
        {
            Refuse(IneligibilityKind.MemberOfAClass, _procedure.Name, LocationOf(_procedure));
            return false;
        }

        // a member of a class is called on an object, Me (MS-VBAL §5.6.11), which its frame holds: what reads it, the interpreter runs (IR lowering S2.5a).
        var parameters = RuntimeProcedureInvoker.GetParameters(_procedure);
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            // Me is held as an object parameter is (below), in the cell its frame holds it in - or its activation, for a member entered without a frame.
            if (i == 0 && parameter.Name == Tokens.Me && IsObjectType(parameter.ResolvedType) && _layout.ParameterSlots[i] >= 0)
            {
                MeSlot = _layout.ParameterSlots[i];
                _ = _objectSlots.Add(_layout.ParameterSlots[i]);
                _slotSymbols[_layout.ParameterSlots[i]] = parameter;
                continue;
            }

            // a Variant is held in the cell its frame holds it in, which compiled code reads and writes.
            if (parameter is not ParamArrayParameterSymbol && parameter.ResolvedType is VBVariantType && _layout.ParameterSlots[i] >= 0)
            {
                NeedsFrame = true;
                _ = _variantSlots.Add(_layout.ParameterSlots[i]);
                _slotSymbols[_layout.ParameterSlots[i]] = parameter;
                continue;
            }

            // an object is held in the cell its frame holds it in, whose reference compiled code reads.
            if (parameter is not ParamArrayParameterSymbol && IsObjectType(parameter.ResolvedType) && _layout.ParameterSlots[i] >= 0)
            {
                NeedsFrame = true;
                _ = _objectSlots.Add(_layout.ParameterSlots[i]);
                _slotSymbols[_layout.ParameterSlots[i]] = parameter;
                continue;
            }

            // a record is held in the cell its frame holds it in, whose fields compiled code reads and writes.
            if (parameter is not ParamArrayParameterSymbol && TryHoldRecord(parameter.ResolvedType, _layout.ParameterSlots[i], parameter))
            {
                continue;
            }

            // a String is held in the cell its frame holds it in, whose text compiled code reads and writes.
            if (parameter is not ParamArrayParameterSymbol && IsText(parameter.ResolvedType) && _layout.ParameterSlots[i] >= 0)
            {
                HoldText(_layout.ParameterSlots[i], parameter);
                continue;
            }

            // what is not a scalar the frame holds: a ParamArray, an object, a String, a Variant, an array, a record; and Me.
            if (parameter is ParamArrayParameterSymbol || ScalarKinds.Of(parameter.ResolvedType) is not { } kind || _layout.ParameterSlots[i] < 0)
            {
                NeedsFrame = true;
                continue;
            }

            var byReference = RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind);
            _slots[_layout.ParameterSlots[i]] = kind;
            _slotSymbols[_layout.ParameterSlots[i]] = parameter;
            Parameters.Add((_layout.ParameterSlots[i], kind, byReference));
            if (byReference)
            {
                _ = _cellSlots.Add(_layout.ParameterSlots[i]);
            }
        }

        var locals = RuntimeProcedureInvoker.GetLocals(_procedure);
        for (var i = 0; i < locals.Length; i++)
        {
            switch (locals[i])
            {
                case VBLocalConstantSymbol:
                    continue;
                case VBLocalVariableSymbol { IsStatic: false } variable when ScalarKinds.Of(variable.ResolvedType) is { } kind && _layout.LocalSlots[i] >= 0:
                    _slots[_layout.LocalSlots[i]] = kind;
                    _slotSymbols[_layout.LocalSlots[i]] = variable;
                    continue;
                case VBLocalVariableSymbol { IsStatic: false, ResolvedType: VBVariantType } variable when _layout.LocalSlots[i] >= 0:
                    NeedsFrame = true;
                    _ = _variantSlots.Add(_layout.LocalSlots[i]);
                    _slotSymbols[_layout.LocalSlots[i]] = variable;
                    continue;
                case VBLocalVariableSymbol { IsStatic: false } variable when IsObjectType(variable.ResolvedType) && _layout.LocalSlots[i] >= 0:
                    NeedsFrame = true;
                    _ = _objectSlots.Add(_layout.LocalSlots[i]);
                    _slotSymbols[_layout.LocalSlots[i]] = variable;
                    continue;
                case VBLocalVariableSymbol { IsStatic: false } variable when TryHoldRecord(variable.ResolvedType, _layout.LocalSlots[i], variable):
                    continue;
                case VBLocalVariableSymbol { IsStatic: false } variable when IsText(variable.ResolvedType) && _layout.LocalSlots[i] >= 0:
                    HoldText(_layout.LocalSlots[i], variable);
                    continue;

                // a Static outlives the activation, and what is no scalar is held as the interpreter holds it: both are the frame's, and the instructions that
                // read or write them the interpreter's.
                default:
                    NeedsFrame = true;
                    continue;
            }
        }

        // a function result that is no scalar is the frame's too, and so is what assigns it.
        if (_procedure is VBReturningMemberSymbol returning)
        {
            if (ScalarKinds.Of(returning.ResolvedType) is not { } result)
            {
                NeedsFrame = true;
                ResultIsVariant = returning.ResolvedType is VBVariantType;
                ResultIsText = IsText(returning.ResolvedType);
            }
            else
            {
                ResultKind = result;
            }
        }

        return _reasons.Count == 0;
    }

    private CompiledStep? AnalyzeStep(Instruction instruction)
    {
        var location = instruction.Node?.SourceLocation;
        switch (instruction.Kind)
        {
            case InstructionKind.Simple when instruction.Node is AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment:
                return AnalyzeLet(instruction.Offset, assignment);

            case InstructionKind.Simple when instruction.Node is AssignmentStatementNode { Kind: AssignmentKind.Set } set:
                return AnalyzeSet(instruction, set);

            // a call statement compiled code does not make is the interpreter's, run as a support call (SupportCallOf).
            case InstructionKind.Simple when instruction.Node is CallStatementNode call:
                var mark = _reasons.Count;
                return Bind(call.Arguments.IsEmpty ? call.Callee : new IndexExpressionNode(call.Callee.Identity, call.Callee.Location, call.Callee, call.Arguments)) switch
                {
                    BoundCall bound when CallOf(bound, CallStep.NoResult, instruction.Offset, resultKind: null) is { } made => made,
                    BoundMember member when MemberCallOf(member, CallStep.NoResult, instruction.Offset, resultKind: null) is { } madeOn => madeOn,
                    BoundName { Symbol: (VBProcedureMemberSymbol or VBFunctionMemberSymbol) and VBTypeMemberSymbol named }
                        when BareCallOf(named, instruction.Offset, kind: null, call.Callee) is { } bare => bare,
                    _ => SupportCallOf(instruction, mark),
                };

            // MS-VBAL §5.4.2.20: a RaiseEvent calls each handler of the event, which may write an argument passed to it by reference back into a variable of
            // the activation. It is the interpreter's statement, run as a support call, as a call statement compiled code does not make is.
            case InstructionKind.Simple when instruction.Node is KeywordStatementNode { Token: Tokens.RaiseEvent } && SupportCallOf(instruction, _reasons.Count) is { } raise:
                return raise;

            case InstructionKind.Simple when ArrayStatementOf(instruction) is { } arrayStatement:
                return arrayStatement;

            case InstructionKind.Simple when instruction.Node is DebugPrintStatementNode print:
                return PrintOf(instruction.Offset, print);

            case InstructionKind.Simple:
                Refuse(IneligibilityKind.Statement, StatementNameOf(instruction.Node), location);
                return null;

            case InstructionKind.ConditionalBranch when instruction.Matching is null && instruction.Else is { } otherwise:
                return ConditionOf(instruction.Node) is ({ } condition, var negate) && Condition(condition) is { } value
                    ? new BranchStep(instruction.Offset, value, negate, otherwise)
                    : Refused(instruction);

            case InstructionKind.LoopBack when instruction.Target is { } target:
                return ConditionOf(instruction.Node) is ({ } loopCondition, var loopNegate) && Condition(loopCondition) is { } loopValue
                    ? new LoopBackStep(instruction.Offset, loopValue, loopNegate, target)
                    : Refused(instruction);

            case InstructionKind.Jump or InstructionKind.ExitLoop when instruction.Node is GoToStatementNode:
                Refuse(IneligibilityKind.GoTo, "GoTo", location);
                return null;

            case InstructionKind.Jump or InstructionKind.ExitLoop when instruction.Target is { } jumpTarget:
                return new JumpStep(instruction.Offset, jumpTarget);

            case InstructionKind.ForOpener when instruction.Node is ForStatementNode forStatement && instruction.End is { } past:
                return AnalyzeForOpener(instruction.Offset, forStatement, past);

            case InstructionKind.ForNext when instruction.Matching is { } variantOpener && instruction.Target is { } variantBody
                && _variantLoops.TryGetValue(variantOpener, out var variantCounter):
                return new VariantForNextStep(instruction.Offset, variantOpener, variantBody, variantCounter);

            case InstructionKind.ForNext when instruction.Matching is { } opener && instruction.Target is { } body && _loopOfOpener.TryGetValue(opener, out var loop):
                return new ForNextStep(instruction.Offset, loop, CounterSlotOf(loop), _loops[loop].Kind, body);

            case InstructionKind.Select when instruction.Node is SelectCaseStatementNode selectCase && instruction.End is { } end:
                return AnalyzeSelect(instruction.Offset, selectCase, end);

            case InstructionKind.ConditionalBranch when instruction.Matching is { } select && instruction.Else is { } next
                && instruction.Node is CaseExpressionStatementNode caseBlock && _selectOfOpener.TryGetValue(select, out var selectNumber):
                return AnalyzeCase(instruction.Offset, caseBlock, selectNumber, next);

            // an Exit goes through the End statement, as the interpreter's does.
            case InstructionKind.ExitProcedure when instruction.Target is { } end:
                return new JumpStep(instruction.Offset, end);

            case InstructionKind.ExitProcedure:
                return new ExitStep(instruction.Offset);

            case InstructionKind.OnErrorGoTo when instruction.Target is not null:
            case InstructionKind.OnErrorDisable or InstructionKind.OnErrorReset or InstructionKind.OnErrorResumeNext:
                HandlesErrors = true;
                return new OnErrorStep(instruction.Offset);

            case InstructionKind.GoSub when instruction.Target is { } subroutine:
                return new GoSubStep(instruction.Offset, subroutine);

            case InstructionKind.Return:
                return new ReturnStep(instruction.Offset);

            case InstructionKind.EndWith:
                return EndWithOf(instruction) ?? Refused(instruction);

            case InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext or InstructionKind.ResumeLabel or InstructionKind.RaiseError
                or InstructionKind.JumpTable or InstructionKind.GoSubTable:
                return new SteppedStep(instruction.Offset);

            // a For Each statement and its Next, run by the interpreter for compiled code (ProcedureAnalysis.ForEach.cs).
            case InstructionKind.ForEachOpener or InstructionKind.ForEachNext:
                return ForEachStepOf(instruction) ?? Refused(instruction);

            default:
                return Refused(instruction);
        }
    }

    // what a statement is called: its node's type, less what every node's type ends with - DebugPrint, Assignment, Open.
    private static string StatementNameOf(StatementNode? node)
    {
        var name = node?.GetType().Name ?? nameof(InstructionKind.Simple);
        foreach (var suffix in new[] { "StatementNode", "Node" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    private CompiledStep? Refused(Instruction instruction)
    {
        if (_reasons.Count == 0)
        {
            Refuse(IneligibilityKind.Instruction, instruction.Kind.ToString(), instruction.Node?.SourceLocation);
        }

        return null;
    }

    // MS-VBAL §5.4.3.8, as TryAssignUnboxed assigns: a scalar variable of the activation, or the function result variable, given its value let-coerced to
    // its type.
    private CompiledStep? AnalyzeLet(int offset, AssignmentStatementNode assignment)
    {
        if (assignment.Target is MemberAccessExpressionNode)
        {
            return IsMemberOfAnObject(Bind(assignment.Target)) ? MemberLetOf(offset, assignment) : AnalyzeFieldLet(offset, assignment);
        }

        if (assignment.Target is IndexExpressionNode)
        {
            return AnalyzeElementLet(offset, assignment);
        }

        if (assignment.Target is not SimpleNameExpressionNode)
        {
            Refuse(IneligibilityKind.AssignmentTarget, assignment.Target.GetType().Name, assignment.SourceLocation);
            return null;
        }

        // a function called for the whole value assigned, of the kind of what it is assigned to, is a call step: nothing of it can fail after the call.
        var assigned = Bind(assignment.Value);
        switch (Bind(assignment.Target))
        {
            case BoundName { Symbol: var function } when ResultKind is { } result && IsOwnFunction(function):
                return assigned switch
                {
                    BoundCall resultCall when !IsCompiledLibrary(resultCall.Procedure) && !IsVariantLibrary(resultCall.Procedure)
                        => CallOf(resultCall, -1, offset, result) ?? RefusedCall(resultCall.Call.Callee, assignment.SourceLocation),
                    BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } resultMember when ErrNumberOf(resultMember) is null
                        => MemberCallOf(resultMember, -1, offset, result)
                        ?? RefusedCall(resultMember.Access, assignment.SourceLocation),
                    _ => PayloadAs(assigned, result) is { } value ? new LetStep(offset, -1, value) : null,
                };
            case BoundVariable variable when SlotOf(variable) is { } slot:
                return assigned switch
                {
                    BoundCall slotCall when !IsCompiledLibrary(slotCall.Procedure) && !IsVariantLibrary(slotCall.Procedure)
                        => CallOf(slotCall, slot, offset, _slots[slot]) ?? RefusedCall(slotCall.Call.Callee, assignment.SourceLocation),
                    BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } slotMember when ErrNumberOf(slotMember) is null
                        => MemberCallOf(slotMember, slot, offset, _slots[slot])
                        ?? RefusedCall(slotMember.Access, assignment.SourceLocation),
                    _ => PayloadAs(assigned, _slots[slot]) is { } slotValue ? new LetStep(offset, slot, slotValue) : null,
                };

            // the result of a function of the Variant type: assigned what a call returns, as a Variant variable is, or the payload of a Variant expression.
            case BoundName { Symbol: var function } when ResultIsVariant && IsOwnFunction(function):
                if (AssignedCallOf(assigned, offset) is { } resultAssigned)
                {
                    return resultAssigned;
                }

                return Dynamic(assigned) is { } resultValue ? new VariantResultLetStep(offset, resultValue) : null;

            // a Variant is assigned the payload as it is, of its own type (MS-VBAL §5.5.1.2.12), as TryAssignUnboxed assigns it - or what a call
            // returns, whatever it is, once the call returned.
            case BoundVariable variable when VariantSlotOf(variable) is { } variantSlot:
                if (AssignedCallOf(assigned, offset) is { } slotAssigned)
                {
                    return slotAssigned;
                }

                if (Dynamic(assigned) is not { } variantValue)
                {
                    return null;
                }

                _variantAssignments.Add((variantSlot, variantValue));
                return new VariantLetStep(offset, variantSlot, variantValue);

            // a String is assigned the text as it is, as TryAssignUnboxed assigns it; or what a call returns, once the call returned, as the interpreter
            // assigns it.
            case BoundVariable variable when TextSlotOf(variable) is { } textSlot:
                if (AssignedCallOf(assigned, offset) is { } textSlotAssigned)
                {
                    return textSlotAssigned;
                }

                return Text(assigned) is { } text ? new TextLetStep(offset, textSlot, text) : null;

            // the result of a function of the String type, assigned the text as it is where its frame holds it, or what a call returns, once the call
            // returned, as the interpreter assigns it.
            case BoundName { Symbol: var function } when ResultIsText && IsOwnFunction(function):
                if (AssignedCallOf(assigned, offset) is { } textAssigned)
                {
                    return textAssigned;
                }

                return Text(assigned) is { } resultText ? new TextResultLetStep(offset, resultText) : null;
            default:
                Refuse(IneligibilityKind.AssignmentTarget, ((SimpleNameExpressionNode)assignment.Target).IdentifierName, assignment.SourceLocation);
                return null;
        }
    }

    /// <summary>The procedures the body calls, by the number its call steps know each by.</summary>
    public List<CompiledCallee> Callees { get; } = [];

    // a call of a procedure of the workspace in a standard module - no Me to give it - every parameter a scalar of its own kind, none a Variant, and given an
    // argument of its kind, or a String given Strings (TextArgumentOf); of a function returning resultKind when what it returns is read, and of a scalar
    // kind when it is not - what it returns is let go of as a temporary of the statement, which compiled code holds none of. A parameter passed by
    // reference is given the cell of a variable of the activation of its kind, which is held in one from then on, the address of a String variable, or a
    // cell of its own holding a value that is no variable (MS-VBAL §5.3.1.11). It is made with RuntimeProcedureInvoker.InvokeScalars, as
    // RuntimeExpressionEvaluator.TryInvokeScalars makes a call of a procedure that has parameters - and with the arguments as the interpreter passes them
    // (CellReference, VBRuntimeReference) when any is passed by reference. A member of the class whose code runs, named bare, is called on Me as
    // MemberCallOf calls one (OwnMemberReceiver), a Property Get of it for what it returns.
    private CallStep? CallOf(BoundCall call, int result, int offset, ScalarKind? resultKind, bool assigns = false, bool returnsObject = false)
    {
        if (call is not { Procedure: VBTypeMemberSymbol procedure, BoundArguments: { } arguments, ResultType: null }
            || procedure.GetProperty(SymbolProperties.Library) is not null || procedure.GetProperty(SymbolProperties.ExternalTarget) is not null
            || procedure is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol))
        {
            return null;
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(procedure).AsSpan();
        int? receiver = null;
        if (parameters is [{ Name: Tokens.Me }, ..])
        {
            if (OwnMemberReceiver(procedure, call.Call.Callee) is not { } me || procedure is VBPropertyGetMemberSymbol && resultKind is null && !assigns)
            {
                return null;
            }

            parameters = parameters[1..];
            receiver = me;
        }
        else if (procedure is VBPropertyGetMemberSymbol)
        {
            return null;
        }

        if (parameters.Length != arguments.Length)
        {
            return null;
        }

        return CallWith(procedure, parameters, arguments, result, offset, resultKind, site: null, receiver, assigns, call.Call.SourceLocation, returnsObject);
    }

    // a member of the class whose code runs, named bare (`Helper`, `Twice(3)`), is a member of the current instance (MS-VBAL §5.6.10), called on the
    // activation's own Me, as the interpreter calls it (InvokeProcedureFrom): the Me slot is the object it is called on. A member of another module, named
    // through it, is no such call.
    private int? OwnMemberReceiver(VBTypeMemberSymbol procedure, ExpressionNode callee)
        => MeSlot is { } meSlot && callee is SimpleNameExpressionNode && procedure.ParentUri.AbsoluteUri == _procedure.ParentUri.AbsoluteUri ? meSlot : null;

    // a call of a function of the Variant type, or of a scalar type, whose result the statement assigns to a Variant - a variable of the activation, or the
    // result of its own function - as the interpreter assigns it (CompiledCallee.AssignsResult). An expression that is no such call is evaluated as any
    // is: the reasons this found for it are not the statement's.
    private CallStep? AssignedCallOf(BoundNode assigned, int offset)
    {
        var before = _reasons.Count;
        var call = assigned switch
        {
            BoundCall bound => CallOf(bound, CallStep.NoResult, offset, resultKind: null, assigns: true),
            BoundMember { Member: VBFunctionMemberSymbol or VBPropertyGetMemberSymbol } member => MemberCallOf(member, CallStep.NoResult, offset, resultKind: null, assigns: true),
            BoundName { Symbol: (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and VBTypeMemberSymbol named } when !IsOwnFunction(named)
                => BareCallOf(named, offset, kind: null, assigned.Expression, assigns: true),
            _ => null,
        };

        if (call is null)
        {
            _reasons.RemoveRange(before, _reasons.Count - before);
        }

        return call;
    }

    // a call of a member of an object variable of the activation declared as a class, its parameters after Me taken as CallOf takes a
    // procedure's. The member is the one the class the variable is declared as has: the one the object's class invokes for it is found when the call is
    // made (CompiledActivation.CanCallMember), as the interpreter finds it (EvaluateBoundMember), and the call is made as the interpreter makes it, the
    // object given as Me. A member written with no object before the dot is a member of the object of the enclosing With block, declared as a class: the
    // object is the one the interpreter holds for the block, found when the call is made (CompiledActivation.CanCallMemberOfWith). A Property Get is called
    // as a function is, for what it returns (MS-VBAL §5.3.1.7), never as a statement. A member of the default instance of a class (MS-VBAL §5.2.4.1.2) is
    // called on the object the session holds for it; one that holds none yet is the interpreter's, which makes the object (MS-VBAL §5.2.3.1.4). A member of
    // an object field of one of the procedure's records (this.Owner.Name) is called on the object the field holds, found where the record holds it.
    private CallStep? MemberCallOf(BoundMember call, int result, int offset, ScalarKind? resultKind, bool assigns = false, bool returnsObject = false)
    {
        if (call.Member is VBPropertyGetMemberSymbol && resultKind is null && !assigns && !returnsObject)
        {
            return null;
        }

        // a member of Me: the object is the activation's own, held in its Me slot.
        if (call is { DeclaredClass: not null, Member: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, Owner: BoundInstance { Slot: { } me }, IsLateBound: false }
            && MeSlot is { } meSlot && ReferenceEquals(me.Layout, _layout) && me.Index == meSlot)
        {
            return MemberCallWith(call, result, offset, resultKind, meSlot, assigns);
        }

        if (call is { DeclaredClass: not null, Member: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, Owner: null, IsLateBound: false }
            && _enclosingWith is { } with)
        {
            return MemberCallWith(call, result, offset, resultKind, receiver: null, assigns, returnsObject) is { } made ? made with { WithAt = with } : null;
        }

        if (call is { DeclaredClass: not null, Member: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, Owner: BoundMember field,
                IsLateBound: false }
            && ObjectFieldOf(field) is { } path)
        {
            return MemberCallWith(call, result, offset, resultKind, receiver: null, assigns, returnsObject) is { } made ? made with { ReceiverField = path } : null;
        }

        // a member of what a call returns (`S.Pos.Distance(X)`, `Make().Pos`): that call is made first (ProcedureAnalysis.ObjectCalls.cs).
        if (call is { DeclaredClass: not null, Member: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, Owner: { } called,
                IsLateBound: false }
            && ObjectCallOf(called) is { } returned)
        {
            return MemberCallWith(call, result, offset, resultKind, receiver: null, assigns, returnsObject) is { } made ? made with { ReceiverCall = returned } : null;
        }

        if (call is not { DeclaredClass: not null, Member: VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, Owner: BoundVariable owner,
                IsLateBound: false }
            || (owner.Variable is VBPredeclaredInstanceSymbol ? DefaultInstanceSlotOf(owner, call.Member) : ObjectSlotOf(owner)) is not { } receiver)
        {
            return null;
        }

        return MemberCallWith(call, result, offset, resultKind, receiver, assigns, returnsObject);
    }

    // the default instance of a class of the workspace a member is called on, held as an object field is: a member of one of the standard library's is no
    // procedure given Me, which the interpreter calls.
    private int? DefaultInstanceSlotOf(BoundVariable owner, VBTypeMemberSymbol member)
        => RuntimeProcedureInvoker.GetParameters(member) is [{ Name: Tokens.Me }, ..] ? FieldSlotOf(owner, receiver: true) : null;

    private CallStep? MemberCallWith(BoundMember call, int result, int offset, ScalarKind? resultKind, int? receiver, bool assigns, bool returnsObject = false)
    {

        var parameters = RuntimeProcedureInvoker.GetParameters(call.Member!);
        if (parameters is not [{ Name: Tokens.Me }, ..])
        {
            return null;
        }

        // a member called with no parentheses is given no argument; one written with them is given the arguments bound for it.
        var arguments = call.BoundArguments ?? (call.Arguments is null ? [] : default);
        if (arguments.IsDefault || arguments.Length != parameters.Length - 1)
        {
            return null;
        }

        return CallWith(call.Member!, parameters.AsSpan()[1..], arguments, result, offset, resultKind, call, receiver, assigns,
            ((SyntaxNode?)call.Arguments ?? call.Access).SourceLocation, returnsObject);
    }

    // at: where the call expression is, which the activation is located at while the callee runs (CompiledCallee.CallLocation); none for a call written
    // nowhere, located by its statement.
    private CallStep? CallWith(VBTypeMemberSymbol procedure, ReadOnlySpan<VBParameterSymbol> parameters, ImmutableArray<BoundArgument> arguments, int result, int offset,
        ScalarKind? resultKind, BoundMember? site, int? receiver, bool assigns, SourceLocation? at, bool returnsObject = false)
    {
        // what is returned is read in its own kind, as it is assigned: a Function of another kind, or a Sub whose result is read, is not this call. A
        // Function called as a statement returns a scalar or a Variant: what the Variant holds is let go of when the statement is over, as the interpreter
        // lets go of it (CompiledActivation.EndStatement); an object, or a String, it returned otherwise would be held by the statement until it ends. A
        // call whose result the statement assigns returns a Variant, a scalar or a String. One whose result compiled code calls a member of returns an object.
        if (returnsObject ? !IsObjectType(ReturnedType(procedure))
            : assigns ? !ReturnsAssignable(procedure)
            : resultKind is { } read ? ReturnedType(procedure) is not { } returned || ScalarKinds.Of(returned) != read
            : ReturnedType(procedure) is { } discarded && ScalarKinds.Of(discarded) is null && discarded is not VBVariantType)
        {
            return null;
        }

        var kinds = new List<ScalarKind?>(parameters.Length);
        var byReference = new List<bool>(parameters.Length);
        var values = new List<PayloadNode>(parameters.Length);
        var escaping = new List<int>();
        ObjectParameter?[]? objects = null;
        for (var i = 0; i < parameters.Length; i++)
        {
            // an object, given the object a variable holds, Me or Nothing, or the address of an object variable of the activation. A call given an object is
            // never made through a direct entry: the invoker or the member's binding gives the object to the parameter, as the interpreter gives it
            // (CompiledActivation.SetObject), and counts it for as long as the callee runs.
            if (parameters[i] is not ParamArrayParameterSymbol && IsObjectType(parameters[i].ResolvedType))
            {
                if (ObjectArgumentOf(arguments[i]) is not { } passed)
                {
                    return null;
                }

                (objects ??= new ObjectParameter?[parameters.Length])[i] = new ObjectParameter(arguments[i].Expression, parameters[i].ResolvedType);
                kinds.Add(null);
                byReference.Add(RuntimeProcedureInvoker.IsByRef(parameters[i].ParameterKind));
                values.Add(passed);
                continue;
            }

            // a String of a procedure of a standard module, given its text or the cell of a String of the activation.
            if (parameters[i] is not ParamArrayParameterSymbol && site is null && receiver is null && IsText(parameters[i].ResolvedType))
            {
                if (TextArgumentOf(arguments[i]) is not { } text)
                {
                    return null;
                }

                kinds.Add(null);
                byReference.Add(RuntimeProcedureInvoker.IsByRef(parameters[i].ParameterKind));
                values.Add(text);
                continue;
            }

            if (parameters[i] is ParamArrayParameterSymbol || ScalarKinds.Of(parameters[i].ResolvedType) is not { } kind || ArgumentOf(arguments[i], kind) is not { } value)
            {
                return null;
            }

            kinds.Add(kind);
            byReference.Add(RuntimeProcedureInvoker.IsByRef(parameters[i].ParameterKind));
            values.Add(value);
            if (value is ReferenceNode reference)
            {
                escaping.Add(reference.Slot);
            }
        }

        // the variables passed by reference are held in cells from the start of the activation, which the call is made with.
        _cellSlots.UnionWith(escaping);
        Callees.Add(new CompiledCallee(procedure, kinds, resultKind, byReference)
        {
            Site = site,
            CallsOwnMember = site is null && receiver is not null,
            CallLocation = at,
            AssignsResult = assigns,
            HoldsWhatItReturns = assigns || resultKind is null && ReturnedType(procedure) is VBVariantType,
            ReturnsObject = returnsObject,
            ObjectParameters = objects ?? [],
        });
        return new CallStep(offset, Callees.Count - 1, values, result, receiver);
    }

    /// <summary>
    /// Whether <paramref name="procedure"/> is a function whose result a statement assigns once the call returned (<see cref="CompiledCallee.AssignsResult"/>):
    /// of the <c>Variant</c> type, of a scalar type, or of the <c>String</c> type of variable length.
    /// </summary>
    /// <param name="procedure">The procedure called.</param>
    internal static bool ReturnsAssignable(VBTypeMemberSymbol procedure)
        => ReturnedType(procedure) is { } returned && (returned is VBVariantType || ScalarKinds.Of(returned) is not null || IsText(returned));

    /// <summary>
    /// The type of what <paramref name="procedure"/> returns when it is a <c>Function</c> or a <c>Property Get</c> (MS-VBAL §5.3.1.6, §5.3.1.7);
    /// <see langword="null"/> for a <c>Sub</c>, a <c>Property Let</c> or a <c>Property Set</c>, which return nothing.
    /// </summary>
    internal static VBType? ReturnedType(VBTypeMemberSymbol procedure)
        => procedure is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol ? ((VBReturningMemberSymbol)procedure).ResolvedType : null;

    // the argument of a parameter of kind: a value let-coerced to it, for one passed by value or for one passed by reference that is given no variable; the
    // cell of a variable of the activation of that very kind, for one passed by reference that is given it. A variable of another kind, or of another
    // activation (a field, an element), is the interpreter's to pass. An optional parameter the call leaves out is given its default (OmittedArgumentOf).
    private PayloadNode? ArgumentOf(BoundArgument argument, ScalarKind kind)
    {
        switch (argument)
        {
            case { Passing: ArgumentPassing.Omitted } when OmittedArgumentOf(argument.Parameter, kind) is { } omitted:
                return BoundArgument.IsByRef(argument.Parameter) ? new TemporaryNode(omitted) : omitted;
            case { Passing: ArgumentPassing.Value, Value: { } given } when BoundArgument.IsByRef(argument.Parameter):
                return PayloadAs(given, kind) is { } copied ? new TemporaryNode(copied) : null;
            case { Passing: ArgumentPassing.Value, Value: { } given }:
                return PayloadAs(given, kind);
            case { Passing: ArgumentPassing.Reference, Value: BoundVariable variable } when SlotOf(variable) is { } slot && _slots[slot] == kind:
                return new ReferenceNode(slot, kind);
            default:
                return null;
        }
    }

    // MS-VBAL §5.3.1.11: what an optional parameter of kind the call leaves out is given, as the interpreter gives it (RuntimeExpressionEvaluator.OmittedArgument):
    // the default the parameter declares, let-coerced to its type, or the default value of its type. One whose default is the calling module's Option
    // Compare, and one whose default raises an error, are the interpreter's to give.
    private ConstantNode? OmittedArgumentOf(VBParameterSymbol parameter, ScalarKind kind)
    {
        if (parameter.TryGetProperty(SymbolProperties.DefaultsToOptionCompare, out var callers) && callers
            || _evaluator.OmittedArgument(_session, parameter) is not { IsSuccess: true, Result: { } omitted }
            || !ScalarPayload.TryOf(parameter.ResolvedType, omitted.RuntimeValue, out var payload) || ScalarKinds.Of(payload.Type) != kind)
        {
            return null;
        }

        return new ConstantNode(kind, payload.Integral, payload.Floating);
    }

    private CompiledStep? RefusedCall(ExpressionNode callee, SourceLocation? location)
    {
        if (_reasons.Count == 0)
        {
            Refuse(IneligibilityKind.Call, callee switch
            {
                SimpleNameExpressionNode name => name.IdentifierName,
                MemberAccessExpressionNode member => member.Member.IdentifierName,
                _ => callee.GetType().Name,
            }, location);
        }

        return null;
    }

    private bool IsOwnFunction(Symbol symbol) => ReferenceEquals(symbol, _procedure) || symbol.Uri.AbsoluteUri == _procedure.Uri.AbsoluteUri && symbol.Name == _procedure.Name;

    // MS-VBAL §5.4.2.3, as ProcedureExecutor.ExecuteForOpener and TryStepForUnboxed do it: a counter of the activation, which counts in its own type.
    private CompiledStep? AnalyzeForOpener(int offset, ForStatementNode forStatement, int past)
    {
        // a Variant counter counts in the type its start, end and step decide (ForLoopEvaluator.Operands): the interpreter runs the statement, and compiled
        // code steps the loop with the state it gives the frame.
        if (forStatement.ControlExpression is SimpleNameExpressionNode variantName && Bind(forStatement.ControlExpression) is BoundVariable variantCounter
            && VariantSlotOf(variantCounter) is { } counterSlot)
        {
            _variantLoops[offset] = counterSlot;
            if (VariantSpeculation.CountingKindOf(SpeculatedKindOf(forStatement.StartExpression), SpeculatedKindOf(forStatement.EndExpression),
                forStatement.StepExpression is { } variantStep ? SpeculatedKindOf(variantStep) : ScalarKind.Integer) is { } counting)
            {
                _variantCountings.Add((counterSlot, counting));
            }

            _stepped.Add(new Ineligibility(IneligibilityKind.ForCounter, variantName.IdentifierName, forStatement.SourceLocation));
            return new SteppedStep(offset);
        }

        if (forStatement.ControlExpression is not SimpleNameExpressionNode || Bind(forStatement.ControlExpression) is not BoundVariable counter
            || SlotOf(counter) is not { } slot)
        {
            Refuse(IneligibilityKind.ForCounter, (forStatement.ControlExpression as SimpleNameExpressionNode)?.IdentifierName ?? string.Empty, forStatement.SourceLocation);
            return null;
        }

        // a loop counts in a local of compiled code: one that counts with a field, held in a cell the interpreter and other procedures see, is stepped.
        var kind = _slots[slot];
        if (kind is ScalarKind.Boolean || _fieldSlots.Contains(slot))
        {
            Refuse(IneligibilityKind.ForCounter, counter.Variable.Name, forStatement.SourceLocation);
            return null;
        }

        // MS-VBAL 5.4.2.3: "If no <step-clause> is present, the <step-increment> value is the integer data value 1", let-coerced to the counter's type.
        _ = PayloadOperations.Convert(ScalarKind.Integer, 1, 0, kind, out var one, out var oneFloating);
        var start = PayloadAs(Bind(forStatement.StartExpression), kind);
        var end = PayloadAs(Bind(forStatement.EndExpression), kind);
        var step = forStatement.StepExpression is { } stepExpression ? PayloadAs(Bind(stepExpression), kind) : new ConstantNode(kind, one, oneFloating);
        if (start is null || end is null || step is null)
        {
            return null;
        }

        var loop = _loops.Count;
        _loops.Add(new CompiledForLoop(offset, counter.Variable, forStatement.ControlExpression, kind));
        _loopOfOpener[offset] = loop;
        _counterSlots.Add(slot);
        return new ForOpenStep(offset, loop, slot, kind, start, end, step, past);
    }

    private readonly List<int> _counterSlots = [];

    // the subtype a bound of a loop of a Variant counter is of, when it is a payload of a known kind (a literal, a typed variable, a constant): looked at
    // only, its refusals not the procedure's.
    private ScalarKind? SpeculatedKindOf(ExpressionNode expression)
    {
        var mark = _reasons.Count;
        var kind = Dynamic(Bind(expression)) is VariantOfNode { Value.Kind: var payload } ? payload : (ScalarKind?)null;
        _reasons.RemoveRange(mark, _reasons.Count - mark);
        return kind;
    }

    private int CounterSlotOf(int loop) => _counterSlots[loop];

    /// <summary>The <c>Select Case</c> statements of a scalar selector, numbered.</summary>
    public IReadOnlyList<CompiledSelect> Selects => _selects;

    private readonly List<CompiledSelect> _selects = [];
    private readonly Dictionary<int, int> _selectOfOpener = [];

    // MS-VBAL §5.4.2.10, as ProcedureExecutor.ExecuteSelect does it on payloads (TryEvaluateSelectorUnboxed): a selector of a scalar type, evaluated once. A
    // selector declared Variant is compared as a Variant, its Case values not converted to its type (RD-VBAL §5.4.2.10): the interpreter's.
    private CompiledStep? AnalyzeSelect(int offset, SelectCaseStatementNode selectCase, int end)
    {
        var bound = Bind(selectCase.ControlExpression);
        if (RuntimeExpressionEvaluator.IsDeclaredVariant(bound))
        {
            Refuse(IneligibilityKind.VariantOperand, "Select Case", selectCase.ControlExpression.Location);
            return null;
        }

        if (Payload(bound) is not { } selector)
        {
            return null;
        }

        _selectOfOpener[offset] = _selects.Count;
        _selects.Add(new CompiledSelect(offset, end, selector.Kind));
        return new SelectStep(offset, _selects.Count - 1, selector);
    }

    // CaseMatchEvaluator.TryMatchUnboxed: each range clause compared with the selector, in order - a To range as both its comparisons - each value converted
    // to the type of the selector, then both to the type the comparison is made in. A conversion that is not exact fails the Case, which the interpreter then
    // matches boxed, as it does when its own payload comparison declines.
    private CompiledStep? AnalyzeCase(int offset, CaseExpressionStatementNode caseBlock, int select, int next)
    {
        var selector = new SelectorNode(select, _selects[select].Kind);
        var clauses = new List<CaseClause>(caseBlock.RangeClauses.Length);
        foreach (var clause in caseBlock.RangeClauses)
        {
            var compared = clause switch
            {
                CaseValueRangeClauseNode value => CaseComparison(Tokens.CompareEqualOp, selector, value.Value) is { } equal ? new CaseClause(equal) : null,
                CaseComparisonRangeClauseNode comparison
                    => CaseComparison(comparison.ComparisonOperator, selector, comparison.Value) is { } compare ? new CaseClause(compare) : null,
                CaseToRangeClauseNode range => CaseComparison(Tokens.CompareGreaterThanOrEqualOp, selector, range.Start) is { } above
                    && CaseComparison(Tokens.CompareLessThanOrEqualOp, selector, range.End) is { } below ? new CaseClause(above, below) : null,
                _ => null,
            };

            if (compared is null)
            {
                if (_reasons.Count == 0)
                {
                    Refuse(IneligibilityKind.Instruction, "Case", clause.SourceLocation);
                }

                return null;
            }

            clauses.Add(compared);
        }

        return new CaseStep(offset, clauses, next);
    }

    // TryCompareUnboxed: selector operator value, on payloads.
    private CompareNode? CaseComparison(string token, SelectorNode selector, ExpressionNode value)
    {
        if (Payload(Bind(value)) is not { } evaluated)
        {
            return null;
        }

        var selectorType = ScalarKinds.TypeOf(selector.Kind);
        var valueType = RuntimeExpressionEvaluator.CaseValueTypeOf(selectorType, ScalarKinds.TypeOf(evaluated.Kind));
        if (ScalarKinds.Of(valueType) is not { } valueKind
            || OperationPlanner.ForValues(token, selectorType, valueType) is not { OperandDestination: { } destinationType }
            || ScalarKinds.Of(destinationType) is not { } destination || OperatorTokens.ComparisonOf(token) is not { } comparison)
        {
            Refuse(IneligibilityKind.Operator, token, value.Location);
            return null;
        }

        return new CompareNode(comparison, Converted(selector, destination), Converted(Converted(evaluated, valueKind), destination));
    }

    // an expression bound as the interpreter binds it where it is written: in a With block, a member with no object before the dot is a member of the
    // block's object, declared as the block's expression is (ProcedureAnalysis.Members.cs).
    private BoundNode Bind(ExpressionNode expression) => _evaluator.BindInProcedure(_session, expression, _procedure, _withExpression);

    // a scalar variable of the activation, by its slot in the procedure's layout; or a scalar field it reaches (ProcedureAnalysis.Fields.cs).
    private int? SlotOf(BoundVariable variable)
        => variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout) && _slots.ContainsKey(slot.Index)
            ? slot.Index
            : FieldSlotOf(variable) is { } field && _slots.ContainsKey(field) ? field : null;

    // a Variant variable of the activation, by its slot.
    private int? VariantSlotOf(BoundVariable variable)
        => variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout) && _variantSlots.Contains(slot.Index) ? slot.Index : null;

    // an object variable of the activation, or an object field it reaches (ProcedureAnalysis.Fields.cs): one declared As New is made when it is read
    // (MS-VBAL §5.2.3.1.4), which is the interpreter's.
    private int? ObjectSlotOf(BoundVariable variable)
        => variable is { IsAutoInstantiated: false, Slot: { } slot } && ReferenceEquals(slot.Layout, _layout) && _objectSlots.Contains(slot.Index)
            ? slot.Index
            : FieldSlotOf(variable) is { } field && _objectSlots.Contains(field) ? field : null;

    // a type whose values are object references: Object, a class or an interface.
    private static bool IsObjectType(VBType? type) => type is VBObjectType or VBClassType;

    // an operand of Is compiled code reads: an object variable of the activation, by its slot, or Nothing, which is no slot. Any other is the interpreter's.
    private bool TryObjectOperand(BoundNode operand, out int? slot)
    {
        slot = null;
        switch (operand)
        {
            case BoundVariable variable when ObjectSlotOf(variable) is { } held:
                slot = held;
                return true;
            case BoundLiteral { Literal.StaticValue: VBObjectValue nothing } when nothing.IsNothing():
                return true;
            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp, Operand: var enclosed }:
                return TryObjectOperand(enclosed, out slot);
            default:
                Unsupported(operand);
                return false;
        }
    }

    // the number the body knows a class TypeOf tests against by.
    private int ClassNumberOf(VBClassModuleSymbol tested)
    {
        var number = _classes.FindIndex(known => known.Uri.AbsoluteUri == tested.Uri.AbsoluteUri);
        if (number < 0)
        {
            number = _classes.Count;
            _classes.Add(tested);
        }

        return number;
    }

    // the condition of an If, an ElseIf, an inline If and a loop, and whether it is an Until (ProcedureExecutor.GetCondition).
    private static (ExpressionNode? Condition, bool Negate) ConditionOf(StatementNode? node) => node switch
    {
        IfBlockStatementNode ifBlock => (ifBlock.ConditionExpression, false),
        ElseIfBlockStatementNode elseIf => (elseIf.ConditionExpression, false),
        InlineIfStatementNode inlineIf => (inlineIf.ConditionExpression, false),
        WhileWendStatementNode whileWend => (whileWend.ConditionExpression, false),
        DoWhileLoopStatementNode doWhile => (doWhile.ConditionExpression, false),
        DoUntilLoopStatementNode doUntil => (doUntil.ConditionExpression, true),
        DoLoopWhileStatementNode doLoopWhile => (doLoopWhile.ConditionExpression, false),
        DoLoopUntilStatementNode doLoopUntil => (doLoopUntil.ConditionExpression, true),
        _ => (null, false),
    };

    // TryEvaluateConditionUnboxed: an extended intermediate converted to a Boolean from its 80 bits, anything else evaluated as it is; its truth is that it
    // is not zero.
    private PayloadNode? Condition(ExpressionNode condition)
    {
        var node = Bind(condition);
        if (IsVariant(node))
        {
            return Dynamic(node) is { } value ? new TruthNode(value) : null;
        }

        return IsExtended(node) ? PayloadAs(node, ScalarKind.Boolean) : Payload(node);
    }

    // whether what node evaluates to is a Variant's value: a Variant of the activation or an operation of one (IsDeclaredVariant), an element of an
    // array of Variants, a Variant field of a record, or what a function of the standard library returns as a Variant.
    private bool IsVariant(BoundNode node)
        => RuntimeExpressionEvaluator.IsDeclaredVariant(node) || IsVariantElement(node) || RecordFieldOf(node) is { Type: VBVariantType }
            || node is BoundCall { Procedure: var called } && IsVariantLibrary(called);

    private static bool IsExtended(BoundNode node) => RuntimeExpressionEvaluator.ExtendedTypeOf(node) is not null;

    private static ScalarKind? ExtendedKindOf(BoundNode node) => ScalarKinds.Of(RuntimeExpressionEvaluator.ExtendedTypeOf(node));

    // TryEvaluatePayloadAs: an extended intermediate converted to the destination from its 80 bits, anything else coerced from its payload.
    private PayloadNode? PayloadAs(BoundNode node, ScalarKind destination)
    {
        // a Variant's payload, of its subtype, converted to the destination as any payload is.
        if (IsVariant(node))
        {
            return Dynamic(node) is { } value ? new UnvariantNode(value, destination) : null;
        }

        if (IsExtended(node))
        {
            return Extended(node) is { } extended ? new StoreNode(extended, destination) : null;
        }

        return Payload(node) is { } payload ? Converted(payload, destination) : null;
    }

    // an exact conversion, made once when it is of a constant: one that declines then declines every time, which the interpreter is handed.
    private static PayloadNode Converted(PayloadNode payload, ScalarKind destination)
    {
        if (payload.Kind == destination)
        {
            return payload;
        }

        if (payload is ConstantNode constant
            && PayloadOperations.Convert(constant.Kind, constant.Integral, constant.Floating, destination, out var integral, out var floating) is PayloadStatus.Ok)
        {
            return new ConstantNode(destination, integral, floating);
        }

        return new ConvertNode(payload, destination);
    }

    // TryEvaluatePayload, case for case.
    private PayloadNode? Payload(BoundNode node)
    {
        switch (node)
        {
            // an element of an array of objects, an object field of a record, or what a call returns, compared, and tested.
            case BoundBinary { Operation.Token: Tokens.CompareIsOp } elementCompared when IsHeldObject(elementCompared.Left) || IsHeldObject(elementCompared.Right)
                || IsReturnedObject(elementCompared.Left) || IsReturnedObject(elementCompared.Right):
                return ElementIs(elementCompared);

            case BoundTypeOf { Operand: BoundIndex or BoundMember } elementTypeOf when IsObjectElement(elementTypeOf.Operand):
                return ElementTypeOf(elementTypeOf, ElementOf(elementTypeOf.Operand)!);

            case BoundTypeOf { Operand: BoundMember tested } fieldTypeOf when ObjectFieldOf(tested) is { } field:
                return new ElementTypeOfNode(new ObjectFieldNode(field), ClassNumberOf(fieldTypeOf.Class));

            // MS-VBAL §5.6.9.7: two object references compared as they are.
            case BoundBinary { Operation.Token: Tokens.CompareIsOp } compared:
                return TryObjectOperand(compared.Left, out var leftObject) && TryObjectOperand(compared.Right, out var rightObject)
                    ? new IsNode(leftObject, rightObject)
                    : null;

            // MS-VBAL §5.6.7: what TypeOf tests is the interpreter's own test (RuntimeExpressionEvaluator.IsInstanceOf).
            case BoundTypeOf { Operand: BoundVariable tested } typeOf when ObjectSlotOf(tested) is { } typed:
                return new TypeOfNode(typed, ClassNumberOf(typeOf.Class));

            // of a Variant, the object it holds; one that holds no object is no instance of any class.
            case BoundTypeOf { Operand: BoundVariable tested } variantTypeOf when VariantSlotOf(tested) is { } held:
                return new ElementTypeOfNode(new VariantObjectOperand(held), ClassNumberOf(variantTypeOf.Class));

            // MS-VBAL §5.6.9.5: two Strings compared, as TryEvaluatePayload compares them (TryCompareText).
            case BoundBinary { Plan: { IsDeferred: false, EffectiveType: VBStringType } } compared:
                return CompareText(compared);

            case BoundBinary or BoundUnary or BoundCall when IsExtended(node):
                return Extended(node) is { } extended ? new StoreNode(extended, ExtendedKindOf(node)!.Value) : null;

            case BoundBinary binary when IsExtended(binary.Left) || IsExtended(binary.Right):
                return PlanOf(binary) is { } extendedPlan ? OperateWithExtended(binary, extendedPlan) : null;

            case BoundUnary { Plan: { IsDeferred: false, OperandDestination: { } destination } plan } unary when IsExtended(unary.Operand):
                return ScalarKinds.Of(destination) is { } to && PayloadAs(unary.Operand, to) is { } converted ? OperateUnary(plan, converted, unary) : Unsupported(unary);

            case BoundLiteral literal:
                return ScalarKinds.Of(literal.Literal.StaticValue.TypeInfo) is { } literalKind
                    && ScalarPayload.TryOf(literal.Literal.StaticValue.TypeInfo, literal.Literal.StaticValue.RuntimeValue, out var payload)
                        ? new ConstantNode(literalKind, payload.Integral, payload.Floating)
                        : Unsupported(literal);

            case BoundVariable variable when VariantSlotOf(variable) is not null:
                Refuse(IneligibilityKind.VariantOperand, variable.Variable.Name, variable.Expression.Location);
                return null;

            case BoundVariable variable:
                return SlotOf(variable) is { } slot ? Read(new SlotNode(slot, _slots[slot]), variable) : Unsupported(variable);

            case BoundName { Symbol: var symbol } when RuntimeExpressionEvaluator.IsConstant(symbol):
                return Constant(node, symbol);

            case BoundQualifiedName { Member: var member } when RuntimeExpressionEvaluator.IsConstant(member):
                return Constant(node, member);

            // MS-VBAL §5.3.1: the name of a function in its own body is its function result variable, which compiled code keeps in a local of its own.
            case BoundName { Symbol: var function } when IsOwnFunction(function) && ResultKind is { } result:
                return Read(new ResultNode(result), node);

            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return Payload(enclosed);

            case BoundUnary { Plan: { IsDeferred: false } plan, Operand: var operand } unary when plan.Operator is Tokens.NegationOp or Tokens.LogicalNotOp:
                return Payload(operand) is { } value ? OperateUnary(plan, value, unary) : null;

            case BoundBinary binary:
                return PlanOf(binary) is { } binaryPlan && Payload(binary.Left) is { } left && Payload(binary.Right) is { } right
                    ? Operate(binaryPlan, left, right, binary)
                    : null;

            // an element of an array field of a record of the activation.
            case BoundMember member when FieldElementOf(member) is { } fieldElement:
                return Element(fieldElement) is { } fieldElementRead ? Read(fieldElementRead, member) : null;

            // a field of a record of the activation.
            case BoundMember member when RecordFieldOf(member) is { } field:
                return FieldPayload(member, field) is { } fieldRead ? Read(fieldRead, member) : null;

            // a public variable of an object, and Err.Number (ProcedureAnalysis.MemberReads.cs).
            case BoundMember member when (MemberFieldOf(member) ?? ErrNumberOf(member)) is { } read:
                return Read(read, member);

            // an element of an array, and its bounds.
            case BoundIndex index:
                return Element(index) is { } elementRead ? Read(elementRead, index) : null;

            case BoundArrayBound arrayBound:
                return ArrayBound(arrayBound);

            // CByte, CInt, CLng, CLngLng, CCur or CBool of an extended intermediate, which converts it from its 80 bits, as the interpreter does
            // (EvaluateWideConversion): never through the function's binding, which would be given it stored.
            case BoundCall { BoundArguments: [{ Value: { } argument, Elements.IsDefaultOrEmpty: true }] } converting
                when IsExtended(argument) && IsCompiledLibrary(converting.Procedure)
                    && ExtendedIntermediates.ConversionOf(converting.Procedure.GetProperty(SymbolProperties.ExternalTarget)!) is { } conversion
                    && ScalarKinds.Of(conversion) is { } convertedTo:
                return PayloadAs(argument, convertedTo);

            // a function of the standard library, called with its arguments or by its name, qualified or not (ProcedureAnalysis.Libraries.cs).
            case BoundCall { Procedure: VBTypeMemberSymbol library } call when IsCompiledLibrary(library):
                return LibraryCallOf(call, library, call.BoundArguments, call.ResultType);

            case BoundQualifiedName { Member: VBTypeMemberSymbol library } qualified when IsCompiledLibrary(library):
                return LibraryCallOf(qualified, library, ImmutableArray<BoundArgument>.Empty, resultType: null);

            case BoundName { Symbol: VBTypeMemberSymbol library } name when IsCompiledLibrary(library):
                return LibraryCallOf(name, library, ImmutableArray<BoundArgument>.Empty, resultType: null);

            // a function called inside an expression (ProcedureAnalysis.Nested.cs).
            case BoundCall { Procedure: VBTypeMemberSymbol function } call when _nesting is not null:
                return NestedCall(call, function);

            case BoundMember { Member: (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and var function } member when _nesting is not null:
                return NestedCall(member, function);

            case BoundName { Symbol: (VBFunctionMemberSymbol or VBPropertyGetMemberSymbol) and VBTypeMemberSymbol function } name when _nesting is not null && !IsOwnFunction(function):
                return NestedCall(name, function);

            default:
                return Unsupported(node);
        }
    }

    // TryEvaluatePayload of what a Variant is: a Variant variable of the activation; an operation of a Variant operand, whose plan is
    // deferred - each operand evaluated on payloads, then planned for their types (PlanFor); and anything else evaluated on payloads, of its own kind.
    private VariantNode? Dynamic(BoundNode node)
    {
        switch (node)
        {
            case BoundVariable variable when VariantSlotOf(variable) is { } slot:
                return new VariantSlotNode(slot);

            // MS-VBAL §5.3.1: the name of a function in its own body is its function result variable, a Variant its frame holds.
            case BoundName { Symbol: var function } when ResultIsVariant && IsOwnFunction(function):
                return new VariantResultNode();

            case BoundIndex or BoundMember when IsVariantElement(node):
                return VariantElement(ElementOf(node)!);

            case BoundUnary { Operation.Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp, Operand: var enclosed }:
                return Dynamic(enclosed);

            // MS-VBAL §5.6.9.7: Is compares two object references, whose plan is deferred to the objects, for a Boolean: an operand of a Variant operation as
            // a Boolean is.
            case BoundBinary { Operation.Token: Tokens.CompareIsOp }:
                return Payload(node) is { } compared ? new VariantOfNode(compared) : null;

            case BoundBinary { Plan: { IsDeferred: true } plan } binary:
                if (!IsPayloadOperator(plan.Operator))
                {
                    Refuse(IneligibilityKind.Operator, plan.Operator, binary.Expression.Location);
                    return null;
                }

                return Dynamic(binary.Left) is { } left && Dynamic(binary.Right) is { } right ? new VariantBinaryNode(plan.Operator, left, right) : null;

            case BoundUnary { Plan: { IsDeferred: true } plan, Operand: var operand } unary when plan.Operator is Tokens.NegationOp or Tokens.LogicalNotOp:
                return Dynamic(operand) is { } value ? new VariantUnaryNode(plan.Operator, value, unary.NegatesAConstantExpression) : null;

            case BoundMember when RecordFieldOf(node) is { Type: VBVariantType } field:
                return new VariantFieldNode(field.Path);

            // a function of the standard library that returns a Variant (ProcedureAnalysis.Libraries.cs).
            case BoundCall { Procedure: VBTypeMemberSymbol library } call when IsVariantLibrary(library):
                return LibraryVariantOf(call, library);

            default:
                return Payload(node) is { } payload ? new VariantOfNode(payload) : null;
        }
    }

    // the binary operators TryOperateBinary does on payloads: the arithmetic ones but ^, the comparisons and the logical operators.
    private static bool IsPayloadOperator(string token)
        => token is Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp or Tokens.DivisionOp or Tokens.IntegerDivisionOp or Tokens.ModuloOp
            || OperatorTokens.ComparisonOf(token) is not null || OperatorTokens.LogicalOf(token) is not null;

    private PayloadNode? Constant(BoundNode node, Symbol constant)
    {
        if (!_evaluator.TryFoldConstant(_session, node, constant, out var payload) || ScalarKinds.Of(payload.Type) is not { } kind)
        {
            return Unsupported(node);
        }

        return new ConstantNode(kind, payload.Integral, payload.Floating);
    }

    // the plan of an operation of declared types: a plan deferred to the values - of a Variant, an object - is the boxed evaluation's.
    private OperationPlan? PlanOf(BoundBinary binary)
    {
        if (binary.Plan is { IsDeferred: false } plan)
        {
            return plan;
        }

        Refuse(binary.Plan is null ? IneligibilityKind.LateBound : IneligibilityKind.VariantOperand, binary.Operation.Token, binary.Expression.Location);
        return null;
    }

    private PayloadNode? Unsupported(BoundNode node)
    {
        var (kind, detail) = node switch
        {
            BoundCall call => (IneligibilityKind.Call, call.Procedure.Name),
            BoundMember member => (IneligibilityKind.Member, member.Access.Member.IdentifierName),
            BoundIndex => (IneligibilityKind.Element, string.Empty),
            BoundLate or BoundError => (IneligibilityKind.LateBound, (node.Expression as SimpleNameExpressionNode)?.IdentifierName ?? string.Empty),
            BoundNew made => (IneligibilityKind.New, made.Class.Name),
            BoundLiteral literal => (IneligibilityKind.Literal, literal.Literal.StaticValue.TypeInfo.Name),
            BoundVariable variable => (IneligibilityKind.ForeignVariable, variable.Variable.Name),
            BoundBinary { Plan: { } plan } => (IneligibilityKind.Operator, plan.Operator),
            BoundUnary { Plan: { } plan } => (IneligibilityKind.Operator, plan.Operator),
            _ => (IneligibilityKind.LateBound, node.GetType().Name),
        };

        Refuse(kind, detail, node.Expression.Location);
        return null;
    }

    // TryOperateWithExtended: a comparison of a floating-point effective type compares the extended values at its precision; any other operation is given
    // each operand let-coerced to the type it coerces them to.
    private PayloadNode? OperateWithExtended(BoundBinary binary, OperationPlan plan)
    {
        if (ExtendedIntermediates.IsComparison(plan.Operator) && ExtendedIntermediates.IsFloating(plan.EffectiveType))
        {
            return ExtendedOperand(binary.Left) is { } left && ExtendedOperand(binary.Right) is { } right
                ? new ExtendedCompareNode(OperatorTokens.ComparisonOf(plan.Operator)!.Value, left, right, ScalarKinds.Of(plan.EffectiveType)!.Value)
                : null;
        }

        if (ScalarKinds.Of(plan.OperandDestination) is not { } destination)
        {
            return Unsupported(binary);
        }

        return PayloadAs(binary.Left, destination) is { } l && PayloadAs(binary.Right, destination) is { } r ? Operate(plan, l, r, binary) : null;
    }

    // TryOperateBinary: each operand converted to the type the plan coerces them to, then the operation of the effective type - an integral operation, a
    // comparison or a logical one.
    private PayloadNode? Operate(OperationPlan plan, PayloadNode left, PayloadNode right, BoundBinary binary)
    {
        if (ScalarKinds.Of(plan.OperandDestination) is not { } destination || ScalarKinds.Of(plan.EffectiveType) is not { } effective)
        {
            return Unsupported(binary);
        }

        var (l, r) = (Converted(left, destination), Converted(right, destination));
        if (effective is not ScalarKind.Boolean && ScalarKinds.IsIntegral(effective)
            && plan.Operator is Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp or Tokens.IntegerDivisionOp or Tokens.ModuloOp)
        {
            return new IntegralNode(plan.Operator, l, r, effective);
        }

        if (OperatorTokens.ComparisonOf(plan.Operator) is { } comparison)
        {
            return new CompareNode(comparison, l, r);
        }

        if (OperatorTokens.LogicalOf(plan.Operator) is { } logical && ScalarKinds.IsIntegral(destination))
        {
            return new LogicalNode(logical, l, r, effective);
        }

        return Unsupported(binary);
    }

    // TryOperateUnary: the operand converted to the effective type, then negated or inverted.
    private PayloadNode? OperateUnary(OperationPlan plan, PayloadNode operand, BoundUnary unary)
    {
        if (ScalarKinds.Of(plan.EffectiveType) is not { } effective)
        {
            return Unsupported(unary);
        }

        var converted = Converted(operand, effective);
        return plan.Operator == Tokens.NegationOp
            ? new NegateNode(converted, effective, unary.NegatesAConstantExpression)
            : ScalarKinds.IsIntegral(effective) ? new NotNode(converted, effective) : Unsupported(unary);
    }

    // EvaluateWide: an operation or a negation of extended operands, in parentheses or not; or Abs, Int, Fix, CDbl or CSng of an extended intermediate,
    // computed on its 80 bits (ExtendedIntermediates.Apply), never through the function's binding.
    private ExtendedNode? Extended(BoundNode node)
    {
        switch (node)
        {
            case BoundBinary { Plan: { } plan } binary:
                return ExtendedOperand(binary.Left) is { } left && ExtendedOperand(binary.Right) is { } right ? new ExtendedOperationNode(plan.Operator, left, right) : null;
            case BoundUnary { Plan: not null } unary:
                return ExtendedOperand(unary.Operand) is { } operand ? new ExtendedNegateNode(operand) : null;
            case BoundUnary { Plan: null } unary:
                return Extended(unary.Operand);
            case BoundCall { BoundArguments: [{ Value: { } argument }] } call
                when call.Procedure.GetProperty(SymbolProperties.ExternalTarget) is { } function && ExtendedIntermediates.Applies(function):
                return Extended(argument) is { } applied ? new ExtendedApplyNode(function, applied) : null;
            default:
                Unsupported(node);
                return null;
        }
    }

    // TryEvaluateWideOperand: an extended intermediate, or a payload widened.
    private ExtendedNode? ExtendedOperand(BoundNode operand)
        => IsExtended(operand) ? Extended(operand) : Payload(operand) is { } payload ? new WidenNode(payload) : null;
}

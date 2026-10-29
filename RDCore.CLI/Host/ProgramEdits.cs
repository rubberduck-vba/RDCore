using System.Collections.Immutable;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using RDCore.Symbols;

namespace RDCore.CLI.Host;

/// <summary>
/// What an edit made while a program waits is to it: applied when it is resumed, not applied to a program that waits yet, or not applicable at all.
/// </summary>
internal enum ProgramEditKind
{
    /// <summary>Applied: the program reads it afresh where it next uses it.</summary>
    Applies,

    /// <summary>Applying it is correct, but not done to a program that waits yet: the program is not resumed on it.</summary>
    NotYet,

    /// <summary>Applying it can't be made correct: the program is not resumed on it, and picks it up when it is run again.</summary>
    Restart,

    /// <summary>The code no longer compiles: there is no program to resume on.</summary>
    DoesNotCompile,
}

/// <summary>An edit made while a program waits: what was edited, and what it is to the program.</summary>
/// <param name="Name">What was edited, as the user names it.</param>
/// <param name="Kind">What it is to the program.</param>
/// <param name="Reason">Why, in the user's words.</param>
/// <param name="Module">The module it is in, as the host addresses it.</param>
/// <param name="Range">Where the edited declaration is now; none for the module as a whole, or a declaration that is gone.</param>
internal sealed record class ProgramEdit(string Name, ProgramEditKind Kind, string Reason, Uri? Module = null, SourceRange? Range = null);

/// <summary>
/// The code and the declarations a program was suspended with, and what was edited since: each procedure's lowered body, each module's declarations as the
/// host defined them, the procedures the program is in.
/// </summary>
internal sealed class ProgramEdits
{
    private readonly ImmutableDictionary<string, string> _bodies;
    private readonly ImmutableDictionary<string, Declaration> _declarations;
    private readonly ImmutableDictionary<string, string> _procedures;
    private readonly ImmutableDictionary<string, ModuleText> _modules;
    private readonly ImmutableHashSet<string> _active;

    // how many variables had had their value reset by a definition when the program was suspended: any reset after is a value the program would go on
    // without.
    private int _reset;

    // a declaration of a module, by what it is and where: its whole text, its signature less its locals, its locals, and whether its module is a class.
    private sealed record class Declaration(string Name, SymbolDescriptorKind Kind, SymbolDescriptor Descriptor, string Whole, string Signature,
        ImmutableArray<LocalDescriptor> Locals, bool InClass, Uri Module, SourceRange Range);

    // what a module says of itself, apart from its members: its options, and what a class implements and raises.
    private sealed record class ModuleText(string Options, string? Interface, int Base);

    private ProgramEdits(ImmutableDictionary<string, string> bodies, ImmutableDictionary<string, Declaration> declarations,
        ImmutableDictionary<string, string> procedures, ImmutableDictionary<string, ModuleText> modules, ImmutableHashSet<string> active)
        => (_bodies, _declarations, _procedures, _modules, _active) = (bodies, declarations, procedures, modules, active);

    /// <summary>What the program of <paramref name="session"/> is, as it waits.</summary>
    public static ProgramEdits Of(IRuntimeSession session, ProgramImage image)
    {
        var declarations = ImmutableDictionary.CreateBuilder<string, Declaration>();
        var procedures = ImmutableDictionary.CreateBuilder<string, string>();
        var modules = ImmutableDictionary.CreateBuilder<string, ModuleText>();
        foreach (var module in session.Symbols.Scopes.Global.Declarations.OfType<VBModuleSymbol>().Where(module => module.GetProperty(SymbolProperties.Library) is null))
        {
            var inClass = module is VBClassModuleSymbol;
            modules[module.Uri.AbsoluteUri] = TextOf(module);
            var declared = session.Symbols.DeclaredIn(module.Uri).ToList();
            foreach (var descriptor in SymbolDescriptorProjector.Project(declared, module.Uri))
            {
                declarations[KeyOf(module.Uri, descriptor)] = new Declaration(
                    $"{module.Name}.{descriptor.Name}", descriptor.Kind, descriptor, Text(descriptor), Text(descriptor with { Locals = [] }), descriptor.Locals,
                    inClass, module.Uri, descriptor.Range);
            }

            // each procedure's body, by the identity its fingerprint is keyed by, to its declaration: a Property Get, Let and Set are one name and three bodies.
            foreach (var procedure in declared.OfType<VBTypeMemberSymbol>())
            {
                if (SymbolDescriptorProjector.Project([procedure], module.Uri).FirstOrDefault() is { } descriptor && IsProcedure(descriptor.Kind))
                {
                    procedures[procedure.SemanticId.ToString()] = KeyOf(module.Uri, descriptor);
                }
            }
        }

        // the procedures with an activation on the stack, by the identity of their bodies: what the program is in the middle of.
        var active = ImmutableHashSet.CreateBuilder<string>();
        foreach (var frame in session.CallStack.Frames.OfType<CallStackFrame>())
        {
            if (frame.Procedure is { } procedure)
            {
                _ = active.Add(procedure.SemanticId.ToString());
            }
        }

        return new ProgramEdits(image.ProcedureFingerprints(), declarations.ToImmutable(), procedures.ToImmutable(), modules.ToImmutable(), active.ToImmutable())
        {
            _reset = session.Symbols.ValuesReset.Count,
        };
    }

    /// <summary>
    /// What was edited since the program was suspended with what <paramref name="suspended"/> says, and what each edit is to it. The program is resumed only
    /// when every one of them <see cref="ProgramEditKind.Applies"/>.
    /// </summary>
    public static IReadOnlyList<ProgramEdit> Since(ProgramEdits suspended, IRuntimeSession session, ProgramImage image)
    {
        var now = Of(session, image);
        var edits = new List<ProgramEdit>();
        var frames = session.CallStack.Frames.OfType<CallStackFrame>().Where(frame => frame.Procedure is not null).ToList();

        // the declarations of the procedures the program is in, and the locals a statement typed at the break declared in them: the frame holds those already.
        var activeDeclarations = suspended._active.Select(id => suspended._procedures.GetValueOrDefault(id)).OfType<string>().ToHashSet();
        var declaredAtAStop = new Dictionary<string, HashSet<string>>();
        foreach (var frame in frames)
        {
            if (suspended._procedures.GetValueOrDefault(frame.Procedure!.SemanticId.ToString()) is { } key)
            {
                var names = declaredAtAStop.TryGetValue(key, out var known) ? known : declaredAtAStop[key] = new(StringComparer.OrdinalIgnoreCase);
                names.UnionWith(frame.DeclaredAtAStop.Select(local => local.Name));
            }
        }

        // the names the code of each frame reads: a constant folded into it keeps the value it had when the activation began. A name its procedure declares
        // for itself - a parameter, a local - is not the module's.
        var framesRead = frames.Select(frame => ReadBy(frame, suspended)).ToList();

        foreach (var key in suspended._declarations.Keys.Union(now._declarations.Keys))
        {
            var before = suspended._declarations.GetValueOrDefault(key);
            var after = now._declarations.GetValueOrDefault(key);
            if (before?.Whole == after?.Whole)
            {
                continue;
            }

            edits.Add(Classify(before, after, activeDeclarations.Contains(key), declaredAtAStop.GetValueOrDefault(key) ?? [], suspended, now, activeDeclarations, framesRead));
        }

        // a procedure the program is in, whose code was edited or whose lines moved: each of its activations goes on in the edited body from where it is,
        // unless where it is was edited.
        foreach (var (frame, body, alignment, waits) in Rebasings(session, image))
        {
            if (frame.WhyNotRebased(body, alignment, waits) is not { } refusal)
            {
                continue;
            }

            var declared = now._procedures.GetValueOrDefault(frame.Procedure!.SemanticId.ToString()) is { } key ? now._declarations.GetValueOrDefault(key) : null;
            var name = declared?.Name ?? NameOfProcedure(frame.Procedure.SemanticId.ToString());
            if (!edits.Any(edit => edit.Name == name && edit.Kind is not ProgramEditKind.Applies))
            {
                var (kind, reason) = refusal switch
                {
                    FrameRebaseRefusal.CallChanged => (ProgramEditKind.Restart, Resources.Host_Edit_CallOfAnActiveProcedure),
                    FrameRebaseRefusal.InTheMiddleOfAHeader => (ProgramEditKind.NotYet, Resources.Host_Edit_HeaderOfAnActiveProcedure),
                    FrameRebaseRefusal.BlockChanged => (ProgramEditKind.Restart, Resources.Host_Edit_BlockOfAnActiveProcedure),
                    FrameRebaseRefusal.FaultChanged => (ProgramEditKind.Restart, Resources.Host_Edit_FaultOfAnActiveProcedure),
                    FrameRebaseRefusal.RunsCompiled => (ProgramEditKind.NotYet, Resources.Host_Edit_ActiveProcedureRunsCompiled),
                    FrameRebaseRefusal.Uncertain => (ProgramEditKind.NotYet, alignment.IsPartial
                        ? Resources.Host_Edit_TooLargeToPlaceInAnActiveProcedure
                        : Resources.Host_Edit_UncertainInAnActiveProcedure),
                    _ => (ProgramEditKind.Restart, Resources.Host_Edit_HandlerOfAnActiveProcedure),
                };
                edits.Add(new ProgramEdit(name, kind, reason, declared?.Module, declared?.Range));
            }
        }

        // a module's options are read by its code as it is lowered: afresh at the next call, but not by a procedure the program is in. What a class implements
        // or raises is what its objects are dispatched through.
        foreach (var (module, text) in suspended._modules)
        {
            if (!now._modules.TryGetValue(module, out var current) || current == text)
            {
                continue;
            }

            if (current.Interface != text.Interface)
            {
                edits.Add(new ProgramEdit(NameOf(module), ProgramEditKind.Restart, Resources.Host_Edit_ClassInterface, new Uri(module)));
            }
            else
            {
                // an array the module holds that was declared with no lower bound has the one the Option Base it was declared under gave it.
                var inIt = activeDeclarations.Any(key => key.StartsWith(module + "|", StringComparison.Ordinal));
                var (kind, reason) = inIt ? (ProgramEditKind.NotYet, Resources.Host_Edit_OptionOfAnActiveModule)
                    : current.Base != text.Base && HoldsArraysOfItsBase(suspended, module) ? (ProgramEditKind.Restart, Resources.Host_Edit_OptionBaseOfStoredArrays)
                    : (ProgramEditKind.Applies, Resources.Host_Edit_Applies);
                edits.Add(new ProgramEdit(NameOf(module), kind, reason, new Uri(module)));
            }
        }

        // a body changed, or gone, under the same declaration: read at its next call. One the program is in that is still loaded is its activations' to go
        // on in, above; one that is gone has no code for them to go on in.
        foreach (var (procedure, fingerprint) in suspended._bodies)
        {
            var inIt = suspended._active.Contains(procedure);
            var gone = !now._bodies.TryGetValue(procedure, out var current);
            var declared = now._procedures.GetValueOrDefault(procedure) is { } key ? now._declarations.GetValueOrDefault(key) : null;
            var name = declared?.Name ?? NameOfProcedure(procedure);
            if ((gone ? inIt : !inIt && current != fingerprint) && !edits.Any(edit => edit.Name == name && edit.Kind is not ProgramEditKind.Applies))
            {
                edits.Add(gone
                    ? new ProgramEdit(name, ProgramEditKind.Restart, Resources.Host_Edit_SignatureOfAnActiveProcedure, declared?.Module, declared?.Range)
                    : new ProgramEdit(name, ProgramEditKind.Applies, Resources.Host_Edit_Applies, declared?.Module, declared?.Range));
            }
        }

        // a variable whose value a definition reset since the program was suspended: the edit that did it may be taken back, and the value is still gone.
        foreach (var reset in session.Symbols.ValuesReset.Skip(suspended._reset))
        {
            var name = $"{NameOf(reset.ParentUri.AbsoluteUri)}.{reset.Name}";
            if (!edits.Any(edit => edit.Name == name && edit.Kind is not ProgramEditKind.Applies))
            {
                edits.Add(new ProgramEdit(name, ProgramEditKind.Restart, Resources.Host_Edit_ValueReset, reset.ParentUri));
            }
        }

        // the code a module was last loaded with did not compile: its old bodies are still loaded, and resuming would run them.
        foreach (var module in now._modules.Keys)
        {
            if (image.IsLoaded(new Uri(module)) && image.Semantics.TryGet(new Uri(module), out var model) && !model.IsValid)
            {
                var first = model.CompileErrors.FirstOrDefault();
                edits.Add(new ProgramEdit(NameOf(module), ProgramEditKind.DoesNotCompile, string.Format(Resources.Host_Edit_DoesNotCompile, first?.Description ?? string.Empty),
                    new Uri(module), first?.Location.Range));
            }
        }

        // in the order they are in the code, whatever order the declarations were found in.
        return [.. edits.OrderBy(edit => edit.Module?.AbsoluteUri, StringComparer.Ordinal).ThenBy(edit => edit.Range?.Start.Line ?? -1)
            .ThenBy(edit => edit.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Moves each activation of a procedure whose body was edited, or loaded again, into its body as it now is: what resuming the program, stepping it or
    /// moving its point of execution does first, once nothing edited is refused (<see cref="Since"/>).
    /// </summary>
    public static void Rebase(IRuntimeSession session, ProgramImage image)
    {
        foreach (var (frame, body, alignment, waits) in Rebasings(session, image).ToList())
        {
            _ = frame.TryRebase(body, alignment, waits);
        }
    }

    // the activations whose procedure's body is not the one they run - edited, or its module loaded again - each with that body, how the two align, and
    // whether it is the activation the program waits in.
    private static IEnumerable<(CallStackFrame Frame, InstructionList Body, InstructionAlignment Alignment, bool Waits)> Rebasings(IRuntimeSession session, ProgramImage image)
    {
        var current = session.CallStack.Current;
        foreach (var frame in session.CallStack.Frames.OfType<CallStackFrame>())
        {
            if (frame is { Procedure: { } procedure, Body: { } body } && image.TryGetValue(procedure.SemanticId, out var now) && !ReferenceEquals(now, body))
            {
                yield return (frame, now, AlignmentOf(body, now), ReferenceEquals(frame, current));
            }
        }
    }

    // a body and its edited body are aligned once, however often what was edited is asked: by each edit status, and again by the resume.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<InstructionList, Aligned> Alignments = new();

    private sealed record class Aligned(InstructionList Edited, InstructionAlignment Alignment);

    private static InstructionAlignment AlignmentOf(InstructionList body, InstructionList edited)
    {
        if (Alignments.TryGetValue(body, out var aligned) && ReferenceEquals(aligned.Edited, edited))
        {
            return aligned.Alignment;
        }

        var alignment = InstructionAlignment.Of(body, edited);
        Alignments.AddOrUpdate(body, new Aligned(edited, alignment));
        return alignment;
    }

    // whether the module holds a fixed-size array a dimension of which was declared with no lower bound: a module variable, or a Static of its procedures.
    // A field of a Type is not one: Option Base does not apply to it.
    private static bool HoldsArraysOfItsBase(ProgramEdits suspended, string module)
    {
        static bool Unbased(ArrayDescriptor? array) => array is { IsFixedSize: true } && array.Bounds.Any(bound => bound.Lower is null);

        return suspended._declarations.Values.Where(declaration => declaration.Module.AbsoluteUri == module).Any(declaration
            => (declaration.Kind == SymbolDescriptorKind.ModuleField && Unbased(declaration.Descriptor.Array))
            || declaration.Locals.Any(local => local.IsStatic && Unbased(local.Array)));
    }

    private static IReadOnlySet<string> ReadBy(CallStackFrame frame, ProgramEdits suspended)
    {
        var read = new HashSet<string>(frame.Body is { } body ? body.Items.Select(item => item.Node).OfType<SyntaxNode>().SelectMany(NamesIn) : [], StringComparer.OrdinalIgnoreCase);
        if (suspended._procedures.GetValueOrDefault(frame.Procedure!.SemanticId.ToString()) is { } key && suspended._declarations.GetValueOrDefault(key) is { } declared)
        {
            read.ExceptWith(declared.Descriptor.Parameters.Select(parameter => parameter.Name));
            read.ExceptWith(declared.Locals.Select(local => local.Name));
        }

        return read;
    }

    // what an edited declaration is to the program.
    private static ProgramEdit Classify(Declaration? before, Declaration? after, bool active, IReadOnlySet<string> declaredAtAStop, ProgramEdits suspended,
        ProgramEdits now, IReadOnlySet<string> activeDeclarations, IReadOnlyList<IReadOnlySet<string>> framesRead)
    {
        var name = (after ?? before)!.Name;
        var kind = (after ?? before)!.Kind;
        ProgramEdit Edit(ProgramEditKind editKind, string reason) => new(name, editKind, reason, (after ?? before)!.Module, after?.Range);

        // added: nothing running holds it, unless it is a field of a class, which an object the program holds has no cell for yet.
        if (before is null)
        {
            return kind == SymbolDescriptorKind.ModuleField && after!.InClass
                ? Edit(ProgramEditKind.NotYet, Resources.Host_Edit_FieldOfAClass)
                : Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }

        // removed: its storage goes, and code that still names it does not compile. A procedure the program is in can't go, nor a field of an object it holds.
        if (after is null)
        {
            return active ? Edit(ProgramEditKind.Restart, Resources.Host_Edit_SignatureOfAnActiveProcedure)
                : kind == SymbolDescriptorKind.ModuleField && before.InClass ? Edit(ProgramEditKind.Restart, Resources.Host_Edit_StoredValue)
                : Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }

        if (IsProcedure(kind))
        {
            // what the frame's arguments and result are laid out by.
            if (active && before.Signature != after.Signature)
            {
                return Edit(ProgramEditKind.Restart, Resources.Host_Edit_SignatureOfAnActiveProcedure);
            }

            // a Static keeps its storage, of the type it had; a local of the frame keeps its value, of the type it had.
            foreach (var local in before.Locals)
            {
                var edited = after.Locals.FirstOrDefault(other => string.Equals(other.Name, local.Name, StringComparison.OrdinalIgnoreCase));
                var same = edited is not null && Text(edited) == Text(local);
                if (local.IsStatic && !same)
                {
                    return Edit(ProgramEditKind.Restart, Resources.Host_Edit_StoredValue);
                }

                if (active && edited is not null && !same)
                {
                    return Edit(ProgramEditKind.Restart, Resources.Host_Edit_LocalOfAnActiveProcedure);
                }
            }

            // a constant of the procedure, revalued: its frame's code was lowered with the value folded in.
            if (active && !before.Descriptor.Constants.Select(Text).SequenceEqual(after.Descriptor.Constants.Select(Text)))
            {
                return Edit(ProgramEditKind.NotYet, Resources.Host_Edit_ValueAnActiveProcedureRead);
            }

            // a local added to or removed from the frame changes its layout, which is not done to a program that waits yet: a local the frame already holds -
            // declared by a statement typed at the break - is no change of it. Its code is the bodies' to say, and its signature is as it was.
            var added = after.Locals.Where(local => !before.Locals.Any(other => string.Equals(other.Name, local.Name, StringComparison.OrdinalIgnoreCase)));
            var relaidOut = after.Locals.Length < before.Locals.Length || added.Any(local => !declaredAtAStop.Contains(local.Name));
            return active && relaidOut
                ? Edit(ProgramEditKind.NotYet, Resources.Host_Edit_LocalsOfAnActiveProcedure)
                : Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }

        switch (kind)
        {
            // a variable holds a value, of the type it was declared with.
            case SymbolDescriptorKind.ModuleField:
                return Edit(ProgramEditKind.Restart, Resources.Host_Edit_StoredValue);

            // the values of a Type the program holds have the fields they were made with: a field added has no cell in them yet, and one removed or retyped
            // would have to be converted under the program.
            case SymbolDescriptorKind.UserDefinedType:
                var kept = before.Descriptor.Members.All(field => after.Descriptor.Members.Any(other => Text(other) == Text(field)));
                return kept ? Edit(ProgramEditKind.NotYet, Resources.Host_Edit_FieldOfAType) : Edit(ProgramEditKind.Restart, Resources.Host_Edit_StoredValue);

            case SymbolDescriptorKind.Event:
                return Edit(ProgramEditKind.Restart, Resources.Host_Edit_ClassInterface);

            // a value revalued is read afresh where it is next used - except where it sizes what the program holds, and where a frame already read it.
            case SymbolDescriptorKind.ModuleConstant or SymbolDescriptorKind.Enum:
                return Revalued(before, after, Edit, suspended, activeDeclarations, framesRead);

            default:
                return Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }
    }

    // a constant, or the members of an Enum, given other values.
    private static ProgramEdit Revalued(Declaration before, Declaration after, Func<ProgramEditKind, string, ProgramEdit> edit, ProgramEdits suspended,
        IReadOnlySet<string> activeDeclarations, IReadOnlyList<IReadOnlySet<string>> framesRead)
    {
        // the names whose value changed: the constant's, or the members of the Enum from the first one edited on - one given no value of its own is one more
        // than the member before it - and every constant written in terms of one of them, which changed with it.
        var (old, edited) = (before.Descriptor.Members, after.Descriptor.Members);
        var first = Enumerable.Range(0, Math.Max(old.Length, edited.Length))
            .FirstOrDefault(index => index >= old.Length || index >= edited.Length || Text(old[index]) != Text(edited[index]), -1);
        var names = ValuedWith(before.Kind == SymbolDescriptorKind.Enum
            ? first < 0 ? [] : old.Skip(first).Concat(edited.Skip(first)).Select(member => member.Name)
            : [before.Descriptor.Name], suspended);

        // what sizes stored data: the bounds of a module variable's array, or of a Static's, or a field of a Type; a fixed-length String of them; and the
        // locals of a procedure the program is in.
        foreach (var declaration in suspended._declarations.Values)
        {
            var sizing = declaration.Kind is SymbolDescriptorKind.ModuleField or SymbolDescriptorKind.UserDefinedType
                ? SizedWith(declaration.Descriptor)
                : declaration.Locals.Where(local => local.IsStatic || activeDeclarations.Contains(KeyOfDeclaration(declaration))).SelectMany(local => SizedWith(local.Array, local.FixedLengthName));
            if (sizing.Any(names.Contains))
            {
                return edit(ProgramEditKind.Restart, Resources.Host_Edit_ValueThatSizesStoredData);
            }
        }

        // a frame's code was lowered with the value folded in: it goes on with the value it read.
        return framesRead.Any(read => read.Overlaps(names))
            ? edit(ProgramEditKind.NotYet, Resources.Host_Edit_ValueAnActiveProcedureRead)
            : edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
    }

    // the names of the constants given, and of every constant whose value is written in terms of one of them, however indirectly: Const M = N + 1 has another
    // value when N has. A name is a name in any module: a constant of another module that shares it is taken as changed too, which refuses, and never runs
    // a program on a value it did not get.
    private static HashSet<string> ValuedWith(IEnumerable<string> changed, ProgramEdits suspended)
    {
        var names = new HashSet<string>(changed, StringComparer.OrdinalIgnoreCase);
        var constants = suspended._declarations.Values.SelectMany(declaration => declaration.Kind switch
        {
            SymbolDescriptorKind.Enum => declaration.Descriptor.Members.Select(member => (member.Name, Value: member.EnumValue?.BaseValue)),
            _ => declaration.Descriptor.Constants.Select(constant => (constant.Name, constant.Value)),
        }).Where(constant => constant.Value is not null).Select(constant => (constant.Name, Reads: NamesIn(constant.Value!).ToList())).ToList();

        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (name, reads) in constants)
            {
                if (!names.Contains(name) && reads.Any(names.Contains))
                {
                    grew = names.Add(name);
                }
            }
        }

        return names;
    }

    // the names a declaration and its fields are sized with: those its array bounds are written in terms of, and the constant a fixed-length String of
    // them is the length of.
    private static IEnumerable<string> SizedWith(SymbolDescriptor descriptor)
        => SizedWith(descriptor.Array, descriptor.FixedLengthName).Concat(descriptor.Members.SelectMany(SizedWith));

    private static IEnumerable<string> SizedWith(ArrayDescriptor? array, string? fixedLengthName)
        => (array?.Bounds ?? []).SelectMany(bound => new[] { bound.Lower, bound.Upper }).OfType<ExpressionNode>().SelectMany(NamesIn)
            .Concat(fixedLengthName is { } name ? [name] : []);

    // the names an expression or a statement refers to by name: not a string that spells one, nor the member of an object that has one.
    private static IEnumerable<string> NamesIn(SyntaxNode node)
        => node.Descendants().Prepend(node).OfType<SimpleNameExpressionNode>().Select(name => name.IdentifierName);

    private static string KeyOfDeclaration(Declaration declaration) => $"{declaration.Module.AbsoluteUri}|{declaration.Kind}|{declaration.Descriptor.Name.ToUpperInvariant()}";

    private static bool IsProcedure(SymbolDescriptorKind kind)
        => kind is SymbolDescriptorKind.Procedure or SymbolDescriptorKind.Function or SymbolDescriptorKind.PropertyGet or SymbolDescriptorKind.PropertyLet
            or SymbolDescriptorKind.PropertySet;

    private static string KeyOf(Uri module, SymbolDescriptor descriptor) => $"{module.AbsoluteUri}|{descriptor.Kind}|{descriptor.Name.ToUpperInvariant()}";

    private static string NameOfProcedure(string procedure) => procedure[(procedure.LastIndexOf('#') + 1)..];

    private static string NameOf(string module) => new Uri(module).Fragment.TrimStart('#') is { Length: > 0 } name ? name : module;

    private static ModuleText TextOf(VBModuleSymbol module)
        => new($"{module.Directives}", module is VBClassModuleSymbol type
            ? $"{string.Join(',', type.ImplementedInterfaceNames)}|{string.Join(',', type.Events.Select(@event => @event.Name))}"
            : null, module.Directives.Base);

    // what a declaration says, wherever it is written: a blank line above it, or a comment, leaves it as it was, and so does where else it is written (its
    // Definitions: a name declared implicitly by each statement that uses it, or once in each branch of a #If block).
    private static string Text(SymbolDescriptor descriptor)
        => System.Text.Json.JsonSerializer.Serialize(descriptor with { Definitions = [], Members = [.. descriptor.Members.Select(member => member with { Definitions = [] })] },
            SyntaxNodeJson.LocationFreeOptions);

    private static string Text(LocalDescriptor local) => System.Text.Json.JsonSerializer.Serialize(local, SyntaxNodeJson.LocationFreeOptions);

    private static string Text(ConstantDescriptor constant) => System.Text.Json.JsonSerializer.Serialize(constant, SyntaxNodeJson.LocationFreeOptions);

}

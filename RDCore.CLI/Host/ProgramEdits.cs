using System.Collections.Immutable;
using System.Text.RegularExpressions;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
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

    // a declaration of a module, by what it is and where: its whole text, its signature less its locals, its locals, and whether its module is a class.
    private sealed record class Declaration(string Name, SymbolDescriptorKind Kind, SymbolDescriptor Descriptor, string Whole, string Signature,
        ImmutableArray<LocalDescriptor> Locals, bool InClass, Uri Module, SourceRange Range);

    // what a module says of itself, apart from its members: its options, and what a class implements and raises.
    private sealed record class ModuleText(string Options, string? Interface);

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

        return new ProgramEdits(image.ProcedureFingerprints(), declarations.ToImmutable(), procedures.ToImmutable(), modules.ToImmutable(), active.ToImmutable());
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

        // what the code of the frames reads: a constant folded into it keeps the value it had when the activation began.
        var framesRead = frames.Select(frame => frame.Body is { } body ? string.Concat(body.Items.Select(item => item.Node is { } node ? NodeText(node) : string.Empty)) : string.Empty).ToList();

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

        // a procedure the program is in whose lines moved: where it goes on, and where its breakpoints are, are lines of the code it began with.
        foreach (var key in activeDeclarations)
        {
            if (suspended._declarations.GetValueOrDefault(key) is { } before && now._declarations.GetValueOrDefault(key) is { } after
                && before.Range.Start.Line != after.Range.Start.Line && !edits.Any(edit => edit.Name == after.Name && edit.Kind is not ProgramEditKind.Applies))
            {
                edits.Add(new ProgramEdit(after.Name, ProgramEditKind.NotYet, Resources.Host_Edit_LinesOfAnActiveProcedure, after.Module, after.Range));
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
                var inIt = activeDeclarations.Any(key => key.StartsWith(module + "|", StringComparison.Ordinal));
                edits.Add(new ProgramEdit(NameOf(module), inIt ? ProgramEditKind.NotYet : ProgramEditKind.Applies,
                    inIt ? Resources.Host_Edit_OptionOfAnActiveModule : Resources.Host_Edit_Applies, new Uri(module)));
            }
        }

        // a body changed, or gone, under the same declaration: read at its next call, unless the program is in it.
        foreach (var (procedure, fingerprint) in suspended._bodies)
        {
            var inIt = suspended._active.Contains(procedure);
            var changed = now._bodies.TryGetValue(procedure, out var current) ? current != fingerprint : inIt;
            var declared = now._procedures.GetValueOrDefault(procedure) is { } key ? now._declarations.GetValueOrDefault(key) : null;
            var name = declared?.Name ?? NameOfProcedure(procedure);
            if (changed && !edits.Any(edit => edit.Name == name && edit.Kind is not ProgramEditKind.Applies))
            {
                edits.Add(new ProgramEdit(name, inIt ? ProgramEditKind.NotYet : ProgramEditKind.Applies,
                    inIt ? Resources.Host_Edit_BodyOfAnActiveProcedure : Resources.Host_Edit_Applies, declared?.Module, declared?.Range));
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

        return edits;
    }

    // what an edited declaration is to the program.
    private static ProgramEdit Classify(Declaration? before, Declaration? after, bool active, IReadOnlySet<string> declaredAtAStop, ProgramEdits suspended,
        ProgramEdits now, IReadOnlySet<string> activeDeclarations, IReadOnlyList<string> framesRead)
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
        IReadOnlySet<string> activeDeclarations, IReadOnlyList<string> framesRead)
    {
        // the names whose value changed: the constant's, or each member of the Enum whose text is not what it was.
        var names = before.Kind == SymbolDescriptorKind.Enum
            ? before.Descriptor.Members.Where(member => !after.Descriptor.Members.Any(other => Text(other) == Text(member))).Select(member => member.Name).ToList()
            : [before.Descriptor.Name];
        bool Names(string text) => names.Any(name => Regex.IsMatch(text, $"\"{Regex.Escape(name)}\"", RegexOptions.IgnoreCase));

        // what sizes stored data: the bounds of a module variable's array, or of a Static's, or a field of a Type; a fixed-length String of them.
        foreach (var declaration in suspended._declarations.Values)
        {
            var stored = declaration.Kind is SymbolDescriptorKind.ModuleField or SymbolDescriptorKind.UserDefinedType
                ? Sizes(declaration.Descriptor)
                : string.Concat(declaration.Locals.Where(local => local.IsStatic || activeDeclarations.Contains(KeyOfDeclaration(declaration)))
                    .Select(local => System.Text.Json.JsonSerializer.Serialize(new { local.Array, local.FixedLength }, SyntaxNodeJson.LocationFreeOptions)));
            if (Names(stored))
            {
                return edit(ProgramEditKind.Restart, Resources.Host_Edit_ValueThatSizesStoredData);
            }
        }

        // a frame's code was lowered with the value folded in: it goes on with the value it read.
        return framesRead.Any(Names) ? edit(ProgramEditKind.NotYet, Resources.Host_Edit_ValueAnActiveProcedureRead) : edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
    }

    // the bounds and fixed lengths a declaration and its fields are sized with, as text.
    private static string Sizes(SymbolDescriptor descriptor)
        => System.Text.Json.JsonSerializer.Serialize(new { descriptor.Array, descriptor.FixedLength }, SyntaxNodeJson.LocationFreeOptions)
            + string.Concat(descriptor.Members.Select(Sizes));

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
            : null);

    // what a declaration says, wherever it is written: a blank line above it, or a comment, leaves it as it was, and so does where else it is written (its
    // Definitions: a name declared implicitly by each statement that uses it, or once in each branch of a #If block).
    private static string Text(SymbolDescriptor descriptor)
        => System.Text.Json.JsonSerializer.Serialize(descriptor with { Definitions = [], Members = [.. descriptor.Members.Select(member => member with { Definitions = [] })] },
            SyntaxNodeJson.LocationFreeOptions);

    private static string Text(LocalDescriptor local) => System.Text.Json.JsonSerializer.Serialize(local, SyntaxNodeJson.LocationFreeOptions);

    private static string Text(ConstantDescriptor constant) => System.Text.Json.JsonSerializer.Serialize(constant, SyntaxNodeJson.LocationFreeOptions);

    private static string NodeText(SyntaxNode node) => System.Text.Json.JsonSerializer.Serialize<SyntaxNode>(node, SyntaxNodeJson.LocationFreeOptions);
}

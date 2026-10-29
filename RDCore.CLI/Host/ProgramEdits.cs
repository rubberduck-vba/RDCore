using System.Collections.Immutable;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Symbols;

namespace RDCore.CLI.Host;

/// <summary>
/// What an edit made while a program waits is to it (plan-edit-and-continue §2): applied when it is resumed, applied in a later build, or a restart.
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
internal sealed record class ProgramEdit(string Name, ProgramEditKind Kind, string Reason);

/// <summary>
/// The code and the declarations a program was suspended with, and what was edited since (plan-edit-and-continue §2, §5): each procedure's lowered body,
/// each module's declarations as the host defined them, the procedures the program is in.
/// </summary>
internal sealed class ProgramEdits
{
    private readonly ImmutableDictionary<string, string> _bodies;
    private readonly ImmutableDictionary<string, Declaration> _declarations;
    private readonly ImmutableDictionary<string, string> _modules;
    private readonly ImmutableHashSet<string> _active;

    // a declaration of a module, by what it is and where: its whole text, its signature less its locals, its locals, and whether its module is a class.
    private sealed record class Declaration(string Name, SymbolDescriptorKind Kind, string Whole, string Signature, ImmutableArray<LocalDescriptor> Locals,
        bool InClass);

    private ProgramEdits(ImmutableDictionary<string, string> bodies, ImmutableDictionary<string, Declaration> declarations,
        ImmutableDictionary<string, string> modules, ImmutableHashSet<string> active)
        => (_bodies, _declarations, _modules, _active) = (bodies, declarations, modules, active);

    /// <summary>What the program of <paramref name="session"/> is, as it waits.</summary>
    public static ProgramEdits Of(IRuntimeSession session, ProgramImage image)
    {
        var declarations = ImmutableDictionary.CreateBuilder<string, Declaration>();
        var modules = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var module in session.Symbols.Scopes.Global.Declarations.OfType<VBModuleSymbol>().Where(module => module.GetProperty(SymbolProperties.Library) is null))
        {
            var inClass = module is VBClassModuleSymbol;
            modules[module.Uri.AbsoluteUri] = ModuleText(module);
            foreach (var descriptor in SymbolDescriptorProjector.Project(session.Symbols.DeclaredIn(module.Uri), module.Uri))
            {
                declarations[KeyOf(module.Uri, descriptor)] = new Declaration(
                    $"{module.Name}.{descriptor.Name}", descriptor.Kind, Text(descriptor), Text(descriptor with { Locals = [] }), descriptor.Locals, inClass);
            }
        }

        // the procedures with an activation on the stack: what the program is in the middle of.
        var active = ImmutableHashSet.CreateBuilder<string>();
        foreach (var frame in session.CallStack.Frames.OfType<CallStackFrame>())
        {
            if (frame.Procedure is { } procedure && SymbolDescriptorProjector.Project([procedure], procedure.ParentUri).FirstOrDefault() is { } descriptor)
            {
                _ = active.Add(KeyOf(procedure.ParentUri, descriptor));
            }
        }

        return new ProgramEdits(image.ProcedureFingerprints(), declarations.ToImmutable(), modules.ToImmutable(), active.ToImmutable());
    }

    /// <summary>
    /// What was edited since the program was suspended with what <paramref name="suspended"/> says, and what each edit is to it. The program is resumed only
    /// when every one of them <see cref="ProgramEditKind.Applies"/>.
    /// </summary>
    public static IReadOnlyList<ProgramEdit> Since(ProgramEdits suspended, IRuntimeSession session, ProgramImage image)
    {
        var now = Of(session, image);
        var edits = new List<ProgramEdit>();

        foreach (var key in suspended._declarations.Keys.Union(now._declarations.Keys))
        {
            var before = suspended._declarations.GetValueOrDefault(key);
            var after = now._declarations.GetValueOrDefault(key);
            if (before?.Whole == after?.Whole)
            {
                continue;
            }

            edits.Add(Classify(before, after, suspended._active.Contains(key)));
        }

        // a module's options and what a class implements or raises: read afresh by its code, unless it is a class whose objects the program may hold.
        foreach (var (module, text) in suspended._modules)
        {
            if (now._modules.TryGetValue(module, out var current) && current != text)
            {
                var isClass = text.StartsWith("class", StringComparison.Ordinal);
                edits.Add(new ProgramEdit(NameOf(module), isClass ? ProgramEditKind.Restart : ProgramEditKind.Applies,
                    isClass ? Resources.Host_Edit_ClassInterface : Resources.Host_Edit_Applies));
            }
        }

        // a body changed, or gone, under the same declaration: read at its next call, unless the program is in it.
        foreach (var (procedure, fingerprint) in suspended._bodies)
        {
            var inIt = suspended._active.Any(key => SameProcedure(key, procedure));
            var changed = now._bodies.TryGetValue(procedure, out var current) ? current != fingerprint : inIt;
            if (changed && !edits.Any(edit => edit.Name == NameOfProcedure(procedure)))
            {
                edits.Add(new ProgramEdit(NameOfProcedure(procedure), inIt ? ProgramEditKind.NotYet : ProgramEditKind.Applies,
                    inIt ? Resources.Host_Edit_BodyOfAnActiveProcedure : Resources.Host_Edit_Applies));
            }
        }

        // the code a module was last loaded with did not compile: its old bodies are still loaded, and resuming would run them.
        foreach (var module in now._modules.Keys)
        {
            if (image.IsLoaded(new Uri(module)) && image.Semantics.TryGet(new Uri(module), out var model) && !model.IsValid)
            {
                var first = model.CompileErrors.FirstOrDefault()?.Description ?? string.Empty;
                edits.Add(new ProgramEdit(NameOf(module), ProgramEditKind.DoesNotCompile, string.Format(Resources.Host_Edit_DoesNotCompile, first)));
            }
        }

        return edits;
    }

    // what an edited declaration is to the program, by plan-edit-and-continue §2.
    private static ProgramEdit Classify(Declaration? before, Declaration? after, bool active)
    {
        var name = (after ?? before)!.Name;
        var kind = (after ?? before)!.Kind;
        ProgramEdit Edit(ProgramEditKind editKind, string reason) => new(name, editKind, reason);

        // A3: added. A field of a class may be one of an object the program holds, which has no cell for it yet (A10, S3).
        if (before is null)
        {
            return kind == SymbolDescriptorKind.ModuleField && after!.InClass
                ? Edit(ProgramEditKind.NotYet, Resources.Host_Edit_FieldOfAClass)
                : Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }

        // A5: removed. A procedure the program is in can't be (R2), nor can a field of an object it may hold (R5).
        if (after is null)
        {
            return active ? Edit(ProgramEditKind.Restart, Resources.Host_Edit_SignatureOfAnActiveProcedure)
                : kind == SymbolDescriptorKind.ModuleField && before.InClass ? Edit(ProgramEditKind.Restart, Resources.Host_Edit_StoredValue)
                : Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }

        if (IsProcedure(kind))
        {
            // R2: what the frame's arguments and result are laid out by.
            if (active && before.Signature != after.Signature)
            {
                return Edit(ProgramEditKind.Restart, Resources.Host_Edit_SignatureOfAnActiveProcedure);
            }

            // R5: a Static keeps its storage, of the type it had; R1: a local of the frame, retyped; A4 / A5 in the frame: S3.
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

            return active && !before.Locals.Select(Text).SequenceEqual(after.Locals.Select(Text))
                ? Edit(ProgramEditKind.NotYet, Resources.Host_Edit_LocalsOfAnActiveProcedure)
                : active ? Edit(ProgramEditKind.NotYet, Resources.Host_Edit_BodyOfAnActiveProcedure)
                : Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies);
        }

        // R5: a variable holds a value, of the type it was declared with; so does each field of a value of a Type.
        return kind switch
        {
            SymbolDescriptorKind.ModuleField or SymbolDescriptorKind.UserDefinedType => Edit(ProgramEditKind.Restart, Resources.Host_Edit_StoredValue),
            SymbolDescriptorKind.Event => Edit(ProgramEditKind.Restart, Resources.Host_Edit_ClassInterface),
            _ => Edit(ProgramEditKind.Applies, Resources.Host_Edit_Applies),
        };
    }

    private static bool IsProcedure(SymbolDescriptorKind kind)
        => kind is SymbolDescriptorKind.Procedure or SymbolDescriptorKind.Function or SymbolDescriptorKind.PropertyGet or SymbolDescriptorKind.PropertyLet
            or SymbolDescriptorKind.PropertySet;

    private static string KeyOf(Uri module, SymbolDescriptor descriptor) => $"{module.AbsoluteUri}|{descriptor.Kind}|{descriptor.Name.ToUpperInvariant()}";

    // a procedure's fingerprint is keyed by its SemanticId, a declaration by its module, its kind and its name.
    private static bool SameProcedure(string key, string procedure)
    {
        var parts = key.Split('|');
        return procedure.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)
            && procedure.EndsWith("." + parts[2], StringComparison.OrdinalIgnoreCase);
    }

    private static string NameOfProcedure(string procedure) => procedure[(procedure.LastIndexOf('#') + 1)..];

    private static string NameOf(string module) => new Uri(module).Fragment.TrimStart('#') is { Length: > 0 } name ? name : module;

    // what a module says of itself, apart from its members: its options, and what a class implements and raises.
    private static string ModuleText(VBModuleSymbol module)
        => module is VBClassModuleSymbol type
            ? $"class|{module.Directives}|{string.Join(',', type.ImplementedInterfaceNames)}|{string.Join(',', type.Events.Select(@event => @event.Name))}"
            : $"module|{module.Directives}";

    // what a declaration says, wherever it is written: a blank line above it, or a comment, leaves it as it was, and so does where else it is written (its
    // Definitions: a name declared implicitly by each statement that uses it, or once in each branch of a #If block).
    private static string Text(SymbolDescriptor descriptor)
        => System.Text.Json.JsonSerializer.Serialize(descriptor with { Definitions = [], Members = [.. descriptor.Members.Select(member => member with { Definitions = [] })] },
            SyntaxNodeJson.LocationFreeOptions);

    private static string Text(LocalDescriptor local) => System.Text.Json.JsonSerializer.Serialize(local, SyntaxNodeJson.LocationFreeOptions);
}

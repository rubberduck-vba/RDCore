using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The static semantics of what a class module declares about events: its <c>Event</c> declarations
/// (<strong>MS-VBAL §5.2.4.3</strong>), its <c>WithEvents</c> variables (<strong>§5.2.3.1.2</strong>) and the procedures
/// that handle an event of one (<strong>§5.3.1.8</strong>).
/// </summary>
/// <remarks>
/// What a <c>RaiseEvent</c> statement may say is <see cref="StatementStaticSemanticsEvaluator"/>'s: this is what the
/// declarations may.
/// <list type="bullet">
/// <item>An event name is unique within the class module (<see cref="VBCompileErrorId.DuplicateDeclaration"/>) and has no
/// underscore (<see cref="VBCompileErrorId.InvalidEventName"/>): an underscore is what ends the name of a variable in the
/// name of a handler.</item>
/// <item>A <c>WithEvents</c> variable is declared as a specific class that has at least one event, and not as the class of
/// the module that contains it (<see cref="VBCompileErrorId.InvalidWithEventsType"/>).</item>
/// <item>A procedure that qualifies as the handler of an event is a subroutine whose parameter list is compatible with the
/// event's (<see cref="VBCompileErrorId.InvalidEventHandler"/>): the same number of parameters, each of the same type
/// and parameter mechanism - though not necessarily of the same name, nor with the mechanism written out in the same way.</item>
/// </list>
/// A procedure qualifies as a handler by its name alone, <c>VariableName_EventName</c>. One whose name only begins with
/// the name of a <c>WithEvents</c> variable and an underscore is no handler of anything, and is not checked.
/// <para>
/// A type that has not been resolved is not a type that is wrong: whatever is declared as one is not reported.
/// </para>
/// </remarks>
public static class ClassModuleEventSemantics
{
    /// <summary>
    /// Checks everything <paramref name="module"/> declares about events.
    /// </summary>
    /// <param name="module">The class module, with its members.</param>
    /// <param name="resolver">
    /// What finds the class a <c>WithEvents</c> variable is declared as, as it is now: a declared type carries the class as
    /// it was when the type was built. <see langword="null"/> checks against what the types carry.
    /// </param>
    /// <returns>Every error found, events first, then variables, then handlers; empty when the declarations are valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(VBClassModuleSymbol module, ISymbolResolver? resolver = null)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        CheckEvents(module, errors);
        foreach (var variable in module.WithEventsVariables)
        {
            if (CheckWithEventsType(module, variable, resolver, errors) is { } source)
            {
                CheckHandlers(module, variable, source, errors);
            }
        }

        return errors.ToImmutable();
    }

    private static SourceLocation LocationOf(VBTypeMemberSymbol member) => new(member.ParentUri, member.SelectionRange);

    private static void CheckEvents(VBClassModuleSymbol module, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declared in module.Events)
        {
            if (declared.Name.Contains('_'))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidEventName, LocationOf(declared),
                    $"The name of event '{declared.Name}' contains an underscore (MS-VBAL §5.2.4.3)."));
            }

            if (!seen.Add(declared.Name))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, LocationOf(declared),
                    $"Event '{declared.Name}' is declared more than once in this class module (MS-VBAL §5.2.4.3)."));
            }
        }
    }

    // the class the variable is declared as, when it is a valid one to handle the events of; null when it is not, after
    // saying why, and when it is not known to be one.
    private static VBClassModuleSymbol? CheckWithEventsType(
        VBClassModuleSymbol module, VBTypeMemberSymbol variable, ISymbolResolver? resolver, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (variable.ResolvedType is VBUnknownType)
        {
            return null;
        }

        if (variable.ResolvedType is not VBClassType declared)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidWithEventsType, LocationOf(variable),
                $"'{variable.Name}' is declared As {variable.ResolvedType.Name}, which is not a specific class (MS-VBAL §5.2.3.1.2)."));
            return null;
        }

        var source = resolver?.ResolveType(declared.Symbol.Name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol as VBClassModuleSymbol
            ?? declared.Symbol;

        // a Uri's fragment is where a symbol's identity lives, and Uri equality ignores it.
        if (source.Uri.AbsoluteUri == module.Uri.AbsoluteUri)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidWithEventsType, LocationOf(variable),
                $"'{variable.Name}' is declared As {source.Name}, the class of the module that contains it (MS-VBAL §5.2.3.1.2)."));
            return null;
        }

        if (!source.Events.Any())
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidWithEventsType, LocationOf(variable),
                $"'{variable.Name}' is declared As {source.Name}, which has no events (MS-VBAL §5.2.3.1.2)."));
            return null;
        }

        return source;
    }

    private static void CheckHandlers(
        VBClassModuleSymbol module, VBTypeMemberSymbol variable, VBClassModuleSymbol source, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        foreach (var handled in source.Events)
        {
            var name = $"{variable.Name}_{handled.Name}";
            foreach (var candidate in module.Members.Where(member => IsProcedure(member)
                && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                if (!IsSubroutine(candidate))
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidEventHandler, LocationOf(candidate),
                        $"'{candidate.Name}' handles event '{handled.Name}' of '{variable.Name}', and an event handler must be a subroutine (MS-VBAL §5.3.1.8)."));
                }
                else if (IncompatibilityOf(ParametersOf(candidate), handled.Parameters) is { } reason)
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidEventHandler, LocationOf(candidate),
                        $"'{candidate.Name}' handles event '{handled.Name}' of '{variable.Name}', and its parameter list is not compatible with the event's: {reason} (MS-VBAL §5.3.1.8)."));
                }
            }
        }
    }

    private static bool IsProcedure(VBTypeMemberSymbol member) => member is VBProcedureMemberSymbol or VBFunctionMemberSymbol;

    // Property Let and Property Set derive from the subroutine's symbol, but are not subroutines.
    private static bool IsSubroutine(VBTypeMemberSymbol member)
        => member is VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol);

    // the implicit Me every member of a class module has at parameter 0 is not a parameter of its parameter list.
    private static ImmutableArray<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member)
    {
        var parameters = member switch
        {
            VBProcedureMemberSymbol procedure => procedure.Parameters,
            VBFunctionMemberSymbol function => function.Parameters,
            _ => [],
        };

        return parameters is [{ Name: "Me" }, ..] ? parameters.RemoveAt(0) : parameters;
    }

    // MS-VBAL §5.3.1.8: the same number of parameters, each of the same type and parameter mechanism.
    private static string? IncompatibilityOf(ImmutableArray<VBParameterSymbol> handler, ImmutableArray<VBParameterSymbol> declared)
    {
        if (handler.Length != declared.Length)
        {
            return $"it has {handler.Length} parameter(s) and the event has {declared.Length}";
        }

        for (var i = 0; i < declared.Length; i++)
        {
            if (!SameType(handler[i].ResolvedType, declared[i].ResolvedType))
            {
                return $"parameter {i + 1} is a {handler[i].ResolvedType.Name} and the event's is a {declared[i].ResolvedType.Name}";
            }

            if (IsByRef(handler[i].ParameterKind) != IsByRef(declared[i].ParameterKind))
            {
                return $"parameter {i + 1} is passed {(IsByRef(handler[i].ParameterKind) ? "ByRef" : "ByVal")} and the event's {(IsByRef(declared[i].ParameterKind) ? "ByRef" : "ByVal")}";
            }
        }

        return null;
    }

    // a type that is not known is not a type that differs.
    private static bool SameType(Model.Types.Abstract.VBType left, Model.Types.Abstract.VBType right)
        => left is VBUnknownType || right is VBUnknownType || string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;
}

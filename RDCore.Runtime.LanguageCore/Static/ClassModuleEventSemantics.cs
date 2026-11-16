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
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.2.4.3, §5.2.3.1.2, §5.3.1.8
// an event name has no underscore because an underscore ends the variable's name in a handler's name.
public static class ClassModuleEventSemantics
{
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
        // a form's control array raises the events of its controls' class
        var type = RDCore.SDK.Workspace.Forms.FormControlArray.ElementOf(variable.ResolvedType) ?? variable.ResolvedType;
        if (type is VBUnknownType)
        {
            return null;
        }

        if (type is not VBClassType declared)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidWithEventsType, LocationOf(variable),
                $"'{variable.Name}' is declared As {type.Name}, which is not a specific class (MS-VBAL §5.2.3.1.2)."));
            return null;
        }

        var source = (resolver is null ? null : VBProjectSymbol.ResolveClass(resolver, declared.Symbol)) ?? declared.Symbol;

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

    // a form's control is no variable the code declares: MS-VBA binds the handlers of its events when the event is raised, and compiles one that does not match
    // the event, which is then never called (observed in Excel). It is dead code, which the static pass says (DeclarationRole.UnmatchedEventHandler),
    // and not an error.
    private static void CheckHandlers(
        VBClassModuleSymbol module, VBTypeMemberSymbol variable, VBClassModuleSymbol source, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (variable.GetProperty(SymbolProperties.DesignerControl))
        {
            return;
        }

        foreach (var handled in source.Events)
        {
            foreach (var candidate in HandlersOf(module, variable, handled))
            {
                if (!IsSubroutine(candidate))
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidEventHandler, LocationOf(candidate),
                        $"'{candidate.Name}' handles event '{handled.Name}' of '{variable.Name}', and an event handler must be a subroutine (MS-VBAL §5.3.1.8)."));
                }
                else if (ParameterLists.Incompatibility(ParameterLists.Of(candidate), handled.Parameters) is { } reason)
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidEventHandler, LocationOf(candidate),
                        $"'{candidate.Name}' handles event '{handled.Name}' of '{variable.Name}', and its parameter list is not compatible with the event's: {reason} (MS-VBAL §5.3.1.8)."));
                }
            }
        }
    }

    // MS-VBAL §5.3.1.8
    internal static IEnumerable<VBTypeMemberSymbol> HandlersOf(VBClassModuleSymbol module, VBTypeMemberSymbol variable, VBEventMemberSymbol handled)
        => module.Members.Where(member => IsProcedure(member)
            && string.Equals(member.Name, $"{variable.Name}_{handled.Name}", StringComparison.OrdinalIgnoreCase));

    // MS-VBAL §5.3.1.8
    internal static bool Matches(VBTypeMemberSymbol candidate, VBEventMemberSymbol handled) => Matches(candidate, handled.Parameters);

    internal static bool Matches(VBTypeMemberSymbol candidate, System.Collections.Immutable.ImmutableArray<VBParameterSymbol> required)
        => IsSubroutine(candidate) && ParameterLists.Incompatibility(ParameterLists.Of(candidate), required) is null;

    private static bool IsProcedure(VBTypeMemberSymbol member) => member is VBProcedureMemberSymbol or VBFunctionMemberSymbol;

    // Property Let and Property Set derive from the subroutine's symbol, but are not subroutines.
    private static bool IsSubroutine(VBTypeMemberSymbol member)
        => member is VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol);

}

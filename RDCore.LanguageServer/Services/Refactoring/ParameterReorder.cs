using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Services.Refactoring;

public enum ParameterReorderRefusalKind
{
    NoProcedure,

    OneParameter,

    NotDeclaredByWorkspace,

    Event,

    // MS-VBAL §5.3.1.8
    Handler,

    ExternalProcedure,

    Order,

    // MS-VBAL §5.3.1.5
    Optional,

    // MS-VBAL §5.3.1.5
    ParamArray,

    Mismatched,

    // MS-VBAL §5.6.12
    LateBound,

    // MS-VBAL §6.1.2.8.1.1
    CalledByName,

    Unresolved,

    // MS-VBAL §5.3.1.11 (runtime semantics of procedure invocation): the arguments are assigned to the parameters in their order.
    Evaluated,

    // MS-VBAL §5.3.1.11: a call whose arguments another order of the parameters would evaluate in another order.
    MayEvaluate,
}

public sealed record class ParameterReorderRefusal(ParameterReorderRefusalKind Kind, string Name, string? Other = null, Uri? Module = null, SourcePosition Position = default)
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.ParameterReorderMessages", typeof(ParameterReorderRefusal).Assembly);

    // a refusal of a position where no procedure with parameters to reorder is declared: a code action offers nothing there.
    public bool FindsNothing => Kind is ParameterReorderRefusalKind.NoProcedure or ParameterReorderRefusalKind.OneParameter;

    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
    {
        var where = Module is null ? string.Empty : (locate ?? DefaultLocation)(Module, Position);
        return Format($"ReorderParameters_{Kind}", culture, Name, Other, where);
    }

    // the title of the code action that reorders the parameters of a procedure, in the language of the user interface.
    public static string Title(string procedure, CultureInfo? culture = null) => Format("ReorderParameters_Title", culture, procedure);

    public static string Format(string key, CultureInfo? culture, params object?[] arguments)
    {
        var ui = culture ?? CultureInfo.CurrentUICulture;
        return string.Format(ui, Messages.GetString(key, ui) ?? key, arguments);
    }

    private static string DefaultLocation(Uri module, SourcePosition position) => $"{SymbolUri.ModuleNameOf(module)} {position.Line + 1}:{position.Character + 1}";
}

// the procedure at a position, and its parameters as they are declared: the value of a Property Let or Set, which stays last, is not one of them.
public sealed record class ParameterReorderTarget(string Name, Uri Module, SourceRange Range, SemanticId Procedure, ImmutableArray<string> Parameters, ImmutableArray<string> Declarations);

// the edits, or why there are none; the changes the user accepts before they are made (an argument that does something, evaluated in another order); and
// the calls whose arguments an order of the parameters could evaluate in another order, which the user is told of before choosing one.
public sealed record class ParameterReorderPlan(ImmutableArray<RenameEdit> Edits, ParameterReorderRefusal? Refusal, ImmutableArray<ParameterReorderRefusal> Changes, ImmutableArray<ParameterReorderRefusal> Possible)
{
    public bool IsValid => Refusal is null;
}

// MS-VBAL §5.3.1.5, §5.6.13.1: the parameters of a procedure are written in another order, and the arguments passed by position in every call to it with
// them. An argument passed by name stays where it is written, unless its parameter moves before one whose argument is passed by position: it is then passed by
// position. The procedures whose parameters are the procedure's change with it.
public static class ParameterReorder
{
    public static (ParameterReorderTarget? Target, ParameterReorderRefusal? Refusal) Prepare(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source
            || NameOccurrences.NameAt(source, position) is not { } name)
        {
            return (null, new(ParameterReorderRefusalKind.NoProcedure, string.Empty));
        }

        // the name of a procedure, where it is declared or called, or the declaration of one of its parameters.
        var here = NameOccurrences.Find(context, name, [source]).Occurrences
            .FirstOrDefault(occurrence => occurrence.Binding is not null && SymbolRename.Contains(occurrence.Range, position));
        var symbol = here.Binding is { } binding ? NameOccurrences.SymbolOf(context, binding) : null;
        if (symbol is VBParameterSymbol parameter)
        {
            symbol = here.Kind is OccurrenceKind.Declaration ? NameOccurrences.SymbolOf(context, new SemanticId(parameter.ParentUri)) : null;
        }

        if (symbol is null || ParameterRemoval.ParametersOf(symbol) is not { Length: > 0 } parameters)
        {
            return (null, new(ParameterReorderRefusalKind.NoProcedure, name));
        }

        var count = Reordered(symbol, parameters);
        if (count < 2)
        {
            return (null, new(ParameterReorderRefusalKind.OneParameter, symbol.Name));
        }

        var declarations = parameters.Take(count).Select(each => DeclarationTextOf(context, each) ?? each.Name).ToImmutableArray();
        return (new ParameterReorderTarget(symbol.Name, module, here.Range, symbol.SemanticId, [.. parameters.Take(count).Select(each => each.Name)], declarations), null);
    }

    // order: for each position of the new list, the position the parameter written there has now.
    public static ParameterReorderPlan Plan(RenameContext context, ParameterReorderTarget target, IReadOnlyList<int> order)
    {
        // the static pass recurses once per level of an expression: one large-stack thread for the whole plan.
        if (!SDK.Platform.LargeStackThread.IsCurrent)
        {
            return SDK.Platform.LargeStackThread.Run(() => Plan(context, target, order));
        }

        if (NameOccurrences.SymbolOf(context, target.Procedure) is not { } procedure)
        {
            return Refused(new(ParameterReorderRefusalKind.NoProcedure, target.Name));
        }

        if (RefusalOf(context, procedure) is { } refusal)
        {
            return Refused(refusal);
        }

        if (order.Count != target.Parameters.Length || order.Distinct().Count() != order.Count || order.Any(index => index < 0 || index >= order.Count))
        {
            return Refused(new(ParameterReorderRefusalKind.Order, procedure.Name));
        }

        if (SignatureRefusalOf(context, procedure, order) is { } invalid)
        {
            return Refused(invalid);
        }

        if (ParameterRemoval.FamilyOf(context, procedure, target.Parameters[0]) is not { } family)
        {
            return Refused(new(ParameterReorderRefusalKind.Handler, procedure.Name));
        }

        var edits = new List<RenameEdit>();
        var changes = new List<(ParameterReorderRefusal Change, bool Changed)>();
        foreach (var member in family)
        {
            if (MemberEdits(context, member, order, edits, changes) is { } refused)
            {
                return Refused(refused);
            }
        }

        return new([.. edits
            .DistinctBy(edit => (edit.Module.AbsoluteUri, edit.Range))
            .OrderBy(edit => edit.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(edit => edit.Range.Start)], null, Distinct(changes.Where(change => change.Changed)), Distinct(changes));
    }

    private static ParameterReorderPlan Refused(ParameterReorderRefusal refusal) => new([], refusal, [], []);

    private static ImmutableArray<ParameterReorderRefusal> Distinct(IEnumerable<(ParameterReorderRefusal Change, bool Changed)> changes)
        => [.. changes.Select(change => change.Change).DistinctBy(change => (change.Module?.AbsoluteUri, change.Position))];

    private static ParameterReorderRefusal? RefusalOf(RenameContext context, Symbol procedure) => procedure switch
    {
        _ when !NameOccurrences.IsDeclaredBy(context, procedure) => new(ParameterReorderRefusalKind.NotDeclaredByWorkspace, procedure.Name),
        VBEventMemberSymbol => new(ParameterReorderRefusalKind.Event, procedure.Name),
        VBExternalSubMemberSymbol or VBExternalFunctionMemberSymbol => new(ParameterReorderRefusalKind.ExternalProcedure, procedure.Name),
        _ => null,
    };

    // MS-VBAL §5.3.1.5: the optional parameters come after the others, and a ParamArray comes last.
    private static ParameterReorderRefusal? SignatureRefusalOf(RenameContext context, Symbol procedure, IReadOnlyList<int> order)
    {
        var parameters = ParameterRemoval.ParametersOf(procedure);
        VBParameterSymbol? optional = null;
        for (var position = 0; position < order.Count; position++)
        {
            var parameter = parameters[order[position]];
            if (IsParamArray(context, parameter) && position != order.Count - 1)
            {
                return new(ParameterReorderRefusalKind.ParamArray, parameter.Name);
            }

            if (parameter.IsOptional)
            {
                optional ??= parameter;
            }
            else if (optional is not null && !IsParamArray(context, parameter))
            {
                return new(ParameterReorderRefusalKind.Optional, parameter.Name, optional.Name);
            }
        }

        return null;
    }

    // the edits of one procedure: its parameter list, and every call to it.
    private static ParameterReorderRefusal? MemberEdits(
        RenameContext context, Symbol member, IReadOnlyList<int> order, List<RenameEdit> edits, List<(ParameterReorderRefusal Change, bool Changed)> changes)
    {
        var parameters = ParameterRemoval.ParametersOf(member);
        if (Reordered(member, parameters) != order.Count)
        {
            return new(ParameterReorderRefusalKind.Mismatched, member.Name, NameOccurrences.Describe(member));
        }

        var at = NameOccurrences.ModuleOf(member.Uri);
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri) is not { } source
            || ParameterRemoval.DeclarationOf(source, parameters[0]) is not { } declaration)
        {
            return new(ParameterReorderRefusalKind.NotDeclaredByWorkspace, member.Name);
        }

        var list = ParameterRemoval.Inside(declaration.Procedure.ParameterListRange!.Value);
        var items = TextsOf(source.Source, ListEdits.ItemsOf(source.Source, list));
        if (items.Count != parameters.Length)
        {
            return new(ParameterReorderRefusalKind.Unresolved, member.Name, Module: source.Module, Position: list.Start);
        }

        edits.AddRange(ListEdits.ReplaceItems(source.Source, list, [.. order.Select(index => items[index]), .. items.Skip(order.Count)])
            .Select(edit => ParameterRemoval.EditOf(source.Module, edit)));
        return CallEdits(context, member, parameters, order, edits, changes);
    }

    // every call to the procedure: its arguments, written in the order of the parameters they are passed to.
    private static ParameterReorderRefusal? CallEdits(
        RenameContext context, Symbol member, ImmutableArray<VBParameterSymbol> parameters, IReadOnlyList<int> order, List<RenameEdit> edits,
        List<(ParameterReorderRefusal Change, bool Changed)> changes)
    {
        var (sites, problem) = ParameterRemoval.CallSitesOf(context, member);
        if (problem is { } missed)
        {
            return new(missed.Kind switch
            {
                ParameterRemoval.CallProblemKind.CalledByName => ParameterReorderRefusalKind.CalledByName,
                ParameterRemoval.CallProblemKind.LateBound => ParameterReorderRefusalKind.LateBound,
                _ => ParameterReorderRefusalKind.Unresolved,
            }, member.Name, Module: missed.Module, Position: missed.Position);
        }

        var count = order.Count;
        var positionOf = new int[count];
        for (var position = 0; position < count; position++)
        {
            positionOf[order[position]] = position;
        }

        foreach (var (source, callee, call, bindingOf) in sites)
        {
            if (call.List is not { } list)
            {
                return new(ParameterReorderRefusalKind.Unresolved, member.Name, Module: source.Module, Position: callee);
            }

            var written = TextsOf(source.Source, ListEdits.ItemsOf(source.Source, list));
            if (written.Count != call.Arguments.Length)
            {
                return new(ParameterReorderRefusalKind.Unresolved, member.Name, Module: source.Module, Position: callee);
            }

            // each argument by the parameter it is passed to; one passed by name to no parameter of the list is left where it is.
            var positional = call.Arguments.TakeWhile(argument => argument is not NamedArgumentNode).Count();
            var named = new Dictionary<int, int>();
            for (var argument = positional; argument < call.Arguments.Length; argument++)
            {
                if (call.Arguments[argument] is NamedArgumentNode { Name: var name }
                    && parameters.Take(count).Select(each => each.Name).ToList().FindIndex(each => string.Equals(each, name, StringComparison.OrdinalIgnoreCase)) is >= 0 and var index)
                {
                    named[index] = argument;
                }
            }

            // the arguments passed by position, in the new order, up to the last that is written; an argument passed by name to a parameter before that one is
            // passed by position. The arguments past the parameters of the list (a ParamArray's) stay after them.
            var last = Enumerable.Range(0, count).LastOrDefault(position => order[position] < positional && written[order[position]].Length > 0, -1);
            var items = new List<string>();

            // the arguments as they are written after the edit, by their position in the call before it (-1 for one that is omitted).
            var arguments = new List<int>();
            for (var position = 0; position <= last; position++)
            {
                var index = order[position];
                var argument = index < positional ? index : named.TryGetValue(index, out var passed) ? passed : -1;
                items.Add(argument < 0 ? string.Empty
                    : call.Arguments[argument] is NamedArgumentNode { Value: var value } ? TextOf(source.Source, value.Location.Range) : written[argument]);
                arguments.Add(argument);
            }

            foreach (var argument in Enumerable.Range(0, call.Arguments.Length).Where(argument => argument >= count && argument < positional || argument >= positional && !arguments.Contains(argument)))
            {
                items.Add(written[argument]);
                arguments.Add(argument);
            }

            var parameterOf = Enumerable.Range(0, call.Arguments.Length)
                .ToDictionary(argument => argument, argument => argument < positional ? Math.Min(argument, count - 1) : named.FirstOrDefault(pair => pair.Value == argument, new(-1, -1)).Key);
            var (evaluated, changed) = EvaluationChange(context, call, bindingOf, arguments, parameterOf, positionOf);
            if (evaluated is { } first)
            {
                changes.Add((new(changed ? ParameterReorderRefusalKind.Evaluated : ParameterReorderRefusalKind.MayEvaluate, member.Name, Module: source.Module, Position: first), changed));
            }

            edits.AddRange(ListEdits.ReplaceItems(source.Source, list, items).Select(edit => ParameterRemoval.EditOf(source.Module, edit)));
        }

        return null;
    }

    // MS-VBAL §5.3.1.11: the arguments are evaluated in the order they are written, and assigned to the parameters in their order. Of two arguments that do
    // more than read a value, one that would be written or assigned after the other in another order may do what it does in another order. The position of
    // the first argument that does more than read a value, when there are two (an order of the parameters can change what the call does), and whether this
    // order does.
    private static (SourcePosition? First, bool Changed) EvaluationChange(
        RenameContext context, ParameterRemoval.Call call, Func<ExpressionNode, SemanticId?> bindingOf, List<int> arguments, Dictionary<int, int> parameterOf,
        int[] positionOf)
    {
        var evaluated = Enumerable.Range(0, call.Arguments.Length)
            .Where(argument => parameterOf[argument] >= 0
                && !ParameterRemoval.IsInert(context, call.Arguments[argument] is NamedArgumentNode { Value: var value } ? value : call.Arguments[argument], bindingOf))
            .ToList();
        if (evaluated.Count < 2)
        {
            return (null, false);
        }

        foreach (var first in evaluated)
        {
            foreach (var second in evaluated.Where(second => second > first))
            {
                var (a, b) = (parameterOf[first], parameterOf[second]);
                if (arguments.IndexOf(first) > arguments.IndexOf(second) || a != b && a < b != positionOf[a] < positionOf[b])
                {
                    return (call.Arguments[first].Location.Range.Start, true);
                }
            }
        }

        return (call.Arguments[evaluated[0]].Location.Range.Start, false);
    }

    // the parameters that are reordered: a Property Let or Set keeps its value last.
    private static int Reordered(Symbol member, ImmutableArray<VBParameterSymbol> parameters)
        => member is VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol ? parameters.Length - 1 : parameters.Length;

    private static bool IsParamArray(RenameContext context, VBParameterSymbol parameter)
    {
        var at = NameOccurrences.ModuleOf(parameter.Uri);
        return context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri) is { } source
            && ParameterRemoval.DeclarationOf(source, parameter) is { Parameter.IsParamArray: true };
    }

    // the declaration of a parameter as it is written (ByVal a As Long), for a dialog to show.
    private static string? DeclarationTextOf(RenameContext context, VBParameterSymbol parameter)
    {
        var at = NameOccurrences.ModuleOf(parameter.Uri);
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri) is not { } source
            || ParameterRemoval.DeclarationOf(source, parameter) is not { } declaration)
        {
            return null;
        }

        var list = ParameterRemoval.Inside(declaration.Procedure.ParameterListRange!.Value);
        return ListEdits.ItemsOf(source.Source, list).Where(item => SymbolRename.Contains(item, parameter.SelectionRange.Start))
            .Select(item => TextOf(source.Source, item)).FirstOrDefault();
    }

    private static IReadOnlyList<string> TextsOf(string source, IReadOnlyList<SourceRange> items) => [.. items.Select(item => TextOf(source, item))];

    private static string TextOf(string source, SourceRange range)
    {
        var lines = source.Split('\n');
        if (range.Start.Line == range.End.Line)
        {
            return lines[range.Start.Line][range.Start.Character..range.End.Character];
        }

        return string.Join('\n', [lines[range.Start.Line][range.Start.Character..], .. lines[(range.Start.Line + 1)..range.End.Line], lines[range.End.Line][..range.End.Character]]);
    }
}

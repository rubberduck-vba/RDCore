using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
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

public enum ParameterRemovalRefusalKind
{
    NoParameter,

    NotDeclaredByWorkspace,

    // MS-VBAL §5.3.1.5
    Used,

    // MS-VBAL §5.3.1.5
    ParamArray,

    // MS-VBAL §5.3.1.7
    Value,

    Event,

    // MS-VBAL §5.3.1.8
    Handler,

    ExternalProcedure,

    // MS-VBAL §5.6.13.1
    Evaluated,

    // MS-VBAL §5.6.12
    LateBound,

    // MS-VBAL §6.1.2.8.1.1
    CalledByName,

    Unresolved,
}

public sealed record class ParameterRemovalRefusal(ParameterRemovalRefusalKind Kind, string Name, string? Other = null, Uri? Module = null, SourcePosition Position = default)
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.ParameterRemovalMessages", typeof(ParameterRemovalRefusal).Assembly);

    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
    {
        var where = Module is null ? string.Empty : (locate ?? DefaultLocation)(Module, Position);
        var format = Messages.GetString($"RemoveParameter_{Kind}", culture ?? CultureInfo.CurrentUICulture) ?? Kind.ToString();
        return string.Format(culture ?? CultureInfo.CurrentUICulture, format, Name, Other, where);
    }

    // the title of the code action that removes a parameter, in the language of the user interface.
    public static string Title(string parameter, CultureInfo? culture = null)
        => string.Format(culture ?? CultureInfo.CurrentUICulture, Messages.GetString("RemoveParameter_Title", culture ?? CultureInfo.CurrentUICulture)!, parameter);

    private static string DefaultLocation(Uri module, SourcePosition position) => $"{SymbolUri.ModuleNameOf(module)} {position.Line + 1}:{position.Character + 1}";
}

// the parameter at a position: the procedure it is a parameter of, and where it is in its list.
public sealed record class ParameterRemovalTarget(string Name, Uri Module, SourceRange Range, SemanticId Procedure, int Index);

public sealed record class ParameterRemovalPlan(ImmutableArray<RenameEdit> Edits, ParameterRemovalRefusal? Refusal)
{
    public bool IsValid => Refusal is null;
}

// MS-VBAL §5.3.1.5, §5.6.13.1: a parameter is removed from its declaration, and its argument from every call that passes it, by position or by name. What
// would change what the code does is refused: a use of the parameter, an argument whose evaluation does something, a call the edit cannot find.
public static class ParameterRemoval
{
    public static (ParameterRemovalTarget? Target, ParameterRemovalRefusal? Refusal) Prepare(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source
            || NameOccurrences.NameAt(source, position) is not { } name)
        {
            return (null, new(ParameterRemovalRefusalKind.NoParameter, string.Empty));
        }

        var here = NameOccurrences.Find(context, name, [source]).Occurrences
            .FirstOrDefault(occurrence => occurrence.Kind is OccurrenceKind.Declaration && SymbolRename.Contains(occurrence.Range, position));
        if (here.Binding is not { } binding || NameOccurrences.SymbolOf(context, binding) is not VBParameterSymbol parameter
            || NameOccurrences.SymbolOf(context, new SemanticId(parameter.ParentUri)) is not { } procedure
            || ParametersOf(procedure).Select(candidate => candidate.SemanticId).ToList().IndexOf(binding) is not (>= 0 and var index))
        {
            return (null, new(ParameterRemovalRefusalKind.NoParameter, name));
        }

        return (new ParameterRemovalTarget(parameter.Name, module, here.Range, procedure.SemanticId, index), null);
    }

    public static ParameterRemovalPlan Plan(RenameContext context, ParameterRemovalTarget target)
    {
        // the static pass recurses once per level of an expression: one large-stack thread for the whole plan.
        if (!SDK.Platform.LargeStackThread.IsCurrent)
        {
            return SDK.Platform.LargeStackThread.Run(() => Plan(context, target));
        }

        if (NameOccurrences.SymbolOf(context, target.Procedure) is not { } procedure)
        {
            return Refused(new(ParameterRemovalRefusalKind.NoParameter, target.Name));
        }

        if (RefusalOf(context, procedure, target.Name) is { } refusal)
        {
            return Refused(refusal);
        }

        if (FamilyOf(context, procedure, target.Name) is not { } family)
        {
            return Refused(new(ParameterRemovalRefusalKind.Handler, procedure.Name));
        }

        var edits = new List<RenameEdit>();
        foreach (var member in family)
        {
            if (MemberEdits(context, member, target.Index, edits) is { } refused)
            {
                return Refused(refused);
            }
        }

        return new([.. edits
            .DistinctBy(edit => (edit.Module.AbsoluteUri, edit.Range))
            .OrderBy(edit => edit.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(edit => edit.Range.Start)], null);
    }

    private static ParameterRemovalPlan Refused(ParameterRemovalRefusal refusal) => new([], refusal);

    private static ParameterRemovalRefusal? RefusalOf(RenameContext context, Symbol procedure, string parameter) => procedure switch
    {
        _ when !NameOccurrences.IsDeclaredBy(context, procedure) => new(ParameterRemovalRefusalKind.NotDeclaredByWorkspace, procedure.Name),
        VBEventMemberSymbol => new(ParameterRemovalRefusalKind.Event, parameter, procedure.Name),
        VBExternalSubMemberSymbol or VBExternalFunctionMemberSymbol => new(ParameterRemovalRefusalKind.ExternalProcedure, parameter, procedure.Name),
        _ => null,
    };

    // the procedures whose parameter lists change together: the accessors of a property, and, for the member of a class that other classes implement, the
    // procedures that implement it (MS-VBAL §5.3.1.9). A procedure that implements a member changes with that member. None for a handler of an event, whose
    // parameters are the event's (MS-VBAL §5.3.1.8).
    internal static IReadOnlyList<Symbol>? FamilyOf(RenameContext context, Symbol procedure, string parameter)
    {
        var root = procedure;
        if (NameOccurrences.SymbolOf(context, new SemanticId(procedure.ParentUri)) is VBClassModuleSymbol owner && procedure.Name.Contains('_'))
        {
            if (ImplementedBy(owner, procedure) is { } implemented)
            {
                root = implemented;
            }
            else if (SymbolRename.IsNamedByConvention(context, owner, procedure))
            {
                return null;
            }
        }

        var family = RenameBinding.DefinedTogether(context, root).ToList();
        if (NameOccurrences.SymbolOf(context, new SemanticId(root.ParentUri)) is VBClassModuleSymbol rootOwner)
        {
            foreach (var implementer in ClassesOf(context).Where(candidate => candidate.ImplementedInterfaces.Any(each => each.SemanticId == rootOwner.SemanticId)))
            {
                foreach (var accessor in family.OfType<VBTypeMemberSymbol>().ToList())
                {
                    if (implementer.FindImplementation(rootOwner, accessor) is { } implementation)
                    {
                        family.AddRange(RenameBinding.DefinedTogether(context, implementation));
                    }
                }
            }
        }

        return [.. family.DistinctBy(member => member.SemanticId)];
    }

    // the member of an interface a procedure of a class implements, by its name (MS-VBAL §5.3.1.9).
    private static Symbol? ImplementedBy(VBClassModuleSymbol owner, Symbol procedure)
    {
        foreach (var implemented in owner.ImplementedInterfaces)
        {
            foreach (var member in implemented.DefaultInterfaceMembers)
            {
                if (owner.FindImplementation(implemented, member) is { } implementation && implementation.SemanticId == procedure.SemanticId)
                {
                    return member;
                }
            }
        }

        return null;
    }

    private static IEnumerable<VBClassModuleSymbol> ClassesOf(RenameContext context)
        => context.Modules.Select(candidate => NameOccurrences.SymbolOf(context, new SemanticId(candidate.Module))).OfType<VBClassModuleSymbol>();

    // the edits of one procedure: its parameter list, and every call to it. A Property Let or Set has the value last, which is not removed.
    private static ParameterRemovalRefusal? MemberEdits(RenameContext context, Symbol member, int index, List<RenameEdit> edits)
    {
        var parameters = ParametersOf(member);
        var count = member is VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol ? parameters.Length - 1 : parameters.Length;
        if (index >= count)
        {
            return new(ParameterRemovalRefusalKind.Value, index < parameters.Length ? parameters[index].Name : member.Name, member.Name);
        }

        var parameter = parameters[index];
        var at = NameOccurrences.ModuleOf(member.Uri);
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri) is not { } source
            || DeclarationOf(source, parameter) is not { } declaration)
        {
            return new(ParameterRemovalRefusalKind.NotDeclaredByWorkspace, member.Name);
        }

        if (declaration.Parameter.IsParamArray)
        {
            return new(ParameterRemovalRefusalKind.ParamArray, parameter.Name);
        }

        // a use of the parameter in its procedure, or a name written as it that could be one.
        var named = NameOccurrences.Find(context, parameter.Name, [source]);
        if (named.Occurrences.FirstOrDefault(occurrence => occurrence.Binding == parameter.SemanticId && occurrence.Kind is not OccurrenceKind.Declaration)
            is { Module: not null } use)
        {
            return new(ParameterRemovalRefusalKind.Used, parameter.Name, Module: use.Module, Position: use.Range.Start);
        }

        if (named.Unknown.FirstOrDefault(unknown => unknown.Kind is OccurrenceKind.Name && SymbolRename.Contains(declaration.Procedure.SourceLocation.Range, unknown.Range.Start))
            is { Module: not null } unresolved)
        {
            return new(ParameterRemovalRefusalKind.Unresolved, parameter.Name, Module: unresolved.Module, Position: unresolved.Range.Start);
        }

        var list = declaration.Procedure.ParameterListRange!.Value;
        edits.Add(EditOf(source.Module, ListEdits.RemoveItem(source.Source, Inside(list), index)));
        return CallEdits(context, member, parameter, index, edits);
    }

    // every call to the procedure: the argument passed for the parameter, by position or by name, is removed.
    private static ParameterRemovalRefusal? CallEdits(RenameContext context, Symbol member, VBParameterSymbol parameter, int index, List<RenameEdit> edits)
    {
        var (sites, problem) = CallSitesOf(context, member);
        if (problem is { } missed)
        {
            return missed.Kind switch
            {
                CallProblemKind.CalledByName => new(ParameterRemovalRefusalKind.CalledByName, parameter.Name, member.Name, missed.Module, missed.Position),
                CallProblemKind.LateBound => new(ParameterRemovalRefusalKind.LateBound, parameter.Name, member.Name, missed.Module, missed.Position),
                _ => new(ParameterRemovalRefusalKind.Unresolved, member.Name, Module: missed.Module, Position: missed.Position),
            };
        }

        foreach (var (source, callee, call, bindingOf) in sites)
        {
            var positional = call.Arguments.TakeWhile(argument => argument is not NamedArgumentNode).Count();
            var removed = index < positional ? index
                : call.Arguments.IndexOf(call.Arguments.OfType<NamedArgumentNode>().FirstOrDefault(argument => string.Equals(argument.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))!);
            if (removed < 0)
            {
                continue;
            }

            var argument = call.Arguments[removed] is NamedArgumentNode { Value: var value } ? value : call.Arguments[removed];
            if (!IsInert(context, argument, bindingOf))
            {
                return new(ParameterRemovalRefusalKind.Evaluated, parameter.Name, Module: source.Module, Position: argument.Location.Range.Start);
            }

            if (call.List is not { } list)
            {
                return new(ParameterRemovalRefusalKind.Unresolved, member.Name, Module: source.Module, Position: callee);
            }

            // a call left with no argument loses its parentheses (F (a), Call F(a), x = F(a)): F () is not a statement.
            var leftEmpty = !ListEdits.ItemsOf(source.Source, list).Where((item, at) => at != removed && item.End != item.Start).Any();
            edits.Add(leftEmpty && call.Parentheses is { } parentheses
                ? new RenameEdit(source.Module, parentheses, string.Empty)
                : EditOf(source.Module, ListEdits.RemoveItem(source.Source, list, removed)));
        }

        return null;
    }

    // every call to a procedure that passes it arguments, where the edit can find it; or the first place the edit could miss one: a call by the procedure's
    // name in a string (MS-VBAL §6.1.2.8.1.1), an access that may be late-bound (MS-VBAL §5.6.12), a name or a member access that is not resolved.
    internal static (IReadOnlyList<CallSite> Sites, CallProblem? Problem) CallSitesOf(RenameContext context, Symbol member)
    {
        var found = NameOccurrences.Find(context, member.Name, context.Modules);
        foreach (var unknown in found.Unknown)
        {
            if (unknown.Kind is OccurrenceKind.String)
            {
                if (RenameBinding.IsCalledBy(context, [member], unknown))
                {
                    return ([], new(CallProblemKind.CalledByName, unknown.Module, unknown.Range.Start));
                }

                continue;
            }

            if (unknown.Kind is OccurrenceKind.Member || RenameBinding.ChainIndex(unknown.Scope, member) < int.MaxValue)
            {
                return ([], new(CallProblemKind.Unresolved, unknown.Module, unknown.Range.Start));
            }
        }

        if (found.Occurrences.FirstOrDefault(occurrence => RenameBinding.LateBoundMatchOf(context, [member], occurrence) != RenameBinding.LateBoundMatch.None)
            is { Module: not null } lateBound)
        {
            return ([], new(CallProblemKind.LateBound, lateBound.Module, lateBound.Range.Start));
        }

        var sites = new List<CallSite>();
        foreach (var group in found.Occurrences
            .Where(occurrence => occurrence.Binding == member.SemanticId && occurrence.Kind is OccurrenceKind.Name or OccurrenceKind.Member && occurrence.Node is not null)
            .GroupBy(occurrence => occurrence.Module.AbsoluteUri))
        {
            var source = context.Modules.First(candidate => candidate.Module.AbsoluteUri == group.Key);
            var calls = CallsIn(source);
            var bindingOf = NameOccurrences.BindingsOf(context, source);
            foreach (var occurrence in group)
            {
                if (calls.TryGetValue(occurrence.Node!, out var call))
                {
                    sites.Add(new CallSite(source, occurrence.Range.Start, call, bindingOf));
                }
            }
        }

        return (sites, null);
    }

    // an argument whose evaluation does nothing but read a value (MS-VBAL §5.6.13.1): an omitted one, a literal, Me, or the name of a variable, a
    // parameter or a constant; a constant or a member of an Enum qualified by its module or its Enum. Anything else may call a procedure or raise an
    // error, which removing the argument would no longer do.
    internal static bool IsInert(RenameContext context, ExpressionNode argument, Func<ExpressionNode, SemanticId?> bindingOf) => argument switch
    {
        MissingArgumentNode or LiteralExpressionNode or InstanceExpressionNode => true,
        VBUnaryOperatorExpressionNode { Children: [LiteralExpressionNode] } => true,
        SimpleNameExpressionNode => bindingOf(argument) is { } binding && NameOccurrences.SymbolOf(context, binding)
            is VBParameterSymbol or VBLocalVariableSymbol or VBLocalConstantSymbol or VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol
                or VBConstantMemberSymbol or VBEnumConstMemberSymbol,
        MemberAccessExpressionNode access => (bindingOf(access) ?? bindingOf(access.Member)) is { } binding
            && NameOccurrences.SymbolOf(context, binding) is VBConstantMemberSymbol or VBEnumConstMemberSymbol,
        _ => false,
    };

    // a call: its arguments, the range of its list (none when the edit cannot tell where it is), and the range of its parentheses with the blanks before them,
    // when they can go with the last argument: not when the call's result is indexed (F(a)(1)).
    internal sealed record class Call(ImmutableArray<ExpressionNode> Arguments, SourceRange? List, SourceRange? Parentheses);

    // a call in a module: where it names what it calls, and what the names of its arguments are bound to.
    internal sealed record class CallSite(RenameModule Source, SourcePosition Callee, Call Call, Func<ExpressionNode, SemanticId?> BindingOf);

    internal enum CallProblemKind
    {
        CalledByName,

        LateBound,

        Unresolved,
    }

    // a place where an edit of the calls to a procedure could miss one.
    internal sealed record class CallProblem(CallProblemKind Kind, Uri Module, SourcePosition Position);

    // the calls of a module, by the node that names what they call: a name, or the member of a member access.
    private static Dictionary<SyntaxNode, Call> CallsIn(RenameModule source)
    {
        var lines = source.Source.Split('\n');
        var calls = new Dictionary<SyntaxNode, Call>(ReferenceEqualityComparer.Instance);
        foreach (var (node, parent) in NameOccurrences.Walk(source.Syntax))
        {
            Call? call = node switch
            {
                IndexExpressionNode { Arguments.IsDefaultOrEmpty: false } index => ParenthesizedCall(lines, index,
                    canLoseThem: parent is not IndexExpressionNode { Callee: var indexed } || !ReferenceEquals(indexed, index)),
                CallStatementNode { Arguments.IsDefaultOrEmpty: false } statement => new Call(statement.Arguments,
                    new SourceRange(statement.Callee.Location.Range.End, statement.Arguments[^1].Location.Range.End), null),
                _ => null,
            };

            if (call is null)
            {
                continue;
            }

            var callee = node is IndexExpressionNode called ? called.Callee : ((CallStatementNode)node).Callee;
            calls[callee] = call;
            if (callee is MemberAccessExpressionNode access)
            {
                calls[access.Member] = call;
            }
        }

        return calls;
    }

    // the list of an index expression is in the parentheses after its callee, on the callee's line (F(a), F (a)).
    private static Call ParenthesizedCall(string[] lines, IndexExpressionNode index, bool canLoseThem)
    {
        var after = index.Callee.Location.Range.End;
        var end = index.Location.Range.End;
        if (after.Line >= lines.Length || lines[after.Line].IndexOf('(', after.Character) is not (>= 0 and var open) || end.Character == 0)
        {
            return new Call(index.Arguments, null, null);
        }

        var list = Inside(new SourceRange(new SourcePosition(after.Line, open), end));
        return new Call(index.Arguments, list, canLoseThem ? new SourceRange(after, end) : null);
    }

    // the text between the parentheses of a list.
    internal static SourceRange Inside(SourceRange parenthesized)
        => new(new SourcePosition(parenthesized.Start.Line, parenthesized.Start.Character + 1), new SourcePosition(parenthesized.End.Line, parenthesized.End.Character - 1));

    internal static (MemberDeclarationNode Procedure, ParameterDeclarationNode Parameter)? DeclarationOf(RenameModule source, VBParameterSymbol parameter)
    {
        foreach (var procedure in source.Syntax.Children.OfType<MemberDeclarationNode>().Where(procedure => procedure.ParameterListRange is not null))
        {
            if (procedure.Children.OfType<ParameterDeclarationNode>().FirstOrDefault(declared => (declared.NameRange ?? declared.Location.Range).Start == parameter.SelectionRange.Start)
                is { } declaration)
            {
                return (procedure, declaration);
            }
        }

        return null;
    }

    internal static RenameEdit EditOf(Uri module, SDK.Platform.Protocol.RefactorTextEdit edit) => new(module, edit.Range, edit.NewText);

    // the parameters a procedure's declaration writes, in order: a class member's implicit Me, its parameter 0, is none of them.
    internal static ImmutableArray<VBParameterSymbol> ParametersOf(Symbol symbol)
    {
        ImmutableArray<VBParameterSymbol> parameters = symbol switch
        {
            VBReturningMemberSymbol member => member.Parameters,
            VBProcedureMemberSymbol member => member.Parameters,
            VBEventMemberSymbol member => member.Parameters,
            _ => [],
        };
        return parameters is [{ Name: SDK.Model.Tokens.Me }, ..] ? parameters.RemoveAt(0) : parameters;
    }
}

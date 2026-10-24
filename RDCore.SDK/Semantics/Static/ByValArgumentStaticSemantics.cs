using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// <strong>RD-VBAL §5.6.13.1</strong>: where an argument can be written <c>ByVal</c>, as <strong>MS-VBA</strong> compiles it (recorded in Excel), which is not
/// where <strong>MS-VBAL §5.6.13.1</strong> says - only before an argument of an external procedure.
/// </summary>
/// <remarks>
/// <para>
/// <c>ByVal</c> written before an argument says how the argument is passed, and MS-VBA takes it only where it is so passed already or where it means something:
/// </para>
/// <list type="bullet">
/// <item>before an argument of a <c>ByVal</c> parameter, of any procedure - the workspace's, a library's, a <c>Declare</c>'s - it is valid and changes
/// nothing (<see cref="ValueExpressionSemanticFlags.RedundantByVal"/>): what is passed is what would be, an object as much as a value;</item>
/// <item>before an argument of a <c>Declare</c>'s <c>ByRef</c> parameter, or of an <c>As Any</c> one, it is valid and passes the value where an address goes;</item>
/// <item>before an argument of a <c>ByRef</c> parameter of a procedure of the workspace or of a library (the type library declares every <c>Variant</c>
/// parameter by reference, <see cref="SymbolProperties.TypeLibraryByRef"/>), of a <c>ParamArray</c>, or of a member called late-bound, it is
/// <em>Type mismatch</em>; of a <c>ByRef</c> array parameter, <em>Array argument must be ByRef</em>;</item>
/// <item>before an argument of a function MS-VBA compiles inline (the reserved names, <c>InStr</c>, <c>InStrB</c>, <c>StrComp</c>), or before an index of
/// an array, it is a <em>Syntax error</em>.</item>
/// </list>
/// </remarks>
public static class ByValArgumentStaticSemantics
{
    // the functions MS-VBA parses with their arguments, and that take no ByVal argument (recorded in Excel), besides the reserved names.
    private static readonly ImmutableHashSet<string> _parsedInline = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "InStr", "InStrB", "StrComp");

    // what one argument written ByVal is: the ByVal node, the error it is, and whether it is valid and changes nothing.
    private readonly record struct Verdict(ByValArgumentExpressionNode Argument, VBCompileErrorInfo? Error, bool IsRedundant);

    /// <summary>
    /// The compile error of the first argument of a call that is written <c>ByVal</c> where <strong>MS-VBA</strong> refuses it, or <see langword="null"/>.
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against.</param>
    /// <param name="call">The call.</param>
    /// <param name="calleeType">The declared type of the callee: an array's index takes no <c>ByVal</c>.</param>
    public static VBCompileErrorInfo? Error(StaticEvaluationContext context, IndexExpressionNode call, VBType? calleeType)
        => Error(context, call.Callee, call.Arguments, calleeType);

    /// <summary>
    /// The compile error of the first argument of a call written as a statement, without parentheses (<c>Work ByVal x</c>), that is written <c>ByVal</c> where
    /// <strong>MS-VBA</strong> refuses it, or <see langword="null"/>.
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against.</param>
    /// <param name="callee">What the statement calls.</param>
    /// <param name="arguments">The arguments, as written.</param>
    /// <param name="calleeType">The declared type of the callee.</param>
    public static VBCompileErrorInfo? Error(StaticEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> arguments, VBType? calleeType)
        => VerdictsOf(context, callee, arguments, calleeType).Select(verdict => verdict.Error).FirstOrDefault(error => error is not null);

    /// <summary>
    /// Flags each argument of a call that is written <c>ByVal</c> for a parameter that is <c>ByVal</c> already (<see cref="ValueExpressionSemanticFlags.RedundantByVal"/>):
    /// a fact of the <c>ByVal</c> argument, where it is written, for an analyzer to say.
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against; nothing is recorded without its facts.</param>
    /// <param name="call">The call, whose arguments have been evaluated.</param>
    /// <param name="calleeType">The declared type of the callee.</param>
    public static void MarkRedundant(StaticEvaluationContext context, IndexExpressionNode call, VBType? calleeType)
        => MarkRedundant(context, call.Callee, call.Arguments, calleeType);

    /// <summary>
    /// Flags each argument of a call written as a statement, without parentheses, that is written <c>ByVal</c> for a parameter that is <c>ByVal</c> already
    /// (<see cref="ValueExpressionSemanticFlags.RedundantByVal"/>).
    /// </summary>
    /// <param name="context">The compile-time context the call is evaluated against; nothing is recorded without its facts.</param>
    /// <param name="callee">What the statement calls.</param>
    /// <param name="arguments">The arguments, as written.</param>
    /// <param name="calleeType">The declared type of the callee.</param>
    public static void MarkRedundant(StaticEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> arguments, VBType? calleeType)
    {
        if (context.Facts is not { } facts)
        {
            return;
        }

        foreach (var verdict in VerdictsOf(context, callee, arguments, calleeType).Where(verdict => verdict.IsRedundant))
        {
            var operandType = facts.TryGet(verdict.Argument.Operand.Identity, out var operand) ? operand.DeclaredType : null;
            facts.Record(new ExpressionFact(verdict.Argument.Identity, verdict.Argument.Location, operandType, ExpressionClassification.Value, null,
                ValueExpressionSemanticFlags.RedundantByVal));
        }
    }

    private static IEnumerable<Verdict> VerdictsOf(StaticEvaluationContext context, ExpressionNode callee, ImmutableArray<ExpressionNode> arguments, VBType? calleeType)
    {
        var byVals = arguments.Select((argument, index) => (Index: index, Name: (argument as NamedArgumentNode)?.Name,
                Node: argument as ByValArgumentExpressionNode ?? (argument as NamedArgumentNode)?.Value as ByValArgumentExpressionNode))
            .Where(argument => argument.Node is not null)
            .ToList();
        if (byVals.Count == 0)
        {
            yield break;
        }

        // a function MS-VBA compiles inline, or an index of an array: the parser takes no ByVal there.
        if (callee is SimpleNameExpressionNode { IdentifierName: var name } && (ReservedNameStaticSemantics.IsReservedName(name) || _parsedInline.Contains(name)))
        {
            foreach (var argument in byVals)
            {
                yield return new Verdict(argument.Node!, VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, argument.Node!.Location,
                    string.Format(Exceptions.VBArgument_ByValInlineFunction_Verbose, name)), false);
            }

            yield break;
        }

        // a variable or a constant of a module is a member of it, and no procedure: what it is called with is its index, or its default member's arguments.
        var member = ObjectValueStaticSemantics.MemberOfCallee(context, callee) is { Kind: not (SymbolKindExt.Variable or SymbolKindExt.Field or SymbolKindExt.Constant) } called
            ? called
            : null;
        if (member is null)
        {
            // what is called is no procedure the call knows: an array, whose index takes no ByVal, or a member called late-bound. Anything else is for the
            // rules of what it is to say.
            var (id, verbose) = calleeType is VBArrayType
                ? (VBCompileErrorId.SyntaxError, Exceptions.VBArgument_ByValIndex_Verbose)
                : IsLateBound(context, callee) ? (VBCompileErrorId.TypeMismatch, Exceptions.VBArgument_ByValLateBound_Verbose) : (default, null);
            if (verbose is null)
            {
                yield break;
            }

            foreach (var argument in byVals)
            {
                yield return new Verdict(argument.Node!, VBCompileErrorInfo.For(id, argument.Node!.Location, verbose), false);
            }

            yield break;
        }

        var parameters = ObjectValueStaticSemantics.ParametersOfCallee(context, callee)!.Value
            .Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef))
            .ToImmutableArray();
        var isExternal = member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol;
        var isLibrary = !isExternal && member.GetProperty(SymbolProperties.ExternalTarget) is not null;
        foreach (var argument in byVals)
        {
            if (ParameterOf(parameters, argument.Index, argument.Name) is not { } parameter)
            {
                continue;
            }

            yield return VerdictOf(argument.Node!, parameter, isExternal, isLibrary);
        }
    }

    private static Verdict VerdictOf(ByValArgumentExpressionNode argument, VBParameterSymbol parameter, bool isExternal, bool isLibrary)
    {
        var isByVal = parameter.ParameterKind is ParameterKind.ExplicitByVal;
        if (parameter is ParamArrayParameterSymbol)
        {
            return Refused(VBCompileErrorId.TypeMismatch, Exceptions.VBArgument_ByValParamArray_Verbose);
        }

        // As Any takes a value where an address goes, a Declare's ByRef parameter too: the keyword means something there.
        if ((isExternal || isLibrary) && parameter.ResolvedType is VBUnknownType || isExternal && !isByVal)
        {
            return new Verdict(argument, null, false);
        }

        if (isLibrary && parameter.GetProperty(SymbolProperties.TypeLibraryByRef) || !isExternal && !isLibrary && !isByVal)
        {
            return parameter.ResolvedType is VBArrayType && !isLibrary
                ? Refused(VBCompileErrorId.ArrayArgumentMustBeByRef, Exceptions.VBArgument_ByValArrayParameter_Verbose)
                : Refused(VBCompileErrorId.TypeMismatch, Exceptions.VBArgument_ByValByRefParameter_Verbose);
        }

        return new Verdict(argument, null, true);

        Verdict Refused(VBCompileErrorId id, string verbose)
            => new(argument, VBCompileErrorInfo.For(id, argument.Location, string.Format(verbose, parameter.Name)), false);
    }

    // the parameter an argument is for: the one named, or the one at its position - a ParamArray's for every position from its own on.
    private static VBParameterSymbol? ParameterOf(ImmutableArray<VBParameterSymbol> parameters, int index, string? name)
    {
        if (name is not null)
        {
            return parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        for (var position = 0; position < parameters.Length; position++)
        {
            if (parameters[position] is ParamArrayParameterSymbol || position == index)
            {
                return parameters[position];
            }
        }

        return null;
    }

    // a member of an object whose type does not say what it has, or a variable that holds one: its parameters are not known, and MS-VBA takes them to be
    // ByRef (recorded in Excel: `o.Add ByVal 1` on an Object is "Type mismatch").
    private static bool IsLateBound(StaticEvaluationContext context, ExpressionNode callee)
    {
        var owner = callee is MemberAccessExpressionNode { Owner: { } accessed } ? accessed : callee;
        return ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = null }, owner) is { IsSuccess: true, Result: VBObjectType or VBVariantType };
    }
}

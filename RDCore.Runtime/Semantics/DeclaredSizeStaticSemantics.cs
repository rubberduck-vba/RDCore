using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Static;
using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// The compile errors of the sizes a module's declarations are written with that only reducing its constants tells: the length of a fixed-length
/// <c>String * n</c> (<strong>MS-VBAL §5.2.3.1.4</strong>), and the bounds of a fixed-size array that name a constant (<strong>§5.2.3.1.3</strong>).
/// </summary>
/// <remarks>
/// Both are "Let-coerced to declared type Long" constant expressions, reduced here, when the module is loaded and its constants are folded, as MS-VBA
/// reduces them when it compiles (observed in Excel):
/// <list type="bullet">
/// <item>a length is 1 to 65526 characters, whether it is written as a number or given by a constant (<see cref="VBCompileErrorId.InvalidLengthForFixedLengthString"/>);
/// written as a number, it is an integer up to 65535, or no length at all (<see cref="VBCompileErrorId.SyntaxError"/>); a name that is no constant is
/// <see cref="VBCompileErrorId.ConstantExpressionRequired"/>, and one nothing declares <see cref="VBCompileErrorId.VariableNotDefined"/> under
/// <c>Option Explicit</c>.</item>
/// <item>a bound that names a constant is a <c>Long</c>, which a constant past its range is not (<see cref="VBCompileErrorId.Overflow"/>), and an upper bound
/// below the lower one leaves the dimension no element (<see cref="VBCompileErrorId.RangeHasNoValues"/>). A bound written as a literal, or that is no
/// constant expression, is <see cref="ConstantExpressionStaticSemantics"/>'s to report.</item>
/// </list>
/// </remarks>
public static class DeclaredSizeStaticSemantics
{
    /// <summary>
    /// The compile errors of the sizes a module's declarations are written with.
    /// </summary>
    /// <param name="session">The session the module is loaded in, whose constants are folded.</param>
    /// <param name="bounds">Reduces a constant expression to the <c>Long</c> it is.</param>
    /// <param name="module">The module.</param>
    /// <param name="regions">Each part of the module's syntax tree, and the scope its names resolve from: a declaration of the module, from the module;
    /// a procedure, from the procedure.</param>
    /// <param name="members">The members the module declares.</param>
    public static ImmutableArray<VBCompileErrorInfo> Check(
        IRuntimeSession session, ArrayBoundEvaluator bounds, Symbol module, IEnumerable<(SyntaxNode Region, Uri Scope)> regions, IReadOnlyList<VBTypeMemberSymbol> members)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        var isExplicit = module is VBModuleSymbol { Directives.Explicit: true };
        foreach (var (region, scope) in regions)
        {
            foreach (var asType in SelfAndDescendants(region).OfType<AsTypeExpressionNode>())
            {
                if (asType.FixedLength is { } length && LengthError(session, bounds, asType, length, scope, isExplicit) is { } error)
                {
                    errors.Add(error);
                }
            }
        }

        foreach (var array in ArraysOf(members))
        {
            errors.AddRange(BoundErrors(session, bounds, array));
            // MS-VBA bounds a field's arrays from 0, whatever the module's Option Base (RD-VBAL §5.2.1.2).
            var optionBase = array is VBUserDefinedTypeFieldSymbol ? 0 : module is VBModuleSymbol { Directives.Base: var moduleBase } ? moduleBase : 0;
            if (SizeError(session, bounds, array, optionBase) is { } size)
            {
                errors.Add(size);
            }
        }

        return errors.ToImmutable();
    }

    private static IEnumerable<SyntaxNode> SelfAndDescendants(SyntaxNode node) => [node, .. node.Descendants()];

    // what a module, its procedures and its types declare that can be declared with bounds.
    private static IEnumerable<Symbol> ArraysOf(IReadOnlyList<VBTypeMemberSymbol> members)
    {
        foreach (var member in members)
        {
            yield return member;

            var locals = member switch
            {
                VBReturningMemberSymbol returning => returning.Locals,
                VBProcedureMemberSymbol procedure => procedure.Locals,
                _ => [],
            };

            foreach (var local in locals)
            {
                yield return local;
            }

            foreach (var field in member is VBUserDefinedTypeMemberSymbol { Members.IsDefault: false } userDefinedType ? userDefinedType.Members : [])
            {
                yield return field;
            }
        }
    }

    private static VBCompileErrorInfo? LengthError(
        IRuntimeSession session, ArrayBoundEvaluator bounds, AsTypeExpressionNode asType, string length, Uri scope, bool isExplicit)
    {
        var location = asType.Location;
        if (char.IsAsciiDigit(length[0]) || length[0] == '&')
        {
            return !VBFixedStringType.TryParseLengthLiteral(length, out var written) || written > VBFixedStringType.MaxWrittenLength
                ? VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, location, string.Format(Exceptions.VBCompileError_FixedLengthNotALength_Verbose, length))
                : InvalidLength(written, length, location);
        }

        var name = new SimpleNameExpressionNode(asType.Identity, location, length);
        var named = session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol;
        if (named is null)
        {
            return isExplicit
                ? VBCompileErrorInfo.For(VBCompileErrorId.VariableNotDefined, location, length)
                : VBCompileErrorInfo.For(VBCompileErrorId.ConstantExpressionRequired, location,
                    string.Format(Exceptions.VBCompileError_FixedLengthNotAConstant_Verbose, length));
        }

        if (named is not (VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol))
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.ConstantExpressionRequired, location,
                string.Format(Exceptions.VBCompileError_FixedLengthNotAConstant_Verbose, length));
        }

        // the constant's own error - a value that does not fold - is the constant's to report, where it is declared.
        return bounds.TryEvaluate(session, new RuntimeEvaluationContext(scope), name, out var characters, out var failure)
            ? InvalidLength(characters, length, location)
            : failure.ErrorInfo?.ErrorId == (int)VBRuntimeErrorId.Overflow
                ? VBCompileErrorInfo.For(VBCompileErrorId.InvalidLengthForFixedLengthString, location,
                    string.Format(Exceptions.VBCompileError_InvalidLengthForFixedLengthString_Verbose, length))
                : null;
    }

    private static VBCompileErrorInfo? InvalidLength(long characters, string length, SourceLocation location)
        => VBFixedStringType.IsValidLength(characters)
            ? null
            : VBCompileErrorInfo.For(VBCompileErrorId.InvalidLengthForFixedLengthString, location,
                string.Format(Exceptions.VBCompileError_InvalidLengthForFixedLengthString_Verbose, length));

    // the bounds of an array that name a constant, reduced: one past the range of a Long is an overflow, an upper bound below the lower one no range.
    private static IEnumerable<VBCompileErrorInfo> BoundErrors(IRuntimeSession session, ArrayBoundEvaluator bounds, Symbol array)
    {
        if (!array.TryGetProperty(SymbolProperties.ArrayBounds, out var declared) || declared.IsDefaultOrEmpty)
        {
            yield break;
        }

        var resolver = session.Symbols.Resolver;
        var context = new RuntimeEvaluationContext(array.ParentUri);
        foreach (var bound in declared)
        {
            ExpressionNode[] expressions = [.. new[] { bound.LowerExpression, bound.UpperExpression }.OfType<ExpressionNode>()];
            if (expressions.All(IsLiteral) || !expressions.All(expression => IsReducible(resolver, array, expression)))
            {
                continue;
            }

            int? lower = null;
            int? upper = null;
            VBCompileErrorInfo? overflow = null;
            foreach (var expression in expressions)
            {
                if (!bounds.TryEvaluate(session, context, expression, out var value, out var failure))
                {
                    overflow ??= failure.ErrorInfo?.ErrorId == (int)VBRuntimeErrorId.Overflow
                        ? VBCompileErrorInfo.For(VBCompileErrorId.Overflow, expression.Location, string.Format(Exceptions.VBCompileError_ArrayBoundOverflow_Verbose, array.Name))
                        : null;
                    continue;
                }

                if (ReferenceEquals(expression, bound.UpperExpression))
                {
                    upper = value;
                }
                else
                {
                    lower = value;
                }
            }

            if (overflow is not null)
            {
                yield return overflow;
                continue;
            }

            if (bound.UpperExpression is { } upperExpression && upper is { } upperValue && (bound.LowerExpression is null ? upperValue < 0 : lower is { } lowerValue && upperValue < lowerValue))
            {
                yield return VBCompileErrorInfo.For(VBCompileErrorId.RangeHasNoValues, upperExpression.Location, array.Name);
            }
        }
    }

    // an array of more elements than RD-VBA holds (VBArrayValue.MaximumLength) is out of memory, its bounds reduced: one whose bounds are all literals is
    // ConstantExpressionStaticSemantics's to report. A dimension with no lower bound starts at the Option Base, as the array the runtime makes of it does.
    private static VBCompileErrorInfo? SizeError(IRuntimeSession session, ArrayBoundEvaluator bounds, Symbol array, int optionBase)
    {
        if (!array.TryGetProperty(SymbolProperties.ArrayBounds, out var declared) || declared.IsDefaultOrEmpty
            || declared.All(bound => new[] { bound.LowerExpression, bound.UpperExpression }.OfType<ExpressionNode>().All(IsLiteral))
            || !declared.SelectMany(bound => new[] { bound.LowerExpression, bound.UpperExpression }.OfType<ExpressionNode>())
                .All(expression => IsReducible(session.Symbols.Resolver, array, expression))
            || declared[^1].UpperExpression is not { } last)
        {
            return null;
        }

        var context = new RuntimeEvaluationContext(array.ParentUri);
        var dimensions = new List<(int, int)>();
        foreach (var bound in declared)
        {
            var lower = optionBase;
            if (bound.LowerExpression is { } lowerExpression && !bounds.TryEvaluate(session, context, lowerExpression, out lower, out _)
                || bound.UpperExpression is not { } upperExpression || !bounds.TryEvaluate(session, context, upperExpression, out var upper, out _))
            {
                return null;
            }

            dimensions.Add((lower, upper));
        }

        return VBArrayValue.LengthOf(dimensions) > VBArrayValue.MaximumLength
            ? VBCompileErrorInfo.For(VBCompileErrorId.OutOfMemory, last.Location,
                string.Format(Exceptions.VBCompileError_OutOfMemory_ArrayElements_Verbose, array.Name, VBArrayValue.MaximumLength))
            : null;
    }

    // whether a bound of an array is one this check reduces: a constant expression. One that is not is "Constant expression required"
    // (ConstantExpressionStaticSemantics), and has no value until the code runs; one kept as text (VBC00060, RD-VBAL 2.6.1) is not analyzed.
    private static bool IsReducible(IRuntimeSymbolResolver resolver, Symbol array, ExpressionNode expression)
        => expression is not UnbuiltExpressionTriviaNode && !expression.Descendants().OfType<UnbuiltExpressionTriviaNode>().Any()
            && ConstantExpressionStaticSemantics.ValueError(resolver, array.ParentUri, expression) is null;

    private static bool IsLiteral(ExpressionNode expression) => expression is LiteralExpressionNode
        or VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode };
}

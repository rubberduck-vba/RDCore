using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static;

/// <summary>
/// Whether the value of a constant is a constant expression (<strong>MS-VBAL §5.6.16.1</strong>): one that is evaluated once, statically, as MS-VBA
/// evaluates it (recorded in Excel).
/// </summary>
/// <remarks>
/// <para>
/// A constant expression is made of literals, constants - of the module, of the procedure, of a library - members of enumerations, parentheses, and the
/// operators; anything else is <see cref="VBCompileErrorId.ConstantExpressionRequired"/>.
/// </para>
/// <para>
/// 👉 MS-VBAL §5.6.16.1 also admits a call of <c>Int</c>, <c>Fix</c>, <c>Abs</c>, <c>Sgn</c>, <c>Len</c>, <c>LenB</c> and the conversion functions. MS-VBA
/// refuses them in a constant (<c>Const k = Len("abc")</c>, <c>Abs(-1)</c>, <c>CLng(1.5)</c>; of a module, oracle\s44 C01, C02, and <c>LenB</c> of a
/// procedure, C05), and RD-VBA refuses them as MS-VBA does. A <c>Const</c> of a procedure valued <c>Len</c> of a constant compiles in MS-VBA, and crashes the
/// host when it runs (C03, C04): RD-VBA compiles it, to the length MS-VBAL gives it, and warns (<c>RDC11021</c>, the author's ruling). MS-VBA admits
/// <c>Len</c> and <c>LenB</c> of a constant expression in the bounds of an array, of a
/// procedure and of a module (<c>Dim d(Len(K))</c>, oracle\s46 F02, oracle\s44 C06), and so does RD-VBA.
/// </para>
/// <para>
/// The <c>Nothing</c> literal is a constant expression, of a type no constant can have: <see cref="VBCompileErrorId.InvalidDataTypeForConstant"/>.
/// </para>
/// </remarks>
public static class ConstantExpressionStaticSemantics
{
    /// <summary>
    /// The compile errors of the values of the constants a module declares, and of the constants its procedures declare.
    /// </summary>
    /// <param name="members">The members of the module.</param>
    /// <param name="resolver">Resolves the names a value is written with, from where the constant is declared.</param>
    /// <param name="optionBase">The module's <c>Option Base</c>: the lower bound of an array dimension declared without one, but of a field of a type.</param>
    internal static IEnumerable<VBCompileErrorInfo> Evaluate(IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver, int optionBase = 0)
    {
        foreach (var member in members)
        {
            if (member is VBConstantMemberSymbol { Value: { } value }
                && (ValueError(resolver, member.ParentUri, value) ?? CycleError(resolver, member, value)) is { } moduleError)
            {
                yield return moduleError;
            }

            foreach (var boundError in BoundErrors(resolver, member, optionBase))
            {
                yield return boundError;
            }

            // a field of a user-defined type that is an array is declared with bounds as a variable is (MS-VBA, Excel-verified, oracle\s29 A09, A10).
            foreach (var field in member is VBUserDefinedTypeMemberSymbol { Members.IsDefault: false } userDefinedType ? userDefinedType.Members : [])
            {
                // MS-VBA bounds a field's arrays from 0, whatever the module's Option Base (RD-VBAL §5.2.1.2).
                foreach (var boundError in BoundErrors(resolver, field, optionBase: 0))
                {
                    yield return boundError;
                }
            }

            foreach (var local in DeclarationStaticSemanticsEvaluator.LocalsOf(member))
            {
                if (local is VBLocalConstantSymbol { Value: { } localValue }
                    && (ValueErrorOf(resolver, local, localValue) ?? CycleError(resolver, local, localValue)) is { } localError)
                {
                    yield return localError;
                }

                foreach (var boundError in BoundErrors(resolver, local, optionBase))
                {
                    yield return boundError;
                }
            }
        }
    }

    // a constant whose value names itself - directly, or through the constants it names - has no value to be folded to: "Constant expression required", as
    // MS-VBA reports it however the cycle is made, in a module or in a procedure, and whether or not anything reads the constant (Excel-verified, oracle\s28
    // C01-C05). Each constant on the cycle is one.
    private static VBCompileErrorInfo? CycleError(ISymbolResolver resolver, Symbol constant, ExpressionNode value)
    {
        var start = constant.Uri.AbsoluteUri;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(Uri Scope, ExpressionNode Value)>();
        pending.Push((constant.ParentUri, value));
        while (pending.TryPop(out var next))
        {
            foreach (var named in ConstantsNamedBy(resolver, next.Scope, next.Value))
            {
                if (named.Uri.AbsoluteUri == start)
                {
                    return VBCompileErrorInfo.For(VBCompileErrorId.ConstantExpressionRequired, value.Location, $"the value of {constant.Name} names {constant.Name} itself");
                }

                if (visited.Add(named.Uri.AbsoluteUri) && ValueOf(named) is { } namedValue)
                {
                    pending.Push((named.ParentUri, namedValue));
                }
            }
        }

        return null;
    }

    private static ExpressionNode? ValueOf(Symbol constant) => constant switch
    {
        VBConstantMemberSymbol { Value: var value } => value,
        VBLocalConstantSymbol { Value: var value } => value,
        _ => null,
    };

    // the constants a constant expression names, by their names or through their modules.
    private static IEnumerable<Symbol> ConstantsNamedBy(ISymbolResolver resolver, Uri scope, ExpressionNode expression)
    {
        switch (expression)
        {
            case VBUnaryOperatorExpressionNode unary:
                foreach (var named in ConstantsNamedBy(resolver, scope, unary.Operand))
                {
                    yield return named;
                }

                break;
            case VBBinaryOperatorExpressionNode binary:
                foreach (var named in ConstantsNamedBy(resolver, scope, binary.Left).Concat(ConstantsNamedBy(resolver, scope, binary.Right)))
                {
                    yield return named;
                }

                break;
            case SimpleNameExpressionNode name when resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol is VBConstantMemberSymbol or VBLocalConstantSymbol:
                yield return resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol!;
                break;
            case MemberAccessExpressionNode { Owner: { } owner } access when resolver.NamespaceOf(owner, scope) is { } qualifier
                && resolver.ResolveMember(qualifier, access.Member, scope).Symbol is VBConstantMemberSymbol member:
                yield return member;
                break;
        }
    }

    // MS-VBAL §5.2.3.1.3: the bounds an array is declared with are constant expressions (MS-VBA: "Constant expression required", Excel-verified).
    private static IEnumerable<VBCompileErrorInfo> BoundErrors(ISymbolResolver resolver, Symbol array, int optionBase)
    {
        if (!array.TryGetProperty(SymbolProperties.ArrayBounds, out var bounds) || bounds.IsDefaultOrEmpty)
        {
            yield break;
        }

        foreach (var bound in bounds)
        {
            foreach (var expression in new[] { bound.LowerExpression, bound.UpperExpression }.OfType<ExpressionNode>())
            {
                if (FirstNotConstant(resolver, array.ParentUri, expression, IntrinsicCalls.Len | IntrinsicCalls.LenB, reporting: true) is { } offending)
                {
                    yield return VBCompileErrorInfo.For(VBCompileErrorId.ConstantExpressionRequired, offending.Location, Describe(offending) ?? string.Empty);
                }
            }

            // a bound is a Long (MS-VBAL §5.2.3.1.3: "Let-coerced to Long"): one past its range is an overflow, before any range is looked at (MS-VBA: "Overflow",
            // Excel-verified, oracle\s29 A13, A25-A27).
            var outOfRange = new[] { bound.LowerExpression, bound.UpperExpression }.OfType<ExpressionNode>()
                .FirstOrDefault(expression => LiteralValue(expression) is { } value && (value < int.MinValue || value > int.MaxValue));
            if (outOfRange is not null)
            {
                yield return VBCompileErrorInfo.For(VBCompileErrorId.Overflow, outOfRange.Location,
                    string.Format(Exceptions.VBCompileError_ArrayBoundOverflow_Verbose, array.Name));
                continue;
            }

            // an upper bound less than the lower one leaves the dimension no element (MS-VBA: "Range has no values", Excel-verified). Bounds written as
            // literals are known here; with no lower bound, Option Base says what it is, and an upper bound below zero is less than either.
            if (bound.UpperExpression is { } upper && LiteralValue(upper) is { } upperValue
                && (bound.LowerExpression is { } lower ? LiteralValue(lower) is { } lowerValue && upperValue < lowerValue : upperValue < 0))
            {
                yield return VBCompileErrorInfo.For(VBCompileErrorId.RangeHasNoValues, upper.Location, array.Name);
            }
        }

        // RD-VBA holds each element in a cell of its own, and an array of more than VBArrayValue.MaximumLength of them is out of memory, as a ReDim of one is
        // when it runs (RD-VBAL §5.4.3.3): MS-VBA (64-bit) allocates `Dim b(100000000) As Byte` (recorded in Excel: oracle\s56 C12). Bounds written as literals
        // are known here; a dimension with no lower bound starts at the module's Option Base, as the array the runtime makes of it does.
        var literal = bounds.Select(bound => (Lower: bound.LowerExpression is { } lower ? LiteralValue(lower) : optionBase, Upper: bound.UpperExpression is { } upper ? LiteralValue(upper) : null))
            .ToArray();
        if (literal.All(bound => bound is { Lower: >= int.MinValue and <= int.MaxValue, Upper: >= int.MinValue and <= int.MaxValue })
            && VBArrayValue.LengthOf(literal.Select(bound => ((int)bound.Lower!.Value, (int)bound.Upper!.Value))) > VBArrayValue.MaximumLength
            && bounds[^1].UpperExpression is { } last)
        {
            yield return VBCompileErrorInfo.For(VBCompileErrorId.OutOfMemory, last.Location,
                string.Format(Exceptions.VBCompileError_OutOfMemory_ArrayElements_Verbose, array.Name, VBArrayValue.MaximumLength));
        }
    }

    // the value of a numeric literal, or of a negated one.
    private static double? LiteralValue(ExpressionNode expression) => expression switch
    {
        LiteralExpressionNode { StaticValue: VBNumericTypedValue number } => number.AsDouble,
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode { StaticValue: VBNumericTypedValue negated } } => -negated.AsDouble,
        _ => null,
    };

    /// <summary>
    /// The compile error of the value of a constant: the first part of it that is not a constant expression, or a value of no type a constant can have.
    /// </summary>
    /// <remarks>
    /// A value that has one is not evaluated: what it would read - a variable, a function's result - has no value until the code runs.
    /// </remarks>
    /// <param name="resolver">Resolves the names the value is written with.</param>
    /// <param name="scope">Where the constant is declared: its module, or its procedure.</param>
    /// <param name="value">The value the constant is declared with.</param>
    /// <returns>The error, or <see langword="null"/> when the value is a constant expression.</returns>
    public static VBCompileErrorInfo? ValueError(ISymbolResolver resolver, Uri scope, ExpressionNode value) => ValueError(resolver, scope, value, IntrinsicCalls.None);

    /// <summary>
    /// The compile error of the value of <paramref name="constant"/>, as <see cref="ValueError(ISymbolResolver, Uri, ExpressionNode)"/> - and of a constant
    /// of a procedure, a call of the standard library's <c>Len</c> of a constant expression is one, which MS-VBA compiles there and refuses in a module
    /// (recorded in Excel: oracle\s44 C01-C04).
    /// </summary>
    /// <param name="resolver">Resolves the names the value is written with.</param>
    /// <param name="constant">The constant: of a module, or of a procedure.</param>
    /// <param name="value">The value it is declared with.</param>
    /// <returns>The error, or <see langword="null"/> when the value is a constant expression.</returns>
    public static VBCompileErrorInfo? ValueErrorOf(ISymbolResolver resolver, Symbol constant, ExpressionNode value)
        => ValueError(resolver, constant.ParentUri, value, constant is VBLocalConstantSymbol ? IntrinsicCalls.Len : IntrinsicCalls.None);

    private static VBCompileErrorInfo? ValueError(ISymbolResolver resolver, Uri scope, ExpressionNode value, IntrinsicCalls admits)
    {
        if (value is LiteralExpressionNode { StaticValue: VBNothingValue })
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.InvalidDataTypeForConstant, value.Location, "Nothing");
        }

        return FirstNotConstant(resolver, scope, value, admits, reporting: true) is { } offending
            ? VBCompileErrorInfo.For(VBCompileErrorId.ConstantExpressionRequired, offending.Location, Describe(offending) ?? string.Empty)
            : null;
    }

    /// <summary>
    /// Whether <paramref name="expression"/> is a constant expression (<strong>MS-VBAL §5.6.16.1</strong>), which MS-VBA evaluates once, when it compiles
    /// the code: made of literals, constants, members of enumerations, parentheses, the operators, and calls of the standard library's <c>Int</c>,
    /// <c>Fix</c>, <c>Abs</c>, <c>Sgn</c>, <c>Len</c>, <c>LenB</c> and conversion functions.
    /// </summary>
    /// <remarks>
    /// The value of a <c>Const</c> admits fewer of those calls (<see cref="ValueError(ISymbolResolver, Uri, ExpressionNode)"/>). Where code is run, MS-VBA
    /// evaluates them all as it compiles it: <c>-CLng(&amp;H80000000)</c> and <c>Abs(CLng(-2147483647) - 1)</c> are the least <c>Long</c>, as a negation
    /// of one evaluated when the code runs never is (recorded in Excel: oracle\s60 A01, N17).
    /// </remarks>
    /// <param name="resolver">Resolves the names the expression is written with.</param>
    /// <param name="scope">Where the expression is written: a module, or a procedure.</param>
    /// <param name="expression">The expression.</param>
    public static bool IsConstantExpression(ISymbolResolver resolver, Uri scope, ExpressionNode expression)
        => FirstNotConstant(resolver, scope, expression, IntrinsicCalls.All) is null;

    // whether an expression is a constant expression, where what is known of the expressions of its tree is kept: an expression asked of after those it is
    // made of looks no further than them, so that asking of every call of a nested call takes time that grows with its depth, not with its square.
    internal static bool IsConstantExpression(ISymbolResolver resolver, Uri scope, ExpressionNode expression, Dictionary<ExpressionNode, bool> known)
    {
        if (!known.TryGetValue(expression, out var isConstant))
        {
            known[expression] = isConstant = FirstNotConstant(resolver, scope, expression, IntrinsicCalls.All, known) is null;
        }

        return isConstant;
    }

    // which calls of the standard library's functions MS-VBAL §5.6.16.1 names are constant expressions where they are written. MS-VBA admits Len and LenB
    // in the bounds of an array (Excel-verified, oracle\s46 F02a, F02b, oracle\s44 C06: `Dim d(Len(K))`), Len alone in the value of a Const of a procedure
    // (C03, C04, which the author lets slip with RDC11021: MS-VBA compiles it, and its run crashes the host), and none in the value of a Const of a module
    // (C01, C02), nor LenB in a procedure's (C05). Where code is run, it evaluates every one of them as it compiles the code (oracle\s60).
    [Flags]
    private enum IntrinsicCalls
    {
        None = 0,
        Len = 1 << 0,
        LenB = 1 << 1,
        // Int, Fix, Abs, Sgn, and the conversion functions.
        Others = 1 << 2,
        All = Len | LenB | Others,
    }

    private static readonly ImmutableHashSet<string> OtherIntrinsicFunctions = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "Int", "Fix", "Abs", "Sgn", "CBool", "CByte", "CCur", "CDate", "CDbl", "CInt", "CLng", "CLngLng", "CLngPtr", "CSng", "CStr", "CVar");

    // known: whether each expression already asked of is a constant expression, with every call admitted; one that is not is its own first.
    // reporting: what is asked is the part a compile error is reported at. An expression kept as text (VBC00060, RD-VBAL 2.6.1) is not analyzed, so no part
    // of it is reported; it is no constant expression all the same, and is never folded.
    private static ExpressionNode? FirstNotConstant(ISymbolResolver resolver, Uri scope, ExpressionNode expression, IntrinsicCalls admits = IntrinsicCalls.None,
        Dictionary<ExpressionNode, bool>? known = null, bool reporting = false) => expression switch
    {
        _ when known is not null && known.TryGetValue(expression, out var isConstant) => isConstant ? null : expression,
        LiteralExpressionNode => null,
        UnbuiltExpressionTriviaNode => reporting ? null : expression,
        // parentheses, - and Not, of a constant expression.
        VBUnaryOperatorExpressionNode unary => FirstNotConstant(resolver, scope, unary.Operand, admits, known, reporting),
        VBBinaryOperatorExpressionNode binary
            => FirstNotConstant(resolver, scope, binary.Left, admits, known, reporting) ?? FirstNotConstant(resolver, scope, binary.Right, admits, known, reporting),
        IndexExpressionNode { Arguments: [var argument] } call when admits != IntrinsicCalls.None && IsAdmittedFunction(resolver, scope, call.Callee, admits)
            => FirstNotConstant(resolver, scope, argument, admits, known, reporting),
        SimpleNameExpressionNode name => IsConstant(resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol) ? null : name,
        MemberAccessExpressionNode { Owner: { } owner } access => resolver.NamespaceOf(owner, scope) is { } qualifier
            ? IsConstant(resolver.ResolveMember(qualifier, access.Member, scope).Symbol) ? null : access
            : IsMemberOfEnumeration(resolver, scope, owner, access.Member.IdentifierName) ? null : access,
        _ => expression,
    };

    private static bool IsConstant(Symbol? symbol) => symbol is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol;

    // a function of the standard library that MS-VBAL §5.6.16.1 names - those admitted - by its name or through its module (`VBA.Len`): not a procedure
    // of the workspace named the same.
    private static bool IsAdmittedFunction(ISymbolResolver resolver, Uri scope, ExpressionNode callee, IntrinsicCalls admits)
    {
        var symbol = callee switch
        {
            SimpleNameExpressionNode name => resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when resolver.NamespaceOf(owner, scope) is { } qualifier
                => resolver.ResolveMember(qualifier, access.Member, scope).Symbol,
            _ => null,
        };

        return symbol is VBFunctionMemberSymbol function && RDCore.SDK.Runtime.StdLib.StdLibSymbolProvider.IsLibraryMember(function)
            && (admits.HasFlag(IntrinsicCalls.Len) && string.Equals(function.Name, "Len", StringComparison.OrdinalIgnoreCase)
                || admits.HasFlag(IntrinsicCalls.LenB) && string.Equals(function.Name, "LenB", StringComparison.OrdinalIgnoreCase)
                || admits.HasFlag(IntrinsicCalls.Others) && OtherIntrinsicFunctions.Contains(function.Name));
    }

    // `Color.Red`, or `Module1.Color.Red`: a member of the enumeration the owner names.
    private static bool IsMemberOfEnumeration(ISymbolResolver resolver, Uri scope, ExpressionNode owner, string name)
    {
        var named = owner switch
        {
            SimpleNameExpressionNode simple => resolver.ResolveValue(simple, ScopeKind.Local, scope).Symbol,
            MemberAccessExpressionNode { Owner: { } namespaceOwner } access when resolver.NamespaceOf(namespaceOwner, scope) is { } qualifier
                => resolver.ResolveMember(qualifier, access.Member, scope).Symbol,
            _ => null,
        };

        return named is VBEnumMemberSymbol { ResolvedType: VBEnumType enumeration }
            && enumeration.Members.Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Describe(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name => name.IdentifierName,
        MemberAccessExpressionNode access => access.Member.IdentifierName,
        IndexExpressionNode { Callee: var callee } => Describe(callee),
        _ => null,
    };
}

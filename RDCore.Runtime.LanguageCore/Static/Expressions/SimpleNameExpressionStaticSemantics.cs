using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.StdLib;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

// MS-VBAL §5.6.10; RD-VBAL §2.3.1.2
public sealed record class SimpleNameExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<SimpleNameExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static SimpleNameExpressionStaticSemantics Instance => _instance.Value;

    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not SimpleNameExpressionNode simpleName)
        {
            throw new ArgumentException($"Expected a {nameof(SimpleNameExpressionNode)}.", nameof(expression));
        }

        var result = context.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope.Uri);
        if (result.IsError)
        {
            return StaticSemanticsEvaluationResult.Error(GetResolutionErrorInfo(expression, simpleName.WrittenName, result.ErrorId!.Value, result.Candidates));
        }

        if (result.IsResolved)
        {
            if (TypeDeclarationCharacterMismatch(simpleName, result.Symbol!) is { } mismatch)
            {
                return StaticSemanticsEvaluationResult.Error(mismatch);
            }

            // a project, a procedural module and an enumeration only qualify the names of what they declare (MS-VBAL §5.6.12, which classifies them before
            // they are ever evaluated as an owner): by itself each is no value, variable or procedure (MS-VBA: "Expected variable or procedure, not
            // module", "not project", "not enum type", Excel-verified, oracle\s29).
            if (NameClassificationStaticSemantics.NotAValueError(result.Symbol!, expression.Location, simpleName.WrittenName) is { } notAValue)
            {
                return StaticSemanticsEvaluationResult.Error(notAValue);
            }

            // the host's Debug object is only ever written as the statements that are its own, Debug.Print and Debug.Assert (DebugStatementNode, which no
            // simple name of it is in): anywhere else - a value, the target of a Let or a Set, the target of a With block, `Call Debug.Print(…)` - MS-VBA reads
            // the name as a syntax error (Excel-verified, oracle\s18 D09, D11-D17).
            if (StandardLibrary.IsDebugObject(result.Symbol!))
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, expression.Location, simpleName.WrittenName));
            }

            return StaticSemanticsEvaluationResult.Success(result.Symbol is ITypedSymbol typed ? typed.ResolvedType : VBUnknownType.TypeInfo);
        }

        // a name in brackets (MS-VBAL §3.3.5.3) that nothing declares is the text of an expression for the host application to evaluate, as [A1] is in
        // Excel, and never an implicit variable, Option Explicit or not: RD-VBA hosts no application to evaluate it.
        if (simpleName.IsBracketed)
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.NoHostApplicationToEvaluateExpression, expression.Location,
                string.Format(Exceptions.VBCompileError_NoHostApplicationToEvaluateExpression_Verbose, simpleName.IdentifierName)));
        }

        // unbound: MS-VBAL 5.6.10 leaves this to Option Explicit (RD-VBAL §5.2.1.3). Under Explicit
        // it's a compile error; otherwise it's IVBInferableType's job to narrow the type from use —
        // this rule only ever answers VBUnknownType.
        return context.Scope.EnclosingModuleDirectives()?.Explicit == true
            ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.VariableNotDefined, expression.Location, simpleName.WrittenName))
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    // A type-declaration character says the type of the name it is written on, and the name was declared as a type of its own: <c>x$</c> of a variable declared
    // As Long is an error, at the name (and not at the statement it is in). A name that was found as written - Left$ is a member of the library, with the
    // character in its name - was never declared with the character, and says nothing about it. What is not known yet (a type still to be inferred) cannot
    // be told apart from the character, and is not. A function of the library whose name is reserved (Len, CVar, Abs, CInt ...) takes any character, which
    // MS-VBA ignores: Len$("abc") is 3, a Long, and CVar$(5) is 5, an Integer (recorded in Excel: oracle\s46 F18, oracle\s44 D01-D12, D16-D18), where an
    // ordinary function of the library refuses it (Asc$, Sqr$: D14, D15).
    private static VBCompileErrorInfo? TypeDeclarationCharacterMismatch(SimpleNameExpressionNode name, Symbol symbol)
    {
        if (name.TypeHint is not { Length: > 0 } hint
            || string.Equals(symbol.Name, name.WrittenName, StringComparison.OrdinalIgnoreCase)
            || IsReservedFunctionOfTheLibrary(name, symbol)
            || symbol is not ITypedSymbol { ResolvedType: var declared }
            || declared is VBUnknownType
            || !IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted)
            || hinted.Equals(declared))
        {
            return null;
        }

        return VBCompileErrorInfo.For(VBCompileErrorId.TypeDeclarationCharacterDoesNotMatch, name.Location,
            $"'{name.WrittenName}': '{hint}' is {hinted.Name}, and '{name.IdentifierName}' is declared as {declared.Name}.");
    }

    private static bool IsReservedFunctionOfTheLibrary(SimpleNameExpressionNode name, Symbol symbol)
        => ReservedNameStaticSemantics.IsReservedName(name.IdentifierName) && RDCore.SDK.Runtime.StdLib.StandardLibrary.IsLibraryMember(symbol);

    internal static VBCompileErrorInfo GetResolutionErrorInfo(
        ExpressionNode expression, string name, VBCompileErrorId errorId, ImmutableArray<Symbol> candidates)
        => VBCompileErrorInfo.For(errorId, expression.Location,
            $"'{name}' — {candidates.Length} candidates: {string.Join(", ", candidates.Select(candidate => SymbolUri.QualifiedNameOf(candidate.ParentUri)))}");
}

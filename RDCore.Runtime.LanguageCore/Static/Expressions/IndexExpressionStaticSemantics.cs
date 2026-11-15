using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK;
using RDCore.Runtime.LanguageCore.Errors;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

// MS-VBAL §5.6.13
public sealed record class IndexExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<IndexExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    public static IndexExpressionStaticSemantics Instance => _instance.Value;

    // MS-VBAL §5.6.13.1
    // invalid by the specification for an array, and compiled by MS-VBA.
    public static bool IsASecondEmptyArgumentList(IndexExpressionNode index)
        => IndexExpressionNode.IsEmptyArgumentList(index.Arguments) && index.Callee is IndexExpressionNode given && IndexExpressionNode.IsEmptyArgumentList(given.Arguments);

    // MS-VBAL §5.6.13
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not IndexExpressionNode index)
        {
            throw new ArgumentException($"Expected an {nameof(IndexExpressionNode)}.", nameof(expression));
        }

        return DetermineDeclaredType(context, index, operandDeclaredTypes[(int)InputIndex.IndexExpressionCallee], []);
    }

    // restarted is each class whose default member the analysis already restarted on, so a default member that returns its own class ends it.
    private static StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, IndexExpressionNode index, VBType callee, ImmutableHashSet<VBClassType> restarted)
    {
        var expression = index;

        // what a call or an element evaluated to - `F()(1)` of a Function F() As Long - is a value, and a value of an intrinsic type other than Variant is
        // neither an array nor an object: MS-VBA rejects indexing it at compile time with a type mismatch (Excel-verified).
        if (index.Callee is IndexExpressionNode && callee is VBNumericType or VBStringType or VBBooleanType or VBDateType)
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                string.Format(Exceptions.VBIndex_NothingToIndex_Verbose, callee.Name)));
        }
        if (callee is VBVariantType or VBObjectType)
        {
            // late-bound: classified as an unbound member with a declared type of Variant.
            return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
        }

        if (callee is VBArrayType array)
        {
            // MS-VBAL 5.6.13.1: an empty argument list, of an array that was not already given one, is the array itself - `f()` is `f`, as `UBound(f())` and
            // `Erase f()` read it. A second one, `f()()`, the specification makes invalid; MS-VBA compiles it as the array (`UBound(f()())` compiles), and
            // raises 9 when it runs (recorded in Excel). RD-VBA does too, and warns (RDC11025).
            if (IndexExpressionNode.IsEmptyArgumentList(index.Arguments))
            {
                return StaticSemanticsEvaluationResult.Success(array);
            }

            // classified as a variable with the array's element type. MS-VBAL 5.6.13 also requires the
            // argument count to match the array's rank — VBArrayType carries no rank.
            return StaticSemanticsEvaluationResult.Success(array.ItemType);
        }

        if (callee is VBClassType declaredClass)
        {
            // a specific class needs a public default member to be callable this way: the class as the composition has it now (CurrentTypes).
            var classType = CurrentTypes.Of(context.Resolver, declaredClass);
            if (classType.DefaultMember is not { } defaultMember)
            {
                return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, classType.Name));
            }

            // a default member that takes no parameters is called with none: the analysis restarts on what it returns, given the same argument list.
            if (!IndexExpressionNode.IsEmptyArgumentList(index.Arguments) && ObjectValueStaticSemantics.ReturnsWhatArgumentsApplyTo(defaultMember)
                && defaultMember.ResolvedType is VBClassType or VBObjectType or VBVariantType or VBArrayType && !restarted.Contains(classType))
            {
                return DetermineDeclaredType(context, index, defaultMember.ResolvedType, restarted.Add(classType));
            }

            return StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType);
        }

        // a name that nothing compiled declares, written with an argument list, is no implicit variable: the language server declares one for each name a
        // procedure uses that nothing declares, so what it saw declare this one is not compiled - a procedure written only in a dead #If branch (MS-VBAL
        // §3.4). MS-VBA: "Sub or Function not defined".
        if (callee is VBUnknownType && index.Callee is SimpleNameExpressionNode name && context.Scope is { } scope && context.Resolver is not null
            && context.Resolver.ResolveValue(name, RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Local, scope.Uri) is { IsResolved: false, IsError: false })
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, index.Callee.Location, name.WrittenName));
        }

        // callee is already classified as a property/function/subroutine reference (its declared type
        // is already that member's return type) or is not yet known (VBUnknownType): the index
        // expression takes on that same classification and declared type, unchanged.
        return StaticSemanticsEvaluationResult.Success(callee);
    }
}

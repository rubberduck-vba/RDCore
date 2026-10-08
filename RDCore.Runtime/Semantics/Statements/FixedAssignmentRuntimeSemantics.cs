using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.3.6-7</strong> the <c>LSet</c> and <c>RSet</c> statements — assignment into a target
/// of a <em>fixed width</em>, which is what makes them two statements rather than one more
/// <see cref="AssignmentKind"/> of an ordinary assignment.
/// </summary>
/// <remarks>
/// Neither statement lets the target change size. Both read the width off the target's <em>current</em> value
/// and fit the source into exactly that many characters — padding with spaces when it is short, truncating
/// when it is long — differing only in which end the padding goes: <c>LSet</c> left-aligns, <c>RSet</c> right-
/// aligns. That the width comes from the value rather than from the declared type is what makes them useful on
/// a fixed-length <c>String</c>, and harmless on a variable-length one.
/// <para>
/// <c>LSet</c> has a second, unrelated job: between two UDT variables it is a <em>byte</em> copy
/// (<see cref="VBUserDefinedTypeImage"/>), which is how VBA fakes a union. <c>RSet</c> has no such form —
/// §5.4.3.7 admits only <c>String</c> and <c>Variant</c>.
/// </para>
/// <para>
/// <see cref="Analyze"/> establishes the facts about the statement as <see cref="FixedAssignmentSemanticFlags"/>, which
/// <see cref="Evaluate"/> reads back to know which form it is, and which an analyzer reads to say what is worth saying about it: that
/// the byte copy of a record that has a variable-length <c>String</c> member is not what it is in MS-VBA, for one.
/// </para>
/// </remarks>
/// <param name="Expressions">Evaluates the source expression.</param>
/// <param name="LetCoercion">Let-coerces it to <c>String</c>, which both statements require of it.</param>
/// <param name="Assignments">Let-assigns the fitted string into the target.</param>
public sealed record class FixedAssignmentRuntimeSemantics(
    RuntimeExpressionEvaluator Expressions,
    ILetCoercionRuntimeSemanticsProvider LetCoercion,
    LetAssignmentEvaluator Assignments)
    : StatementRuntimeSemantics<FixedAssignmentSemanticContext, FixedAssignmentSemanticFlags>
{
    /// <summary>The character a short value is padded with — U+0020, as the specification spells it.</summary>
    private const char Padding = ' ';

    /// <summary>
    /// Executes an <c>LSet</c> or <c>RSet</c> statement.
    /// </summary>
    /// <param name="session">The session the statement runs in.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="assignment">The statement, whose <see cref="AssignmentStatementNode.Kind"/> says which
    /// end the padding goes.</param>
    public RuntimeExecutionOutcome Execute(
        IRuntimeSession session, RuntimeEvaluationContext context, AssignmentStatementNode assignment)
    {
        if (!Assignments.TryResolveTarget(session, context, assignment.Target, out var symbol))
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var current = ((SDK.Model.Symbols.Abstract.ITypedSymbol)symbol!).ResolvedType
            .CreateValue(session.Symbols.Resolver.GetValue(symbol!));

        var evaluated = Expressions.Evaluate(session, assignment.Value, context);
        if (!evaluated.IsSuccess)
        {
            return evaluated.IsInternalError
                ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
        }

        var source = evaluated.Result!;
        var builder = new SemanticContextFlagsBuilder<FixedAssignmentSemanticContext, FixedAssignmentSemanticFlags>();
        Analyze(session, new ConversionOperationSemanticContext(), builder, assignment, current, source);
        var semantics = builder.Build();

        var result = Evaluate(session, semantics, assignment, current, source);
        if (!result.IsSuccess)
        {
            return result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
        }

        // the byte copy of one record over another is done in place: a UDT has location identity, and LSet assigns no new one.
        return semantics.Flags.HasFlag(FixedAssignmentSemanticFlags.UserDefinedTypeCopy)
            ? RuntimeExecutionOutcome.Next
            : Assignments.Assign(session, context, assignment, symbol!, assignment.Target, assignment.Value, result.Result!);
    }

    public override ISemanticFlagsAccumulator<FixedAssignmentSemanticFlags> Analyze(
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<FixedAssignmentSemanticFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        if (node is not AssignmentStatementNode assignment || inputs is not [var current, var source])
        {
            return builder;
        }

        switch ((Unwrapped(current), Unwrapped(source)))
        {
            case (VBUserDefinedTypeValue target, VBUserDefinedTypeValue from) when assignment.Kind is AssignmentKind.LSet:
                builder.AddFlags(FixedAssignmentSemanticFlags.UserDefinedTypeCopy);
                if (HoldsVariableLengthString(from))
                {
                    builder.AddFlags(FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString);
                }

                if (HoldsVariableLengthString(target))
                {
                    builder.AddFlags(FixedAssignmentSemanticFlags.DestinationHoldsVariableLengthString);
                }
                break;

            case (VBStringValue, _):
                builder.AddFlags(FixedAssignmentSemanticFlags.StringTarget);
                break;

            default:
                builder.AddFlags(FixedAssignmentSemanticFlags.Failed);
                break;
        }

        return builder;
    }

    /// <summary>
    /// Evaluates an <c>LSet</c> or <c>RSet</c> statement over the current value of its target and the value of its source.
    /// </summary>
    /// <remarks>
    /// The result is the string the target is to be given, for the string form. The record form has no value to give: it copies the source
    /// over the target in place, and the result is that target.
    /// </remarks>
    public override RuntimeSemanticsEvaluationResult Evaluate(
        IRuntimeSession session,
        FixedAssignmentSemanticContext context,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        if (node is not AssignmentStatementNode assignment || inputs is not [var current, var source])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // "The value type of <bound-variable-expression> MUST be String or a UDT" - a target holding anything
        // else is a type mismatch at run time, the static rule having already allowed String, Variant and UDT
        // through on the declared type alone.
        return (Unwrapped(current), Unwrapped(source)) switch
        {
            (VBUserDefinedTypeValue target, VBUserDefinedTypeValue from)
                when assignment.Kind is AssignmentKind.LSet => CopyRecord(from, target),

            (VBStringValue target, var from) => Fit(session, assignment, target, from),

            _ => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.TypeMismatch, assignment.SourceLocation, $"{assignment.Kind} requires a String or a user-defined type")),
        };
    }

    // "The data in <expression>... is copied into <bound-variable-expression> variable" - bytes, not fields,
    // so that a copy between two differently-shaped records reinterprets rather than refusing. The target is
    // filled in place: a UDT has location identity, and LSet assigns no new one.
    private static RuntimeSemanticsEvaluationResult CopyRecord(VBUserDefinedTypeValue source, VBUserDefinedTypeValue target)
    {
        VBUserDefinedTypeImage.Copy(source, target);
        return RuntimeSemanticsEvaluationResult.Success(target);
    }

    private RuntimeSemanticsEvaluationResult Fit(
        IRuntimeSession session, AssignmentStatementNode assignment, VBStringValue target, VBTypedValue source)
    {
        // "Let e be the data value of <expression> Let-coerced to declared type String."
        var coerced = LetCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, assignment.Value, new()
        {
            NodeId = assignment.Identity,
            SourceValue = source,
            DestinationTypeDesc = new(VBStringType.TypeInfo),
            Site = ConversionSite.StringStatement,
        });

        if (!coerced.IsSuccess)
        {
            return coerced.IsApplicable
                ? RuntimeSemanticsEvaluationResult.Error(coerced.ErrorInfo!)
                : RuntimeSemanticsEvaluationResult.InternalError();
        }

        var text = coerced.Result!.Handle.Value.BoxedValue as string ?? string.Empty;

        // "Let qLength be the number of characters in the data value of <bound-variable-expression>" - the
        // target's own current length, which is the whole point: neither statement resizes anything.
        var width = target.Length;
        var fitted = text.Length >= width
            // both statements truncate from the same end: "the initial qLength characters of e" for LSet, and
            // "the first qLength characters in <expression>" for RSet. Only the padding side differs.
            ? text[..width]
            : assignment.Kind is AssignmentKind.RSet
                ? text.PadLeft(width, Padding)
                : text.PadRight(width, Padding);

        return RuntimeSemanticsEvaluationResult.Success(new VBStringValue(fitted));
    }

    private static bool HoldsVariableLengthString(VBUserDefinedTypeValue record)
        => record.TypeInfo is VBUserDefinedType type && VBUserDefinedTypeImage.HoldsVariableLengthString(type);

    // a Variant target or source is one of the declared types the static rule admits, and what it holds is
    // what the runtime rule is about - so both sides are unwrapped before either is judged.
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }
}

using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.SetCoercion;

/// <summary>
/// <strong>MS-VBAL 5.5.2.2</strong> Set-coercion (run-time semantics).
/// </summary>
/// <remarks>
/// A <see cref="VBObjectValue"/>'s own <c>TypeInfo</c> is always the generic <see cref="VBObjectType"/>
/// (confirmed: <c>NewExpressionRuntimeSemantics</c> is the only production site constructing one, and
/// it never sets anything but that) — a live object's <em>actual</em> class is only known by looking up
/// its <see cref="IObjectInstance.ClassModule"/> through <see cref="IRuntimeSession.TryGetInstance"/>.
/// The same-or-implements-class compatibility check (MS-VBAL 5.5.2.2.1) walks
/// <see cref="VBClassType.Supertypes"/> from there — today that is only ever the default
/// <c>[VBObjectType.TypeInfo]</c> (nothing in the codebase yet populates it from a class's real
/// <c>Implements</c> clauses), so this check currently only ever succeeds for an exact same-class
/// match; it becomes meaningful the moment something populates <c>Supertypes</c>, with no changes
/// needed here.
/// </remarks>
public sealed record class SetCoercionRuntimeSemantics(IVerboseMessageBuilder FormatterService) : ISetCoercionRuntimeSemantics
{
    public SetCoercionResult EvaluateSetCoercion(IRuntimeSession session, ExpressionNode expression, VBTypedValue source, VBType destinationType)
    {
        if (source is not VBObjectValue sourceObject)
        {
            // MS-VBAL 5.5.2.2.2: source isn't an object reference at all.
            return SetCoercionResult.Error(destinationType is VBVariantType
                ? OnSetCoercionTypeMismatch(expression)
                : OnSetCoercionObjectRequired(expression));
        }

        if (sourceObject.IsNothing())
        {
            // MS-VBAL 5.5.2.2.1: Nothing -> any class/Object/Variant destination always succeeds.
            return SetCoercionResult.Success(destinationType is VBVariantType ? new VBVariantValue(VBObjectValue.Nothing) : VBObjectValue.Nothing);
        }

        if (destinationType is VBObjectType or VBVariantType)
        {
            // late-bound / catch-all destination: any real object reference is compatible.
            return SetCoercionResult.Success(destinationType is VBVariantType ? new VBVariantValue(sourceObject) : sourceObject);
        }

        if (destinationType is VBClassType destinationClass)
        {
            return session.Symbols.TryGetInstance(sourceObject.Value, out var instance)
                && IsCompatibleClass(VBClassType.FromClassModule(instance.ClassModule), destinationClass)
                    ? SetCoercionResult.Success(sourceObject)
                    : SetCoercionResult.Error(OnSetCoercionTypeMismatch(expression));
        }

        return SetCoercionResult.Error(OnSetCoercionTypeMismatch(expression));
    }

    private static bool IsCompatibleClass(VBClassType source, VBClassType destination, HashSet<Uri>? visited = null)
    {
        if (string.Equals(source.Symbol.Uri.AbsoluteUri, destination.Symbol.Uri.AbsoluteUri, StringComparison.Ordinal))
        {
            return true;
        }

        visited ??= [];
        if (!visited.Add(source.Symbol.Uri))
        {
            return false; // already visited - guards against a cyclical Implements graph.
        }

        return source.Supertypes.Any(supertype => supertype is VBClassType superClass && IsCompatibleClass(superClass, destination, visited));
    }

    private VBRuntimeErrorInfo OnSetCoercionTypeMismatch(ExpressionNode expression) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, expression.Location,
            FormatterService.Format(Exceptions.SetCoercionRuntimeErrorExceptionTypeMismatch_Verbose, expression, []));

    private VBRuntimeErrorInfo OnSetCoercionObjectRequired(ExpressionNode expression) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectRequired, expression.Location,
            FormatterService.Format(Exceptions.SetCoercionRuntimeErrorExceptionObjectRequired_Verbose, expression, []));
}

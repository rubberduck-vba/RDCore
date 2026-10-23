using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Reading and writing a binding without losing whether the value is known (<see cref="IKnowledgeBinding"/>).
/// </summary>
public static class BindingKnowledge
{
    /// <summary>
    /// Stores <paramref name="value"/> in <paramref name="handle"/>, and whether it is known where the binding keeps that.
    /// </summary>
    /// <param name="handle">The binding to write to.</param>
    /// <param name="resolver">The resolver the binding writes through.</param>
    /// <param name="value">The runtime value to store.</param>
    /// <param name="isKnown">Whether it is known.</param>
    /// <remarks>
    /// 👉 A binding that does not keep whether its value is known is written to as it always was.
    /// </remarks>
    public static void Store(this IBindingHandle handle, ISymbolResolver resolver, IRuntimeValue value, bool isKnown)
    {
        if (handle is IKnowledgeBinding knowledge)
        {
            knowledge.Store(value, isKnown);
        }
        else
        {
            handle.SetValue(resolver, value);
        }
    }

    /// <summary>
    /// Stores the value of <paramref name="source"/> in <paramref name="handle"/>, known or not as <paramref name="source"/> is.
    /// </summary>
    /// <param name="handle">The binding to write to.</param>
    /// <param name="resolver">The resolver the binding writes through.</param>
    /// <param name="source">The value being assigned.</param>
    public static void Store(this IBindingHandle handle, ISymbolResolver resolver, VBTypedValue source)
        => handle.Store(resolver, source.RuntimeValue, !source.IsIndeterminate);

    /// <summary>
    /// A new cell that holds the value of <paramref name="source"/>, known or not as <paramref name="source"/> is: the cell an element of an
    /// array, or a field, is given when it is assigned a cell of its own.
    /// </summary>
    /// <param name="source">The value being assigned.</param>
    public static ValueBindingHandle NewCell(VBTypedValue source) => NewCell(source.RuntimeValue, !source.IsIndeterminate);

    /// <summary>
    /// A new cell that holds <paramref name="value"/>, and whether it is known.
    /// </summary>
    /// <param name="value">The runtime value to hold.</param>
    /// <param name="isKnown">Whether it is known.</param>
    public static ValueBindingHandle NewCell(IRuntimeValue value, bool isKnown)
    {
        var cell = new ValueBindingHandle(value);
        if (!isKnown)
        {
            cell.Forget();
        }

        return cell;
    }

    /// <summary>
    /// The binding a value that is read from <paramref name="handle"/> is bound to.
    /// </summary>
    /// <param name="handle">The binding being read.</param>
    /// <returns>
    /// <paramref name="handle"/> itself when what it holds is known. When it is not, a detached binding to the same assumed value that
    /// is not known and never will be: the cell may be assigned a known value afterwards, which must not make a value read before
    /// look known.
    /// </returns>
    public static IBindingHandle ForReading(this IBindingHandle handle)
        => handle switch
        {
            ReferenceBindingHandle { IsKnown: false } cell => new IndeterminateBindingHandle(new ReferenceBindingHandle((VBRuntimeReference)cell.Value)),
            // a cell of a value, of whatever representation it holds the value in (a runtime's payload cell is one too).
            IKnowledgeBinding { IsKnown: false } cell => new IndeterminateBindingHandle(new ValueBindingHandle(cell.Value)),
            _ => handle,
        };
}

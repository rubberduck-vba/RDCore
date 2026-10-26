using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A <c>Variant</c> that refers to a variable of another declared type: what an element of a param array is when the argument it was given is a variable
/// (<strong>MS-VBAL §5.3.1.5</strong>: the param array "behaves as if it was an array whose elements were 'byref' &lt;positional-param&gt; elements whose
/// declared types were Variant").
/// </summary>
/// <remarks>
/// <para>
/// Read, it is a <c>Variant</c> holding the variable's value, as the type the variable is declared: <c>TypeName(a(0))</c> of a <c>Long</c> is <c>Long</c>.
/// Written, it is the variable that is written, which holds a value of its declared type only: what is assigned to the element is let- or set-coerced to
/// <see cref="DeclaredType"/> before it is stored (<see cref="DeclaredTypeOf"/>), as an assignment to the variable would be - a <c>String</c> no <c>Long</c>
/// can hold is a type mismatch, and leaves the variable as it was.
/// </para>
/// <para>
/// A variable declared <c>Variant</c> holds a <c>Variant</c> already, which is read and written as it is: the handle says only that the element is a variable,
/// which a server is given by reference (<c>VT_BYREF</c>) when the param array is a late-bound member's arguments.
/// </para>
/// </remarks>
/// <param name="Variable">The cell of the variable the element refers to.</param>
/// <param name="DeclaredType">The declared type of the variable.</param>
public sealed record class VariantReferenceBindingHandle(IBindingHandle Variable, VBType DeclaredType) : IKnowledgeBinding
{
    /// <summary>
    /// The declared type a value assigned through <paramref name="cell"/> is coerced to: the referenced variable's, for a <see cref="VariantReferenceBindingHandle"/>,
    /// and <paramref name="itemType"/>, the element type of the array, for any other cell.
    /// </summary>
    /// <param name="cell">The cell of an element, or <see langword="null"/> for an element that has none.</param>
    /// <param name="itemType">The element type of the array the element is of.</param>
    public static VBType DeclaredTypeOf(IBindingHandle? cell, VBType itemType)
        => cell is VariantReferenceBindingHandle reference ? reference.DeclaredType : itemType;

    /// <inheritdoc/>
    /// <remarks>A <c>Variant</c> holding the variable's value, as the type it is declared.</remarks>
    public IRuntimeValue Value => DeclaredType is VBVariantType ? Variable.Value : new VBRuntimeVariantValue(DeclaredType.VarType(), DeclaredType.CreateValue(Variable));

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => Variable.BindingCapabilities;

    /// <inheritdoc/>
    public bool IsKnown => Variable is not IKnowledgeBinding { IsKnown: false };

    /// <inheritdoc/>
    public IRuntimeValue GetValue(ISymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => Variable.SetValue(resolver, ValueOfTheVariable(value));

    /// <inheritdoc/>
    public void Store(IRuntimeValue value, bool isKnown)
    {
        if (Variable is IKnowledgeBinding knowledge)
        {
            knowledge.Store(ValueOfTheVariable(value), isKnown);
            return;
        }

        throw new NotSupportedException($"The variable an element of a param array refers to cannot be written without a resolver ({DeclaredType.Name}).");
    }

    /// <inheritdoc/>
    public void Forget()
    {
        if (Variable is IKnowledgeBinding knowledge)
        {
            knowledge.Forget();
        }
    }

    /// <inheritdoc/>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();

    // what the variable holds: the value, coerced to its declared type by whatever assigns the element (DeclaredTypeOf) - or a Variant holding a value of
    // that type, which is the same value. A Variant holding a value of another type was not coerced, and the variable cannot hold it.
    private IRuntimeValue ValueOfTheVariable(IRuntimeValue value)
        => value switch
        {
            _ when DeclaredType is VBVariantType => value,
            VBRuntimeVariantValue { WrappedValue: var wrapped } when wrapped.TypeInfo.Name == DeclaredType.Name => wrapped.RuntimeValue,
            VBRuntimeVariantValue { WrappedValue: var wrapped }
                => throw new InvalidOperationException($"A {wrapped.TypeInfo.Name} was stored through an element that refers to a {DeclaredType.Name}, without being coerced to it."),
            _ => value,
        };
}

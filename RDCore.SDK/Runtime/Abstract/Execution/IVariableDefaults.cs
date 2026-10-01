using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What the storage of a declared variable starts as.
/// </summary>
/// <remarks>
/// For most variables that is the default value of the declared type. For an array it is more than the type can say: a
/// fixed-size array is as big as its declaration's bounds (<strong>MS-VBAL §5.2.3.1.3</strong>), constant expressions that may
/// name a <c>Const</c> and so have to be reduced, and an array of <c>Long</c> that has no dimensions yet is still an array of
/// <c>Long</c>. Whatever allocates storage for a variable asks this, which is what keeps that in one place.
/// </remarks>
public interface IVariableDefaults
{
    /// <summary>
    /// The value the storage of <paramref name="variable"/> starts as: its type's default value, except for an array.
    /// </summary>
    /// <param name="variable">The variable, which is typed.</param>
    VBTypedValue DefaultValueOf(Symbol variable);
}

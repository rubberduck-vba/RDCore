namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// States that a standard-library parameter declared with a type of its own is given where its argument is - its address in the session's memory - rather
/// than what it holds: <c>StrPtr</c>'s, which is the address of the characters of the very <c>String</c> it is passed.
/// </summary>
/// <remarks>
/// The parameter is declared as a <see cref="Model.Values.Intrinsic.VBLongLongValue"/>, whatever the width of the environment's pointers, and is declared to
/// the language as <see cref="DeclaredAs"/> says, <c>ByVal</c>: it takes what a parameter of that type takes. What it is given is
/// <list type="bullet">
/// <item>the address of a variable of that type, an element of an array of it, or a field of a record of it, passed to it - and of a <c>Variant</c> that holds
/// a value of that type;</item>
/// <item>the address of a temporary holding the argument let-coerced to the type, for anything else, which is freed when the statement that passes it is
/// over.</item>
/// </list>
/// Unlike <see cref="StdLibAsAnyAttribute"/>, a <c>ByVal</c> argument passes no bytes in place of an address: it is a value, passed in a temporary.
/// </remarks>
/// <param name="declaredAs">The type of the intrinsic value the parameter is declared as, such as <see cref="Model.Values.Intrinsic.VBStringValue"/>.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibByAddressAttribute(Type declaredAs) : Attribute
{
    /// <summary>
    /// The type of the intrinsic value the parameter is declared as.
    /// </summary>
    public Type DeclaredAs { get; } = declaredAs;
}

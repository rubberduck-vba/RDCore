namespace RDCore.SDK.Model;

/// <summary>
/// Where the variable a reference to an undeclared name implicitly declares lives.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.6.10</strong> has such a name declare a local variable of the procedure it is written in,
/// "as if by a local variable declaration statement immediately preceding" the statement — which is what
/// <see cref="Procedure"/> is. It is also the reason a value assigned to an undeclared name does not outlive the
/// procedure that assigned it.
/// <para>
/// <see cref="Module"/> is how a BASIC works: every variable is global, so a name assigned in one line is the
/// same variable in the next, and a program's variables can be looked at after it has run. It is a dial of the
/// environment rather than of a module — a language server serving an interactive shell turns it on — and it
/// changes nothing about a name that is declared, only where an undeclared one is declared.
/// </para>
/// <para>
/// 👉 Neither mode applies under <c>Option Explicit</c>, where an undeclared name is a compile error.
/// </para>
/// </remarks>
public enum ImplicitDeclarationScope
{
    /// <summary>
    /// A local of the procedure that refers to the name: <strong>MS-VBAL §5.6.10</strong>, and VBA's behavior.
    /// </summary>
    Procedure = 0,

    /// <summary>
    /// A module-level <c>Variant</c> of the module that refers to the name, shared by every procedure of it and
    /// kept for as long as the session is.
    /// </summary>
    Module = 1,
}

using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// How an instruction gives a variable a value.
/// </summary>
public enum VariableDefinitionKind
{
    /// <summary>
    /// The value the variable has when the activation starts: a parameter's argument, a local's initial value, a field's current value. Not an instruction's.
    /// </summary>
    Entry,

    /// <summary>
    /// An assignment of the whole variable: <c>Let</c>, <c>Set</c>, <c>LSet</c>, <c>RSet</c>.
    /// </summary>
    Assignment,

    /// <summary>
    /// The control variable of a <c>For</c> or <c>For Each</c> loop, assigned by the loop.
    /// </summary>
    LoopControl,

    /// <summary>
    /// <c>ReDim</c> of an array, or <c>Erase</c>.
    /// </summary>
    Redimension,

    /// <summary>
    /// A write to a part of the variable: an element of an array, a member of a <c>Type</c>, a part of a string (<c>Mid</c>), the elements a <c>ReDim Preserve</c>
    /// keeps.
    /// </summary>
    PartialWrite,

    /// <summary>
    /// An argument the procedure called may take by reference, and so write to.
    /// </summary>
    ByRefArgument,

    /// <summary>
    /// A variable a file statement reads into: <c>Input #</c>, <c>Line Input #</c>, <c>Get #</c>.
    /// </summary>
    FileInput,
}

/// <summary>
/// A place where a variable may be given a value.
/// </summary>
/// <remarks>
/// <para>
/// Every variable the procedure defines or uses has one <see cref="VariableDefinitionKind.Entry"/> definition, the value it has when the activation starts, and one
/// definition for each place an instruction may write it: an instruction may have several of the same variable (<c>x = F(x)</c> assigns <c>x</c> and passes it
/// by reference; <c>Input #1, a(1), a(2)</c> writes two parts of <c>a</c>). A write to a part of it (<c>a(1) = 5</c>, <c>t.X = 1</c>, <c>a(1).X = 1</c>,
/// <c>.X = 1</c> in a <c>With</c> block on a <c>Type</c> variable, <c>Mid(s, 1) = "x"</c>) is a definition of the variable the target is rooted at, of the kind
/// <see cref="VariableDefinitionKind.PartialWrite"/> whatever the statement is.
/// </para>
/// <para>
/// A definition replaces the value before it only when its instruction <em>completes</em>. An instruction that raises an error does not complete, and may have
/// written some of what it writes all the same (a procedure it called may have written an argument before it failed). Along its error edge (<see cref="FlowEdges"/>),
/// what reached it and what it defines both reach the handler, the next statement, or the caller.
/// </para>
/// <para>
/// What a procedure that is called writes is no definition here: a field, a global or a <c>Static</c> local that it may change is only a use where it is called, which
/// keeps the value before it live. An argument it may take by reference is a definition (<see cref="VariableDefinitionKind.ByRefArgument"/>), which is not complete.
/// </para>
/// </remarks>
/// <param name="Variable">The variable.</param>
/// <param name="Offset">The offset of the instruction that defines it; <c>-1</c> for <see cref="VariableDefinitionKind.Entry"/>.</param>
/// <param name="Kind">How the instruction gives the variable a value.</param>
/// <param name="IsComplete">
/// Whether the whole value is replaced, every time the instruction completes: what ends the life of the value before. A write to a part, and an argument that may or may
/// not be written, is not; neither is a <c>For Each</c> loop's assignment of its control variable, which an empty collection never makes.
/// </param>
/// <param name="Expression">
/// The target the instruction writes, the whole of it: <c>a(1).X</c> for a write to <c>a(1).X</c>, of which <see cref="Variable"/> is <c>a</c>. <see langword="null"/>
/// for <see cref="VariableDefinitionKind.Entry"/>.
/// </param>
public sealed record class VariableDefinition(SemanticId Variable, int Offset, VariableDefinitionKind Kind, bool IsComplete, SyntaxNode? Expression);

/// <summary>
/// A place where the value of a variable may be read.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Offset">The offset of the instruction that reads it.</param>
/// <param name="Expression">The expression that names it there.</param>
public sealed record class VariableUse(SemanticId Variable, int Offset, SyntaxNode Expression);

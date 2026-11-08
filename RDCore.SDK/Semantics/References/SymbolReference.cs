using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Semantics.References;

/// <summary>
/// How a reference writes the name of a symbol.
/// </summary>
public enum ReferenceForm
{
    /// <summary>
    /// The name a declaration declares: a variable, a constant, a parameter, a procedure, a <c>Type</c>, an <c>Enum</c> or one of their members.
    /// </summary>
    Declaration,

    /// <summary>
    /// A simple name (<strong>MS-VBAL §5.6.10</strong>): <c>Foo</c>, or the owner <c>Foo</c> of <c>Foo.Bar</c>.
    /// </summary>
    Name,

    /// <summary>
    /// The name of a member, after a <c>.</c> that has an expression before it (<strong>MS-VBAL §5.6.12</strong>): <c>Bar</c> of <c>Foo.Bar</c>.
    /// </summary>
    Member,

    /// <summary>
    /// The name of a member, after a <c>.</c> that has no expression before it, in a <c>With</c> block (<strong>MS-VBAL §5.6.15</strong>): <c>Bar</c> of
    /// <c>.Bar</c>, a member of the object of the block.
    /// </summary>
    WithMember,

    /// <summary>
    /// The name of a named argument, before its <c>:=</c> (<strong>MS-VBAL §5.6.13.1</strong>): the parameter it is given to.
    /// </summary>
    NamedArgument,

    /// <summary>
    /// The member an attribute is of: <c>Foo</c> of <c>Attribute Foo.VB_UserMemId = 0</c>.
    /// </summary>
    Attribute,

    /// <summary>
    /// The type an <c>As</c> clause declares: <c>Class1</c> of <c>Dim c As Class1</c>, or of <c>As New Class1</c>.
    /// </summary>
    TypeName,

    /// <summary>
    /// The name of a procedure, in a string literal given to a member of a library that calls a procedure by its name: the procedure name of
    /// <c>CallByName</c> (<strong>MS-VBAL §6.1.2.8.1.1</strong>), a host's macro (<c>Application.Run "Module1.Foo"</c>), the procedure a host calls later
    /// (<c>Application.OnTime</c>, <c>OnKey</c>), or the <c>OnAction</c> of a control. What the string names is only known when the code runs.
    /// </summary>
    String,
}

/// <summary>
/// How a reference uses the symbol it names, where the reference is a name in an expression (<see cref="ReferenceForm.Name"/>,
/// <see cref="ReferenceForm.Member"/>, <see cref="ReferenceForm.WithMember"/>).
/// </summary>
/// <remarks>
/// It is what tells the procedures of a property apart (<strong>MS-VBAL §5.3.1.7</strong>): a read calls its <c>Property Get</c>, a Let-assignment its
/// <c>Property Let</c>, a Set-assignment its <c>Property Set</c>.
/// </remarks>
public enum ReferenceUse
{
    /// <summary>
    /// The reference is no name in an expression: a declaration, a named argument, an attribute, a type name or a string.
    /// </summary>
    None,

    /// <summary>
    /// Its value is read, or it is called.
    /// </summary>
    Read,

    /// <summary>
    /// It is the target of a Let-assignment (<strong>MS-VBAL §5.4.3.8</strong>), or of an <c>LSet</c> or an <c>RSet</c> (<strong>MS-VBAL §5.4.3.6</strong>,
    /// §5.4.3.7), with an argument list or not (<c>Title(1) = x</c>).
    /// </summary>
    Let,

    /// <summary>
    /// It is the target of a Set-assignment (<strong>MS-VBAL §5.4.3.9</strong>).
    /// </summary>
    Set,

    /// <summary>
    /// It is the result of the function or the <c>Property Get</c> whose body assigns it, or reads it as a simple name: its function result variable
    /// (<strong>MS-VBAL §5.3.1</strong>, §5.6.2.1), which calls no procedure. With an argument list, the same name calls the procedure again, and is
    /// <see cref="Read"/>.
    /// </summary>
    Result,
}

/// <summary>
/// One place a module of the workspace names a symbol, and how.
/// </summary>
/// <param name="Module">The address of the module.</param>
/// <param name="Range">Where the name is written, without a type-declaration character; in a string, the name in it.</param>
/// <param name="Form">How the name is written.</param>
/// <param name="Use">How the name is used, where it is a name in an expression.</param>
public sealed record class SymbolReference(Uri Module, SourceRange Range, ReferenceForm Form, ReferenceUse Use)
{
    /// <summary>
    /// The symbol the name refers to: one of <see cref="SymbolReferences.Symbols"/>, the procedure of a property it calls among them.
    /// <see langword="null"/> for a reference of <see cref="SymbolReferences.Unknown"/>, whose meaning is not known.
    /// </summary>
    public SemanticId? Symbol { get; init; }

    /// <summary>
    /// For a <see cref="ReferenceForm.Member"/>, what the expression before the <c>.</c> refers to: the module, the variable or the property whose member it is.
    /// <see langword="null"/> for any other form, and where the expression refers to no symbol (<c>GetObject().Foo</c>) or is not in a procedure.
    /// </summary>
    public SemanticId? Owner { get; init; }

    /// <summary>
    /// The node that writes the argument list the name is called or indexed with, when one is written after it: the index expression of <c>Foo(1)</c> and of
    /// <c>Call Foo(1)</c> (<c>IndexExpressionNode</c>), or the call statement of <c>Foo 1, 2</c> and of a bare <c>Foo</c> (<c>CallStatementNode</c>), whose
    /// argument list may be empty. <see langword="null"/> where no argument list is written after the name, and for a reference that is no name in an
    /// expression.
    /// </summary>
    /// <remarks>The node is of the syntax tree of the module the query was given (<c>RenameModule.Syntax</c>).</remarks>
    public SyntaxNode? Call { get; init; }

    /// <summary>
    /// The node of the call the name is a whole argument of, by itself or as the value of a named argument: the index expression or the call statement.
    /// <see langword="null"/> where the name is no whole argument of a call: in a larger expression (<c>Foo x + 1</c>), in parentheses (<c>Foo (x)</c>), or
    /// an index of an array.
    /// </summary>
    public SyntaxNode? ArgumentOf { get; init; }

    /// <summary>
    /// Whether the call the name is an argument of may take it by reference (<strong>MS-VBAL §5.3.1.11</strong>), and write to what it names: an argument not
    /// written with <c>ByVal</c>, nor in parentheses, given to a parameter that is <c>ByRef</c> or a <c>ParamArray</c>, or to a procedure whose parameters are
    /// not known (a member called late-bound). A public variable of an object is never taken by reference: MS-VBA passes a copy of it.
    /// </summary>
    public bool MayBePassedByRef { get; init; }

    /// <summary>
    /// For a <see cref="ReferenceForm.String"/>, what qualifies the name in it: <c>Module1</c> of <c>"Module1.Foo"</c>; <see langword="null"/> otherwise.
    /// </summary>
    public string? Qualifier { get; init; }
}

/// <summary>
/// Every place the modules of the workspace name a symbol, and the places that write its name where what they mean is not known.
/// </summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Symbols">
/// The symbol and those defined with it, which the references refer to: each procedure of a property (its <c>Property Get</c>, <c>Let</c> and <c>Set</c>).
/// </param>
/// <param name="References">Each place that names the symbol, its declarations included, by module and then in the order written.</param>
/// <param name="Unknown">
/// Each place that writes the symbol's name, where the symbol could be what it means and what it means is not known: a member accessed late-bound (on an
/// <c>Object</c> or a <c>Variant</c>), a name in an expression nested too deeply to be analyzed (<strong>RD-VBAL §2.6.1</strong>), and a name in a string a
/// library calls a procedure by (<see cref="ReferenceForm.String"/>). A refactoring that rewrites the references cannot rewrite these with certainty.
/// </param>
public sealed record class SymbolReferences(
    Symbol Symbol, IReadOnlyList<SemanticId> Symbols, IReadOnlyList<SymbolReference> References, IReadOnlyList<SymbolReference> Unknown);

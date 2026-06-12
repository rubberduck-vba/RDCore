namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.1 Expression Classifications</strong>
/// </summary>
/// <remarks>
/// For documentation purposes only - this is not intended to be directly used anywhere.
/// RDCore AST ultimately defines every single one of these, but the classifications aren't determining the structure: 
/// in this implementation everything is derived from the semantics.
/// </remarks>
public enum ExpressionClassification
{
    /// <summary>
    /// Represents an immutable <em>data value</em> with a <em>declared type</em>.
    /// </summary>
    ValueExpression,
    /// <summary>
    /// References a <em>variable declaration</em>, has an <em>argument list</em>, and a <em>declarated type</em>.
    /// </summary>
    VariableExpression,
    /// <summary>
    /// References a <em>property</em>, has an <em>argument list</em>, and a <em>declared type</em>.
    /// </summary>
    PropertyExpression,
    /// <summary>
    /// References a <em>function</em>, has an <em>argument list</em>, and a <em>declared type</em>.
    /// </summary>
    FunctionExpression,
    /// <summary>
    /// References a <em>subroutine</em> and has an <em>argument list</em>.
    /// </summary>
    SubroutineExpression,
    /// <summary>
    /// References a variable, property, function, or subroutine that cannot be statically determined. Has an optional member name, and an argument list.
    /// </summary>
    UnboundMemberExpression,
    /// <summary>
    /// References a <em>project</em>.
    /// </summary>
    ProjectExpression,
    /// <summary>
    /// References a <em>procedural module</em>.
    /// </summary>
    ProceduralModuleExpression,
    /// <summary>
    /// References a <em>declared type</em>.
    /// </summary>
    TypeExpression,
}

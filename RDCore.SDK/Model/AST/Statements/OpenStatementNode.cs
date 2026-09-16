using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>MS-VBAL 5.4.5.1</strong> the <c>Open</c> statement's <c>fileMode</c> clause.
/// </summary>
public enum VBFileMode
{
    Append,
    Binary,
    Input,
    Output,
    Random,
}

/// <summary>
/// <strong>MS-VBAL 5.4.5.1</strong> the <c>Open</c> statement's <c>access</c> clause.
/// </summary>
public enum VBFileAccessMode
{
    Read,
    Write,
    ReadWrite,
}

/// <summary>
/// <strong>MS-VBAL 5.4.5.1</strong> the <c>Open</c> statement's <c>lock</c> clause.
/// </summary>
public enum VBFileLockMode
{
    Shared,
    Read,
    Write,
    ReadWrite,
}

/// <summary>
/// <strong>MS-VBAL 5.4.5.1</strong> the <c>Open</c> statement. <see cref="Mode"/>, <see cref="Access"/>,
/// and <see cref="Lock"/> are keyword choices, not expressions — the reason this statement needs its
/// own shape rather than the generic <see cref="KeywordStatementNode"/> the other file statements share.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="PathName">The file path expression.</param>
/// <param name="Mode">The <c>For</c> mode clause, or <c>null</c> when the statement declares none.</param>
/// <param name="Access">The <c>Access</c> clause, or <c>null</c> when the statement declares none.</param>
/// <param name="Lock">The lock clause, or <c>null</c> when the statement declares none.</param>
/// <param name="FileNumber">The file channel this handle is bound to.</param>
/// <param name="RecordLength">The <c>Len</c> clause's record length, or <c>null</c> when the statement declares none.</param>
public record class OpenStatementNode(
    SyntaxNodeId Identity,
    SourceLocation SourceLocation,
    ExpressionNode PathName,
    VBFileMode? Mode,
    VBFileAccessMode? Access,
    VBFileLockMode? Lock,
    ExpressionNode FileNumber,
    ExpressionNode? RecordLength)
    : StatementNode(Identity, SourceLocation, RecordLength is null ? [PathName, FileNumber] : [PathName, FileNumber, RecordLength]);

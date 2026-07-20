using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

public record class StatementBlock(ImmutableArray<BoundStatement> Statements);

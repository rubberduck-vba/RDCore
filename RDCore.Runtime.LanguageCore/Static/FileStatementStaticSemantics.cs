using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.4.5; RD-VBAL §5.4.5.13, §5.4.5.1.1, §5.4.5.1, §5.4.5.6, §5.4.5.10, §5.4.5.12, §5.4.5.11
public static class FileStatementStaticSemantics
{
    public static bool TryEvaluate(StaticEvaluationContext context, StatementNode statement, out ImmutableArray<VBCompileErrorInfo> errors)
    {
        var checks = new StatementOperandChecks(context);
        errors = [];

        switch (statement)
        {
            case OpenStatementNode open:
                EvaluateOpen(checks, open);
                break;
            case PrintStatementNode { FileNumber: { } fileNumber } print:
                FileNumber(checks, fileNumber);
                OutputList(checks, print.Items);
                break;
            case FileLockStatementNode fileLock:
                FileNumber(checks, fileLock.FileNumber);
                foreach (var record in new[] { fileLock.StartRecord, fileLock.EndRecord }.OfType<ExpressionNode>())
                {
                    checks.RequireScalar(record, checks.TypeOf(record), "A record number");
                }
                break;
            case KeywordStatementNode keyword when EvaluateKeyword(checks, keyword):
                break;
            default:
                return false;
        }

        errors = checks.Errors;
        return true;
    }

    // "If <mode> is the keyword Output then <access> MUST consist of the keyword Write. If <mode> is the keyword Input then <access> MUST be the keyword Read.
    // If <mode> is the keyword Append then <access> MUST be either the keyword sequence Read Write or the keyword Write."
    private static void EvaluateOpen(StatementOperandChecks checks, OpenStatementNode open)
    {
        checks.RequireLetCoercible(open.PathName, checks.TypeOf(open.PathName), VBStringType.TypeInfo);
        FileNumber(checks, open.FileNumber);
        if (open.RecordLength is { } length)
        {
            checks.RequireLetCoercible(length, checks.TypeOf(length), VBIntegerType.TypeInfo);
        }

        var allowed = open.Mode switch
        {
            VBFileMode.Output => new[] { VBFileAccessMode.Write },
            VBFileMode.Input => [VBFileAccessMode.Read],
            VBFileMode.Append => [VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
            _ => null,
        };

        if (open.Access is { } access && allowed is not null && !allowed.Contains(access))
        {
            checks.Add(VBCompileErrorInfo.For(VBCompileErrorId.FileAccessNotValidForMode, open.SourceLocation,
                $"A file opened For {open.Mode} cannot have Access {access} (MS-VBAL §5.4.5.1)."));
        }
    }

    // the statements that are a keyword and a list of operands, by the order the parser gives them (the optional record number of Put and Get is not there when absent).
    private static bool EvaluateKeyword(StatementOperandChecks checks, KeywordStatementNode statement)
    {
        var inputs = statement.Inputs.OfType<ExpressionNode>().ToArray();
        switch (statement.Token)
        {
            case Tokens.Close:
                foreach (var fileNumber in inputs)
                {
                    FileNumber(checks, fileNumber);
                }
                return true;

            case Tokens.Seek when inputs is [var fileNumber, var position]:
                FileNumber(checks, fileNumber);
                checks.RequireScalar(position, checks.TypeOf(position), "The position of a Seek statement");
                return true;

            case Tokens.Width when inputs is [var fileNumber, var width]:
                FileNumber(checks, fileNumber);
                checks.RequireScalar(width, checks.TypeOf(width), "The line width of a Width statement");
                return true;

            case Tokens.LineInput when inputs is [var fileNumber, var variable]:
                FileNumber(checks, fileNumber);
                checks.RequireVariable(variable, "The target of a Line Input statement", "5.4.5.6");
                checks.RequireStringOrVariant(variable, checks.TypeOf(variable), "The target of a Line Input statement");
                return true;

            case Tokens.Input when inputs is [var fileNumber, .. var variables]:
                FileNumber(checks, fileNumber);
                foreach (var variable in variables)
                {
                    checks.RequireVariable(variable, "An input variable", "5.4.5.10");
                    checks.RequireNotAnObject(variable, checks.TypeOf(variable), "An input variable", throughUserDefinedTypes: false);
                }
                return true;

            case Tokens.Put when inputs is [var fileNumber, .., var data]:
                FileNumber(checks, fileNumber);
                foreach (var record in inputs[1..^1])
                {
                    checks.TypeOf(record);
                }
                checks.RequireNotAnObject(data, checks.TypeOf(data), "The data of a Put statement", throughUserDefinedTypes: true);
                return true;

            case Tokens.Get when inputs is [var fileNumber, .., var variable]:
                FileNumber(checks, fileNumber);
                foreach (var record in inputs[1..^1])
                {
                    checks.TypeOf(record);
                }
                checks.RequireVariable(variable, "The target of a Get statement", "5.4.5.12");
                checks.RequireNotAnObject(variable, checks.TypeOf(variable), "The target of a Get statement", throughUserDefinedTypes: true);
                return true;

            case Tokens.Name when inputs is [var oldPath, var newPath]:
                checks.RequireLetCoercible(oldPath, checks.TypeOf(oldPath), VBStringType.TypeInfo);
                checks.RequireLetCoercible(newPath, checks.TypeOf(newPath), VBStringType.TypeInfo);
                return true;

            default:
                return false;
        }
    }

    private static void FileNumber(StatementOperandChecks checks, ExpressionNode fileNumber)
        => checks.RequireScalar(fileNumber, checks.TypeOf(fileNumber), "A file number");

    // "The declared type of <spc-number> and of <tab-number> MUST be a scalar declared type."
    private static void OutputList(StatementOperandChecks checks, ImmutableArray<PrintOutputItemNode> items)
    {
        foreach (var item in items)
        {
            switch (item.Value)
            {
                case PrintSpcClauseNode spc:
                    checks.RequireScalar(spc.Count, checks.TypeOf(spc.Count), "The number of a Spc clause");
                    break;
                case PrintTabClauseNode { Column: { } column }:
                    checks.RequireScalar(column, checks.TypeOf(column), "The number of a Tab clause");
                    break;
                case PrintTabClauseNode:
                    break;
                case { } value:
                    checks.TypeOf(value);
                    break;
            }
        }
    }
}

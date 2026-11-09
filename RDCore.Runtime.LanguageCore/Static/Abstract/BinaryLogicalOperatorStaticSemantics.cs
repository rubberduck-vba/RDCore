namespace RDCore.Runtime.LanguageCore.Static.Abstract;

// MS-VBAL §5.6.9.8
public record class BinaryLogicalOperatorStaticSemantics(string Token) : PlannedOperatorStaticSemantics
{
    protected override string Operator => Token;
}

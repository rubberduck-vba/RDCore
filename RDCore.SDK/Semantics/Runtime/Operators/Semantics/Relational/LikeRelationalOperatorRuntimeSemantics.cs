using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Services.VerboseMessages;
using System.Text;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Relational
{
    /// <summary>
    /// MS-VBAL 5.6.9.6 Binary 'Like' Operator
    /// </summary>
    public sealed record class LikeRelationalOperatorRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
        IVerboseMessageBuilder FormatterService)
        : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
    {
        protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison)
        {
            var regex = ToRegex(rhs, comparison);
            var options = comparison == StringComparison.InvariantCultureIgnoreCase
                ? RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                : RegexOptions.CultureInvariant;

            return Regex.IsMatch(lhs, regex, options);
        }

        protected override bool ComparisonOp(double lhs, double rhs) => throw new NotSupportedException();

        private static string ToRegex(string likePattern, StringComparison comparison)
        {
            StringBuilder regexStr = new();
            for (var i = 0;  i < likePattern.Length; i++)
            {
                var token = likePattern[i];
                switch (token)
                {
                    case '?':
                        regexStr.Append('.');
                        break;
                    case '#':
                        regexStr.Append(@"\d");
                        break;
                    case '*':
                        regexStr.Append(@".*?");
                        break;
                    case '[':
                        if (i + 1 < likePattern.Length && likePattern[i + 1] == '!')
                        {
                            regexStr.Append("[^");
                        }
                        else
                        {
                            regexStr.Append(token);
                        }
                        break;
                    default:
                        regexStr.Append(token);
                        break;

                }
            }
            // Full string match, e.g. "abcd" should NOT match "a.c"
            return $"^{regexStr}$";
        }
    }
}

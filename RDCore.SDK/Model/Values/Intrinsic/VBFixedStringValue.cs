using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic
{
    public sealed record class VBFixedStringValue : VBStringValue
    {
        public VBFixedStringValue(int length, Symbol? symbol = null)
            : base(symbol)
        {
            Length = length;
        }

        public VBFixedStringValue(VBStringValue value)
            : base(value.ResolvedSymbol)
        {
            Length = value.Length;
            Value = FixLength(value.Value, Length);
        }

        public override int Length { get; }

        public VBFixedStringValue WithFixedValue(string value) => new(WithValue(value));

        public override VBStringValue WithValue(string? value)
        {
            var fixedValue = FixLength(value, Length);
            return this with { Value = fixedValue };
        }

        private static string FixLength(string? value, int length)
        {
            // MS-VBAL 5.5.1.2.5 let-coercion to String*length (fixed-length strings)
            value ??= string.Empty;
            if (value.Length > length)
            {
                value = value[..length];
            }
            else if (value.Length < length)
            {
                value = value.PadRight(length, ' ');
            }
            return value;
        }
    }
}
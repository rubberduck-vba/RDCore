using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Runtime.LanguageCore.Plans;

// MS-VBAL §5.5.1.2, §5.2.3.4, §2.2
public sealed record class ConversionPlan(VBType Source, VBType Destination, ConversionRule Rule, bool IsDeferred = false)
{
    public bool IsTypeMismatch => Rule == ConversionRule.None && !IsDeferred;

    public bool IsBySource => Rule is ConversionRule.FromObject or ConversionRule.FromNull or ConversionRule.FromEmpty or ConversionRule.FromError;
}

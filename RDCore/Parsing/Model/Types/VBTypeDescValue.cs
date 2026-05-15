using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

/// <summary>
/// A metatype that describes a type. Not used in many places!
/// </summary>
internal record class VBTypeDescValue(VBType type) : VBTypedValue(type)
{
    public override int Size => sizeof(int);
}

namespace RDCore.Runtime.LanguageCore.Plans;

// MS-VBAL §5.5.1.2
public enum ConversionRule
{
    None,

    // MS-VBAL §5.5.1.2.1
    Numeric,

    // MS-VBAL §5.5.1.2.2
    Boolean,

    // MS-VBAL §5.5.1.2.3
    Date,

    // MS-VBAL §5.5.1.2.4
    String,

    // MS-VBAL §5.5.1.2.5
    FixedString,

    // MS-VBAL §5.5.1.2.6
    ByteArray,

    // MS-VBAL §5.5.1.2.7
    Array,

    // MS-VBAL §5.5.1.2.8
    UserDefinedType,

    // MS-VBAL §5.5.1.2.9
    Error,

    // MS-VBAL §5.5.1.2.10
    Null,

    // MS-VBAL §5.5.1.2.11
    Empty,

    // MS-VBAL §5.5.1.2.12
    Variant,

    // MS-VBAL §5.5.1.2.13
    Object,

    // MS-VBAL §5.5.1.2.13
    FromObject,

    // MS-VBAL §5.5.1.2.10
    FromNull,

    // MS-VBAL §5.5.1.2.11
    FromEmpty,

    // MS-VBAL §5.5.1.2.9
    FromError,
}

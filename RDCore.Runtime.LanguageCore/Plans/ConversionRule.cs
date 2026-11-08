namespace RDCore.Runtime.LanguageCore.Plans;

/// <summary>
/// The rule of <strong>MS-VBAL §5.5.1.2</strong> a let-coercion follows: the destination's, or - for an object, <c>Null</c>, <c>Empty</c> and an
/// <c>Error</c>, which are let-coerced by rules of their own whatever the destination is - the source's.
/// </summary>
public enum ConversionRule
{
    /// <summary>
    /// No rule applies: run-time error 13 (<em>Type mismatch</em>).
    /// </summary>
    None,

    /// <summary>
    /// <strong>§5.5.1.2.1</strong>: to a numeric type.
    /// </summary>
    Numeric,

    /// <summary>
    /// <strong>§5.5.1.2.2</strong>: to <c>Boolean</c>.
    /// </summary>
    Boolean,

    /// <summary>
    /// <strong>§5.5.1.2.3</strong>: to <c>Date</c>.
    /// </summary>
    Date,

    /// <summary>
    /// <strong>§5.5.1.2.4</strong>: to <c>String</c>.
    /// </summary>
    String,

    /// <summary>
    /// <strong>§5.5.1.2.5</strong>: to a fixed-length string.
    /// </summary>
    FixedString,

    /// <summary>
    /// <strong>§5.5.1.2.6</strong>: to a resizable array of <c>Byte</c>.
    /// </summary>
    ByteArray,

    /// <summary>
    /// <strong>§5.5.1.2.7</strong>: to a resizable array of anything else.
    /// </summary>
    Array,

    /// <summary>
    /// <strong>§5.5.1.2.8</strong>: to a user-defined type.
    /// </summary>
    UserDefinedType,

    /// <summary>
    /// <strong>§5.5.1.2.9</strong>: to <c>Error</c>.
    /// </summary>
    Error,

    /// <summary>
    /// <strong>§5.5.1.2.10</strong>: to <c>Null</c>.
    /// </summary>
    Null,

    /// <summary>
    /// <strong>§5.5.1.2.11</strong>: to <c>Empty</c>.
    /// </summary>
    Empty,

    /// <summary>
    /// <strong>§5.5.1.2.12</strong>: to <c>Variant</c>.
    /// </summary>
    Variant,

    /// <summary>
    /// <strong>§5.5.1.2.13</strong>: to <c>Object</c>.
    /// </summary>
    Object,

    /// <summary>
    /// <strong>§5.5.1.2.13</strong>: from an object, <c>Nothing</c> included, to any type: the value of its default member.
    /// </summary>
    FromObject,

    /// <summary>
    /// <strong>§5.5.1.2.10</strong>: from <c>Null</c>, to anything but a <c>Variant</c> or a fixed-size array.
    /// </summary>
    FromNull,

    /// <summary>
    /// <strong>§5.5.1.2.11</strong>: from <c>Empty</c>, to anything but a <c>Variant</c> or a fixed-size array.
    /// </summary>
    FromEmpty,

    /// <summary>
    /// <strong>§5.5.1.2.9</strong>: from an <c>Error</c>, to anything but a <c>Variant</c> or a fixed-size array.
    /// </summary>
    FromError,
}

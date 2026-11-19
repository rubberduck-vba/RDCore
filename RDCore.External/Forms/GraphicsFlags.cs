namespace RDCore.External.Forms;

/// <summary>
/// The first argument of the <c>Line</c>, <c>Circle</c>, <c>PSet</c> and <c>Scale</c> methods of a Visual Basic 6.0 form or PictureBox, which a graphics
/// statement calls: what the statement's syntax says, and which of its optional parts it was written with.
/// </summary>
/// <remarks>
/// The methods take every argument their type library declares, so an argument the statement leaves out is given as 0, and its flag says it was left out.
/// </remarks>
[Flags]
public enum GraphicsFlags : short
{
    /// <summary>
    /// None of the parts below.
    /// </summary>
    None = 0,

    /// <summary>
    /// The first point is written with <c>Step</c>: the point of a <c>PSet</c>, the center of a <c>Circle</c>, the first point of a <c>Line</c>.
    /// </summary>
    FirstStep = 1,

    /// <summary>
    /// The second point of a <c>Line</c> is written with <c>Step</c>.
    /// </summary>
    SecondStep = 2,

    /// <summary>
    /// The statement has its first point: a <c>Line</c> that starts where it says, or a <c>Scale</c> with its two points.
    /// </summary>
    HasFirstPoint = 4,

    /// <summary>
    /// The <c>B</c> of a <c>Line</c>: a box.
    /// </summary>
    Box = 8,

    /// <summary>
    /// The <c>BF</c> of a <c>Line</c>: a box filled with its color.
    /// </summary>
    FilledBox = 16,

    /// <summary>
    /// The statement has its color.
    /// </summary>
    HasColor = 32,

    /// <summary>
    /// A <c>Circle</c> has its start angle.
    /// </summary>
    HasStart = 64,

    /// <summary>
    /// A <c>Circle</c> has its end angle.
    /// </summary>
    HasEnd = 128,

    /// <summary>
    /// A <c>Circle</c> has its aspect.
    /// </summary>
    HasAspect = 256,
}

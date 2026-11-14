using RDCore.SDK.Workspace.Pictures;

namespace RDCore.Runtime.Execution.External.Pictures;

/// <summary>
/// What a <c>stdole.StdPicture</c> holds: the picture's file and what its header says, or no picture at all.
/// </summary>
internal sealed class StdPictureObject
{
    /// <summary>
    /// The picture's format; <see langword="null"/> for an object that holds no picture.
    /// </summary>
    public PictureFormat? Format { get; init; }

    /// <summary>
    /// Whether the object was made by <c>New StdPicture</c> and never given a picture: what <c>SavePicture</c> writes as a file of no bytes, where it refuses the
    /// empty picture <c>LoadPicture</c> gives for no file name.
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>
    /// The picture's file, as it was read.
    /// </summary>
    public byte[] Bytes { get; init; } = [];

    /// <summary>
    /// The image of an icon or a cursor that was loaded, among those its file holds; <see langword="null"/> for any other picture.
    /// </summary>
    public IconImage? Image { get; init; }

    /// <summary>
    /// The picture's width in HIMETRIC units.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// The picture's height in HIMETRIC units.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// The value <c>Handle</c> reads: 0 for no picture.
    /// </summary>
    public int Handle { get; init; }

    /// <summary>
    /// The value <c>hPal</c> reads, which a program can assign.
    /// </summary>
    public int Palette { get; set; }

    /// <summary>
    /// The picture's <c>Type</c>: 0 for none, 1 for a bitmap (a GIF and a JPEG are loaded as one), 2 for a metafile, 3 for an icon or a cursor, 4 for an
    /// enhanced metafile.
    /// </summary>
    public short Type => Format switch
    {
        null => 0,
        PictureFormat.Metafile => 2,
        PictureFormat.Icon or PictureFormat.Cursor => 3,
        PictureFormat.EnhancedMetafile => 4,
        _ => 1,
    };
}

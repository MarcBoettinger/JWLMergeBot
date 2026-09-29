namespace JWLMerge.BackupFileServices.Models.DatabaseModels;

public class IndependentMedia
{
    /// <summary>
    /// The independent media identifier.
    /// </summary>
    public int IndependentMediaId { get; set; }

    /// <summary>
    /// The original file name.
    /// </summary>
    public string OriginalFilename { get; set; } = null!;

    /// <summary>
    /// The file path (name of the media file stored alongside the database inside the .jwlibrary archive).
    /// </summary>
    public string FilePath { get; set; } = null!;

    /// <summary>
    /// The file's MIME type.
    /// </summary>
    public string MimeType { get; set; } = null!;

    /// <summary>
    /// The file's hash (used to detect identical media across backups).
    /// </summary>
    public string Hash { get; set; } = null!;

    public IndependentMedia Clone()
    {
        return (IndependentMedia)MemberwiseClone();
    }
}

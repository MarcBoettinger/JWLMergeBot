namespace JWLMerge.BackupFileServices.Models.DatabaseModels;

public class PlaylistItem
{
    /// <summary>
    /// The playlist item identifier.
    /// </summary>
    public int PlaylistItemId { get; set; }

    /// <summary>
    /// The label of the playlist item.
    /// </summary>
    public string Label { get; set; } = null!;

    /// <summary>
    /// The start trim offset, in ticks.
    /// </summary>
    public int? StartTrimOffsetTicks { get; set; }

    /// <summary>
    /// The end trim offset, in ticks.
    /// </summary>
    public int? EndTrimOffsetTicks { get; set; }

    /// <summary>
    /// The accuracy of the playlist item (refers to PlaylistItemAccuracy.PlaylistItemAccuracyId).
    /// </summary>
    public int Accuracy { get; set; } = 1;

    /// <summary>
    /// The playlist end action.
    /// </summary>
    public int EndAction { get; set; }

    /// <summary>
    /// The file path of the playlist item's thumbnail (refers to IndependentMedia.FilePath).
    /// </summary>
    public string? ThumbnailFilePath { get; set; }

    public PlaylistItem Clone()
    {
        return (PlaylistItem)MemberwiseClone();
    }
}

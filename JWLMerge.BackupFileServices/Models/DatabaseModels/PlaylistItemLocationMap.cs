namespace JWLMerge.BackupFileServices.Models.DatabaseModels;

public class PlaylistItemLocationMap
{
    /// <summary>
    /// The playlist item identifier.
    /// </summary>
    public int PlaylistItemId { get; set; }

    /// <summary>
    /// The location identifier.
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// The major multimedia type.
    /// </summary>
    public int MajorMultimediaType { get; set; }

    /// <summary>
    /// The base media duration, in ticks.
    /// </summary>
    public long? BaseDurationTicks { get; set; }

    public PlaylistItemLocationMap Clone()
    {
        return (PlaylistItemLocationMap)MemberwiseClone();
    }
}

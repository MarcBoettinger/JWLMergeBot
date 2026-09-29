namespace JWLMerge.BackupFileServices.Models.DatabaseModels;

public class PlaylistItemIndependentMediaMap
{
    /// <summary>
    /// The playlist item identifier.
    /// </summary>
    public int PlaylistItemId { get; set; }

    /// <summary>
    /// The independent media identifier.
    /// </summary>
    public int IndependentMediaId { get; set; }

    /// <summary>
    /// The duration, in ticks.
    /// </summary>
    public long DurationTicks { get; set; }

    public PlaylistItemIndependentMediaMap Clone()
    {
        return (PlaylistItemIndependentMediaMap)MemberwiseClone();
    }
}

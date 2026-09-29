namespace JWLMerge.BackupFileServices.Models.DatabaseModels;

public class PlaylistItemMarker
{
    /// <summary>
    /// The playlist item marker identifier.
    /// </summary>
    public int PlaylistItemMarkerId { get; set; }

    /// <summary>
    /// The playlist item identifier.
    /// </summary>
    public int PlaylistItemId { get; set; }

    /// <summary>
    /// The label of the marker.
    /// </summary>
    public string Label { get; set; } = null!;

    /// <summary>
    /// The start time, in ticks.
    /// </summary>
    public int StartTimeTicks { get; set; }

    /// <summary>
    /// The duration, in ticks.
    /// </summary>
    public int DurationTicks { get; set; }

    /// <summary>
    /// The end transition duration, in ticks.
    /// </summary>
    public int EndTransitionDurationTicks { get; set; }

    public PlaylistItemMarker Clone()
    {
        return (PlaylistItemMarker)MemberwiseClone();
    }
}

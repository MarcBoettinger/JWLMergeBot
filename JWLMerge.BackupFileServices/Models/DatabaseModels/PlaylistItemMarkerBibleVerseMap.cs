namespace JWLMerge.BackupFileServices.Models.DatabaseModels;

public class PlaylistItemMarkerBibleVerseMap
{
    /// <summary>
    /// The playlist item marker identifier.
    /// </summary>
    public int PlaylistItemMarkerId { get; set; }

    /// <summary>
    /// The Bible verse identifier.
    /// </summary>
    public int VerseId { get; set; }

    public PlaylistItemMarkerBibleVerseMap Clone()
    {
        return (PlaylistItemMarkerBibleVerseMap)MemberwiseClone();
    }
}

namespace MetaRecord.Data;

/// <summary>
/// Tracks metadata schema versions for change detection.
/// </summary>
public class MetadataVersionEntity
{
    public int Id { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}

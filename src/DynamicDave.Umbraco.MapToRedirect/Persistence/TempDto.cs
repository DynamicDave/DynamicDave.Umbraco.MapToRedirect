using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

/// <summary>A test-run plan or job result, kept for <see cref="TempStore.TimeToLive"/>.</summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class TempDto
{
    public const string TableName = "dynamicDaveMapToRedirectTemp";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("key")]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveMapToRedirectTemp_key")]
    public Guid Key { get; set; }

    /// <summary><see cref="TempKind"/>.</summary>
    [Column("kind")]
    public int Kind { get; set; }

    [Column("createdUtc")]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveMapToRedirectTemp_createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("expiresUtc")]
    public DateTime ExpiresUtc { get; set; }

    [Column("fileName")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? FileName { get; set; }

    /// <summary>JSON or CSV text.</summary>
    [Column("content")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Content { get; set; } = string.Empty;
}

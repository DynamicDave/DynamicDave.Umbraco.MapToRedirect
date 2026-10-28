using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

/// <summary>One applied CSV import.</summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class ImportDto
{
    public const string TableName = "dynamicDaveMapToRedirectImport";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("key")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_dynamicDaveMapToRedirectImport_key")]
    public Guid Key { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("userKey")]
    public Guid UserKey { get; set; }

    [Column("userName")]
    [Length(255)]
    public string UserName { get; set; } = string.Empty;

    [Column("fileName")]
    [Length(255)]
    public string FileName { get; set; } = string.Empty;

    [Column("rootKey")]
    public Guid RootKey { get; set; }

    [Column("culture")]
    [Length(20)]
    public string Culture { get; set; } = string.Empty;

    [Column("overwrite")]
    public bool Overwrite { get; set; }

    [Column("added")]
    public int Added { get; set; }

    [Column("replaced")]
    public int Replaced { get; set; }

    [Column("alreadyPresent")]
    public int AlreadyPresent { get; set; }

    [Column("skipped")]
    public int Skipped { get; set; }

    [Column("failed")]
    public int Failed { get; set; }

    [Column("changedCount")]
    [Constraint(Default = "0")]
    public int Changed { get; set; }

    /// <summary><see cref="ImportState"/>.</summary>
    [Column("state")]
    public int State { get; set; }

    [Column("undoneUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? UndoneUtc { get; set; }

    [Column("undoneBy")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? UndoneBy { get; set; }
}

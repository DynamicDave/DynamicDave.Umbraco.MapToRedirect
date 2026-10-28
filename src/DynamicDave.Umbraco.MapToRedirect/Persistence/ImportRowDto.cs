using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

/// <summary>One executed row of an import.</summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class ImportRowDto
{
    public const string TableName = "dynamicDaveMapToRedirectImportRow";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("importId")]
    [ForeignKey(typeof(ImportDto))]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveMapToRedirectImportRow_importId")]
    public int ImportId { get; set; }

    [Column("rowNumber")]
    public int RowNumber { get; set; }

    [Column("oldRoute")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string OldRoute { get; set; } = string.Empty;

    [Column("culture")]
    [Length(20)]
    public string Culture { get; set; } = string.Empty;

    /// <summary><see cref="Applying.ApplyOutcome"/>.</summary>
    [Column("outcome")]
    public int Outcome { get; set; }

    [Column("newPageKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? NewPageKey { get; set; }

    [Column("replacedPageKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ReplacedPageKey { get; set; }

    /// <summary>Create date of the replaced redirect, so undo can tell whether it is still the same one.</summary>
    [Column("replacedCreateDateUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReplacedCreateDateUtc { get; set; }

    /// <summary>The replaced redirect was culture-less (all languages); it was kept and only overridden.</summary>
    [Column("replacedAllCultures")]
    [Constraint(Default = "0")]
    public bool ReplacedAllCultures { get; set; }

    /// <summary>The row was undone (an undo that stopped midway skips it when retried).</summary>
    [Column("undone")]
    [Constraint(Default = "0")]
    public bool Undone { get; set; }

    [Column("oldUrl")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? OldUrl { get; set; }

    [Column("newUrl")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? NewUrl { get; set; }

    [Column("message")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Message { get; set; }

    /// <summary>Stable code of <see cref="Message"/> (RowMessages); the dashboard localizes it.</summary>
    [Column("messageCode")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? MessageCode { get; set; }
}

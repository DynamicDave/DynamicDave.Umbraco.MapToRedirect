using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

// Picked up by Umbraco's type loader like every PackageMigrationPlan; runs unattended at startup.
public class MapToRedirectMigrationPlan : PackageMigrationPlan
{
    public MapToRedirectMigrationPlan()
        : base(Constants.PackageName)
    {
    }

    protected override void DefinePlan() => To<CreateTables>("dynamicdave-maptoredirect-1-tables")
        .To<SeedJobState>("dynamicdave-maptoredirect-2-job-state");
}

// AsyncMigrationBase exists in Umbraco 17 and 18; MigrationBase was removed in 18.
public class CreateTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists(ImportDto.TableName))
        {
            Create.Table<ImportDto>().Do();
        }

        if (!TableExists(ImportRowDto.TableName))
        {
            Create.Table<ImportRowDto>().Do();
        }

        if (!TableExists(TempDto.TableName))
        {
            Create.Table<TempDto>().Do();
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Creates the job state row (empty value) in umbracoKeyValue. IKeyValueService can only compare-and-set an existing
/// row, so with the row created here every job claim (JobRunner.TryStart) is a compare-and-set, also the first one.
/// Runs once per installation, at startup, before the backoffice can start a job.
/// </summary>
public class SeedJobState(IMigrationContext context, IKeyValueService keyValueService) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (keyValueService.GetValue(Constants.JobStateKey) is null)
        {
            keyValueService.SetValue(Constants.JobStateKey, string.Empty);
        }

        return Task.CompletedTask;
    }
}

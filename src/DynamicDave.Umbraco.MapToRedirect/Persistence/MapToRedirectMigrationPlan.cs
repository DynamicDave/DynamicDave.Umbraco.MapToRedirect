using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace DynamicDave.Umbraco.MapToRedirect.Persistence;

// Picked up by Umbraco's type loader like every PackageMigrationPlan; runs unattended at startup.
public class MapToRedirectMigrationPlan : PackageMigrationPlan
{
    public MapToRedirectMigrationPlan()
        : base(Constants.PackageName)
    {
    }

    protected override void DefinePlan() => To<CreateTables>("dynamicdave-maptoredirect-1-tables");
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

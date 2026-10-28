using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Models;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class JobVisibilityTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static JobState Job(JobStatus status, Guid user, DateTime? completed = null, DateTime? heartbeat = null) =>
        new(Guid.NewGuid(), JobKind.Generate, status, user, "Name", Now.AddHours(-5), heartbeat ?? Now.AddHours(-5), completed,
            0, 0, "", null, null, null, null);

    [Fact]
    public void Running_job_of_another_user_is_visible() =>
        Assert.True(ApiMapping.IsJobVisible(Job(JobStatus.Running, Other, heartbeat: Now), Me, Now));

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    public void Own_finished_job_30_minutes_ago_is_visible(JobStatus status) =>
        Assert.True(ApiMapping.IsJobVisible(Job(status, Me, completed: Now.AddMinutes(-30)), Me, Now));

    [Fact]
    public void Own_finished_job_61_minutes_ago_is_hidden() =>
        Assert.False(ApiMapping.IsJobVisible(Job(JobStatus.Completed, Me, completed: Now.AddMinutes(-61)), Me, Now));

    [Fact]
    public void Finished_job_of_another_user_is_hidden() =>
        Assert.False(ApiMapping.IsJobVisible(Job(JobStatus.Failed, Other, completed: Now.AddMinutes(-1)), Me, Now));

    [Fact]
    public void Aborted_own_job_uses_the_heartbeat_time()
    {
        Assert.True(ApiMapping.IsJobVisible(Job(JobStatus.Aborted, Me, heartbeat: Now.AddMinutes(-30)), Me, Now));
        Assert.False(ApiMapping.IsJobVisible(Job(JobStatus.Aborted, Me, heartbeat: Now.AddMinutes(-61)), Me, Now));
    }

    [Fact]
    public void Aborted_job_of_another_user_is_hidden() =>
        Assert.False(ApiMapping.IsJobVisible(Job(JobStatus.Aborted, Other, heartbeat: Now.AddMinutes(-3)), Me, Now));
}

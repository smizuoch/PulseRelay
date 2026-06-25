using PulseRelay.Desktop.Services;
using Xunit;

namespace PulseRelay.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public async Task Second_instance_cannot_acquire_the_same_application_lock()
    {
        string name = $"PulseRelay.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.Acquire(name);

        Assert.True(first.HasOwnership);

        bool secondOwned = await Task.Run(() =>
        {
            using var second = SingleInstanceGuard.Acquire(name);
            return second.HasOwnership;
        });

        Assert.False(secondOwned);
    }

    [Fact]
    public void Lock_can_be_acquired_again_after_the_owner_exits()
    {
        string name = $"PulseRelay.Tests.{Guid.NewGuid():N}";

        using (var first = SingleInstanceGuard.Acquire(name))
        {
            Assert.True(first.HasOwnership);
        }

        using var next = SingleInstanceGuard.Acquire(name);
        Assert.True(next.HasOwnership);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        string name = $"PulseRelay.Tests.{Guid.NewGuid():N}";
        var guard = SingleInstanceGuard.Acquire(name);

        guard.Dispose();
        guard.Dispose();
    }
}

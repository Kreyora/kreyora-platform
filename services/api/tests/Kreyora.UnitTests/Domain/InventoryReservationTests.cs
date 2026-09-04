using Kreyora.Domain.Inventory;

namespace Kreyora.UnitTests.Domain;

public class InventoryReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Commit_TransitionsOnlyAnUnexpiredActiveReservation()
    {
        var reservation = Create();

        reservation.Commit(Now.AddMinutes(1));

        Assert.Equal(InventoryReservationState.Committed, reservation.State);
        Assert.Equal(Now.AddMinutes(1), reservation.CommittedAt);
        Assert.Throws<InvalidOperationException>(() => reservation.Release(Now.AddMinutes(2)));
    }

    [Fact]
    public void Expire_RejectsEarlyExpiry_AndPreventsCommit()
    {
        var reservation = Create();

        Assert.Throws<InvalidOperationException>(() => reservation.Expire(Now));
        reservation.Expire(Now.AddMinutes(16));

        Assert.Equal(InventoryReservationState.Expired, reservation.State);
        Assert.Throws<InvalidOperationException>(() => reservation.Commit(Now.AddMinutes(16)));
    }

    [Fact]
    public void InventoryItem_ReserveReleaseAndCommit_MaintainAvailability()
    {
        var item = InventoryItem.Create("01J00000000000000000000001", "01J00000000000000000000002");
        item.ApplyMovement(10);

        item.Reserve(7);
        Assert.Equal(3, item.AvailableQuantity);
        item.ReleaseReservation(2);
        item.CommitReservation(5);

        Assert.Equal(5, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(5, item.AvailableQuantity);
    }

    private static InventoryReservation Create() => InventoryReservation.Create(
        "01J00000000000000000000001",
        "01J00000000000000000000002",
        "01J00000000000000000000003",
        2,
        InventoryReservationSource.Manual,
        "manual-hold-1",
        "01J00000000000000000000004",
        Now.AddMinutes(15),
        Now);
}

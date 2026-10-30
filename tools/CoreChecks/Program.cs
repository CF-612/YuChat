using System;
using LingChatXR.Core;

internal static class Program
{
    private static int checks;

    private static int Main()
    {
        try
        {
            PlacementAndDefaultMovement();
            TenGrabReleaseCycles();
            InvalidReleaseAndInput();
            OwnershipAndStaleCommands();
            TouchAndGrabPriority();
            TrackingLossAndReset();
            Console.WriteLine($"PASS: {checks} behavior assertions; no Unity, headset, visual or frame-time claims.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL after {checks} assertions: {exception.Message}");
            return 1;
        }
    }

    private static PetInteraction Placed()
    {
        var pet = new PetInteraction();
        Check(pet.ConfirmPlane(new Vec3(0, 0.75f, 0), 0.4f, 0.3f, new Vec3(0, 0.75f, 0)), "confirm fixture plane");
        return pet;
    }

    private static void PlacementAndDefaultMovement()
    {
        var pet = new PetInteraction();
        Check(pet.State == PetState.Unplaced && !pet.PlaneConfirmed, "initially unplaced");
        Check(!pet.BeginGrab("hand", out _), "cannot grab before placement");
        Check(!pet.BeginTouch("hand"), "cannot touch before placement");
        Check(!pet.ConfirmPlane(default, 0.01f, 0.3f, default), "area smaller than footprint rejected");
        Check(!pet.ConfirmPlane(new Vec3(float.NaN, 0, 0), 1, 1, default), "nonfinite plane rejected");
        Check(!pet.ConfirmPlane(default, float.PositiveInfinity, 1, default), "nonfinite extent rejected");
        Check(!pet.ConfirmPlane(default, 0.4f, 0.3f, new Vec3(0, -0.01f, 0)), "initial contact under plane rejected");
        Check(pet.ConfirmPlane(default, 0.4f, 0.3f, new Vec3(0, 0.02f, 0)), "slightly raised placement accepted");
        Equal(pet.Position, default, "placement projected to confirmed plane");
        Check(!pet.AutonomousEnabled, "autonomous translation is off without a walking asset");
        Check(!pet.TryMoveAutonomous(new Vec3(0.1f, 0, 0), pet.Generation), "default does not slide standing model");
        pet.SetAutonomousEnabled(true);
        Check(pet.TryMoveAutonomous(new Vec3(0.1f, 0, 0), pet.Generation), "explicit animation-driven move accepted");
        Check(!pet.TryMoveAutonomous(new Vec3(0.36f, 0, 0), pet.Generation), "whole footprint remains inside area");
        Equal(pet.Position, new Vec3(0.1f, 0, 0), "rejected move preserves position");
        Console.WriteLine("PASS: placement, footprint and no implicit walking");
    }

    private static void TenGrabReleaseCycles()
    {
        var pet = Placed();
        int oldToken = -1;
        for (int i = 0; i < 10; ++i)
        {
            Check(pet.BeginGrab("left", out int token), $"cycle {i} begin grab");
            Check(token != oldToken, $"cycle {i} distinct input generation");
            var airborne = new Vec3(-0.2f + i * 0.04f, 1.15f, 0.1f);
            Check(pet.MoveGrab("left", token, airborne), $"cycle {i} held pose follows");
            Equal(pet.Position, airborne, $"cycle {i} airborne pose");
            var contact = new Vec3(airborne.X, 0.75f, airborne.Z);
            Check(pet.ReleaseGrab("left", token, contact), $"cycle {i} release");
            Check(pet.LastReleaseWasValid && pet.State == PetState.Idle, $"cycle {i} valid landing idle");
            Equal(pet.Position, contact, $"cycle {i} stays at new position without accumulated drift");
            Equal(pet.LastSafePosition, contact, $"cycle {i} updates safety point");
            Check(!pet.MoveGrab("left", token, default), $"cycle {i} no late move");
            oldToken = token;
        }
        Console.WriteLine("PASS: ten grab/release cycles preserve each new contact position");
    }

    private static void InvalidReleaseAndInput()
    {
        var pet = Placed();
        Vec3[] invalid =
        {
            new Vec3(0.36f, 0.75f, 0),
            new Vec3(0, 0.75f, 0.26f),
            new Vec3(0, 0.74f, 0),
            new Vec3(0, 1f, 0),
            new Vec3(float.NaN, 0.75f, 0),
            new Vec3(0, float.PositiveInfinity, 0),
            new Vec3(float.MaxValue, 0.75f, float.MinValue)
        };
        foreach (Vec3 target in invalid)
        {
            var safe = pet.LastSafePosition;
            Check(pet.BeginGrab("left", out int token), "begin invalid landing case");
            Check(pet.MoveGrab("left", token, new Vec3(0.1f, 1.2f, 0)), "raise before invalid landing");
            Check(pet.ReleaseGrab("left", token, target), "invalid landing still ends current grab");
            Check(!pet.LastReleaseWasValid && pet.State == PetState.Idle, "invalid landing marked and recovered");
            Equal(pet.Position, safe, "invalid release returns to safe position without impulse");
        }
        Check(pet.BeginGrab("left", out int active), "begin finite check");
        var before = pet.Position;
        Check(!pet.MoveGrab("left", active, new Vec3(float.NaN, 0, 0)), "NaN move rejected");
        Equal(pet.Position, before, "nonfinite move preserves valid pose");
        Console.WriteLine("PASS: invalid heights, footprint edges, NaN and infinite input recover safely");
    }

    private static void OwnershipAndStaleCommands()
    {
        var pet = Placed();
        pet.SetAutonomousEnabled(true);
        int staleWalk = pet.Generation;
        Check(pet.BeginGrab("left", out int token), "grab takes spatial ownership");
        Check(!pet.BeginGrab("right", out _), "second hand cannot take active grab");
        Check(!pet.MoveGrab("right", token, new Vec3(0, 1, 0)), "wrong hand cannot move");
        Check(!pet.ReleaseGrab("right", token, pet.LastSafePosition), "wrong hand cannot release");
        Check(!pet.TryMoveAutonomous(new Vec3(0.1f, 0.75f, 0), staleWalk), "grabbing stops autonomous movement");
        Check(pet.ReleaseGrab("left", token, new Vec3(0.2f, 0.75f, 0)), "release updates safe point");
        Check(!pet.TryMoveAutonomous(default, staleWalk), "old walk does not revive after release");
        Check(!pet.ReleaseGrab("left", token, default), "late duplicate release rejected");
        Check(pet.BeginGrab("left", out int next), "next grab begins");
        Check(next != token && !pet.MoveGrab("left", token, default), "previous grab token invalid during new grab");
        pet.SetAutonomousEnabled(false);
        Check(pet.MoveGrab("left", next, new Vec3(0.2f, 1, 0)), "autonomy toggle does not steal grab");
        Console.WriteLine("PASS: owner/generation arbitration rejects old hand and movement tasks");
    }

    private static void TouchAndGrabPriority()
    {
        var pet = Placed();
        pet.SetAutonomousEnabled(true);
        int oldWalk = pet.Generation;
        Check(pet.BeginTouch("finger"), "head touch enters touched state");
        Check(pet.BeginTouch("finger"), "continuous touch is idempotent");
        Check(!pet.BeginTouch("other"), "second touch owner cannot replace current touch");
        Check(!pet.EndTouch("other"), "unrelated touch end ignored");
        Check(!pet.TryMoveAutonomous(new Vec3(0.1f, 0.75f, 0), oldWalk), "touch interrupts movement");
        Check(pet.BeginGrab("left", out int token), "grab preempts touch");
        Check(!pet.EndTouch("finger"), "late touch exit does not cancel grab");
        Check(!pet.BeginTouch("finger"), "touch cannot override held state");
        Check(pet.ReleaseGrab("left", token, pet.LastSafePosition), "release after touch preemption");
        Check(pet.State == PetState.Idle, "old touched state does not return after grab");
        Check(pet.BeginTouch("finger") && pet.EndTouch("finger"), "fresh touch can enter and leave");
        Check(pet.State == PetState.Idle, "touch exit returns idle");
        Console.WriteLine("PASS: touch pauses movement, grab preempts touch, late touch exit is harmless");
    }

    private static void TrackingLossAndReset()
    {
        var pet = Placed();
        Check(pet.BeginGrab("left", out int token), "grab before tracking loss");
        Check(pet.MoveGrab("left", token, new Vec3(0.2f, 1.2f, 0.1f)), "raise before tracking loss");
        pet.Suspend();
        Check(pet.State == PetState.Suspended && !pet.PlaneConfirmed && pet.Holder == null, "tracking loss clears ownership and space");
        Equal(pet.Position, pet.LastSafePosition, "loss restores safe virtual pose");
        Check(!pet.MoveGrab("left", token, default) && !pet.ReleaseGrab("left", token, default), "tracking loss invalidates old grab");
        Check(!pet.BeginGrab("left", out _) && !pet.BeginTouch("finger"), "cannot resume without physical plane confirmation");
        Check(pet.ConfirmPlane(new Vec3(1, 0.8f, 2), 0.4f, 0.3f, new Vec3(1, 0.8f, 2)), "confirm new physical space");
        Check(!pet.MoveGrab("left", token, default), "old input remains stale after reconfirmation");
        Check(pet.BeginGrab("left", out int newToken), "grab in new space");
        pet.Reset();
        Check(pet.State == PetState.Unplaced && !pet.PlaneConfirmed, "reset demands fresh placement");
        Check(!pet.ReleaseGrab("left", newToken, default), "reset invalidates active input");
        Check(pet.ConfirmPlane(default, 0.4f, 0.3f, default), "placement after reset");
        Check(!pet.MoveGrab("left", newToken, default), "reset does not revive old grab");
        Console.WriteLine("PASS: tracking loss, focus/pause suspension and reset require fresh space confirmation");
    }

    private static void Equal(Vec3 actual, Vec3 expected, string message) => Check(actual.Equals(expected), $"{message}: expected {expected}, actual {actual}");

    private static void Check(bool condition, string message)
    {
        ++checks;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

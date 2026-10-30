using System;

namespace LingChatXR.Core
{
    public enum PetState
    {
        Unplaced,
        Idle,
        Touched,
        Held,
        Suspended
    }

    /// <summary>
    /// Sole authority for the pet's spatial root. Call on the scene's main thread.
    /// Position is the calibrated contact point (feet/base), never a desktop offset.
    /// Input adapters own hit tests and pass explicit owner IDs and generation tokens.
    /// </summary>
    public sealed class PetInteraction
    {
        private string toucher;
        private Vec3 planeCenter;
        private float halfWidth;
        private float halfDepth;

        public PetState State { get; private set; } = PetState.Unplaced;
        public Vec3 Position { get; private set; }
        public Vec3 LastSafePosition { get; private set; }
        public bool PlaneConfirmed { get; private set; }
        public int Generation { get; private set; }
        public string Holder { get; private set; }
        public bool LastReleaseWasValid { get; private set; }
        public float RadiusMeters { get; }
        public float ReleaseToleranceMeters { get; }
        public bool AutonomousEnabled { get; private set; }
        public Vec3 PlaneCenter => planeCenter;
        public float HalfWidth => halfWidth;
        public float HalfDepth => halfDepth;

        public PetInteraction(float radiusMeters = 0.05f, float releaseToleranceMeters = 0.04f)
        {
            if (!Vec3.Finite(radiusMeters) || radiusMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            if (!Vec3.Finite(releaseToleranceMeters) || releaseToleranceMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(releaseToleranceMeters));

            RadiusMeters = radiusMeters;
            ReleaseToleranceMeters = releaseToleranceMeters;
        }

        public bool ConfirmPlane(Vec3 center, float newHalfWidth, float newHalfDepth, Vec3 initialPosition)
        {
            if (!center.IsFinite || !initialPosition.IsFinite ||
                !Vec3.Finite(newHalfWidth) || !Vec3.Finite(newHalfDepth) ||
                newHalfWidth < RadiusMeters || newHalfDepth < RadiusMeters ||
                !ValidContact(initialPosition, center, newHalfWidth, newHalfDepth))
                return false;

            InvalidateInputs();
            planeCenter = center;
            halfWidth = newHalfWidth;
            halfDepth = newHalfDepth;
            PlaneConfirmed = true;
            Position = ProjectToPlane(initialPosition);
            LastSafePosition = Position;
            LastReleaseWasValid = false;
            State = PetState.Idle;
            return true;
        }

        public bool BeginTouch(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner) || !PlaneConfirmed ||
                (State != PetState.Idle && State != PetState.Touched))
                return false;

            if (State == PetState.Touched)
                return string.Equals(toucher, owner, StringComparison.Ordinal);

            InvalidateInputs();
            toucher = owner;
            State = PetState.Touched;
            return true;
        }

        public bool EndTouch(string owner)
        {
            if (State != PetState.Touched || !string.Equals(toucher, owner, StringComparison.Ordinal))
                return false;

            InvalidateInputs();
            State = PetState.Idle;
            return true;
        }

        public bool BeginGrab(string owner, out int generation)
        {
            generation = Generation;
            if (string.IsNullOrWhiteSpace(owner) || !PlaneConfirmed ||
                (State != PetState.Idle && State != PetState.Touched))
                return false;

            InvalidateInputs();
            Holder = owner;
            LastReleaseWasValid = false;
            State = PetState.Held;
            generation = Generation;
            return true;
        }

        public bool MoveGrab(string owner, int generation, Vec3 position)
        {
            if (!OwnsGrab(owner, generation) || !position.IsFinite)
                return false;

            Position = position;
            return true;
        }

        /// <summary>
        /// Returns whether this release belongs to the current grab. An invalid landing
        /// is still an accepted release, restoring the last safe point without momentum.
        /// Inspect LastReleaseWasValid to distinguish a valid landing from recovery.
        /// </summary>
        public bool ReleaseGrab(string owner, int generation, Vec3 position)
        {
            if (!OwnsGrab(owner, generation))
                return false;

            bool valid = ValidContact(position, planeCenter, halfWidth, halfDepth);
            Position = valid ? ProjectToPlane(position) : LastSafePosition;
            if (valid)
                LastSafePosition = Position;

            InvalidateInputs();
            LastReleaseWasValid = valid;
            State = PetState.Idle;
            return true;
        }

        public void SetAutonomousEnabled(bool enabled)
        {
            if (enabled == AutonomousEnabled)
                return;
            AutonomousEnabled = enabled;
            // Changing this preference must not steal an active touch or grab.
            if (State == PetState.Idle)
                AdvanceGeneration();
        }

        /// <summary>
        /// Optional animation-driven movement. No timer or automatic translation runs here.
        /// A caller must capture Generation when scheduling movement and supply it on every step.
        /// </summary>
        public bool TryMoveAutonomous(Vec3 destination, int expectedGeneration)
        {
            if (!AutonomousEnabled || State != PetState.Idle || !PlaneConfirmed ||
                expectedGeneration != Generation || !ValidContact(destination, planeCenter, halfWidth, halfDepth))
                return false;

            Position = ProjectToPlane(destination);
            LastSafePosition = Position;
            return true;
        }

        /// <summary>Tracking loss, app pause or focus loss invalidates the physical space.</summary>
        public void Suspend()
        {
            if (PlaneConfirmed)
                Position = LastSafePosition;
            InvalidateInputs();
            PlaneConfirmed = false;
            LastReleaseWasValid = false;
            State = PetState.Suspended;
        }

        /// <summary>Reset requires explicit placement again; no old world pose is resumed.</summary>
        public void Reset()
        {
            InvalidateInputs();
            PlaneConfirmed = false;
            LastReleaseWasValid = false;
            State = PetState.Unplaced;
            Position = default;
            LastSafePosition = default;
            planeCenter = default;
            halfWidth = 0f;
            halfDepth = 0f;
        }

        private bool OwnsGrab(string owner, int generation) =>
            State == PetState.Held && PlaneConfirmed && generation == Generation &&
            string.Equals(Holder, owner, StringComparison.Ordinal);

        private bool ValidContact(Vec3 value, Vec3 center, float width, float depth)
        {
            if (!value.IsFinite)
                return false;

            // Double arithmetic prevents overflow for hostile but finite input coordinates.
            double y = (double)value.Y - center.Y;
            double x = Math.Abs((double)value.X - center.X);
            double z = Math.Abs((double)value.Z - center.Z);
            return y >= 0d && y <= ReleaseToleranceMeters &&
                x <= (double)width - RadiusMeters && z <= (double)depth - RadiusMeters;
        }

        private Vec3 ProjectToPlane(Vec3 value) => new Vec3(value.X, planeCenter.Y, value.Z);

        private void InvalidateInputs()
        {
            Holder = null;
            toucher = null;
            AdvanceGeneration();
        }

        private void AdvanceGeneration()
        {
            // Exhaustion would need billions of interactions; fail closed instead of reusing a token.
            Generation = checked(Generation + 1);
        }
    }
}

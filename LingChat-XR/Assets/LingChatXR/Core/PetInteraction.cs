using System;

namespace LingChatXR.Core
{
    public enum PetState
    {
        Unplaced,
        Idle,
        Touched,
        Held,
        Descending,
        Settling,
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
        private float planeYaw;
        private Vec3 descentStart;
        private Vec3 descentTarget;
        private float descentDuration;
        private float descentElapsed;
        public const float Gravity = 9.81f;
        public float VerticalVelocity { get; private set; }
        public float LandingSpeed { get; private set; }
        public float SettleAmount { get; private set; }

        public PetState State { get; private set; } = PetState.Unplaced;
        public Vec3 Position { get; private set; }
        public Vec3 LastSafePosition { get; private set; }
        public bool PlaneConfirmed { get; private set; }
        public int Generation { get; private set; }
        public string Holder { get; private set; }
        public bool LastReleaseWasValid { get; private set; }
        public float RadiusMeters { get; private set; }
        public float ReleaseToleranceMeters { get; }
        public bool AutonomousEnabled { get; private set; }
        public Vec3 PlaneCenter => planeCenter;
        public float HalfWidth => halfWidth;
        public float HalfDepth => halfDepth;
        public float PlaneYawRadians => planeYaw;

        public PetInteraction(float radiusMeters = 0.05f, float releaseToleranceMeters = 0.04f)
        {
            if (!Vec3.Finite(radiusMeters) || radiusMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            if (!Vec3.Finite(releaseToleranceMeters) || releaseToleranceMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(releaseToleranceMeters));

            RadiusMeters = radiusMeters;
            ReleaseToleranceMeters = releaseToleranceMeters;
        }

        public bool ConfirmPlane(Vec3 center, float newHalfWidth, float newHalfDepth, Vec3 initialPosition, float yawRadians=0)
        {
            if (!center.IsFinite || !initialPosition.IsFinite ||
                !Vec3.Finite(newHalfWidth) || !Vec3.Finite(newHalfDepth) || !Vec3.Finite(yawRadians) ||
                newHalfWidth < RadiusMeters || newHalfDepth < RadiusMeters ||
                !ValidContact(initialPosition, center, newHalfWidth, newHalfDepth, yawRadians, RadiusMeters))
                return false;

            InvalidateInputs();
            planeCenter = center;
            halfWidth = newHalfWidth;
            halfDepth = newHalfDepth;
            planeYaw = yawRadians;
            PlaneConfirmed = true;
            Position = ProjectToPlane(initialPosition);
            LastSafePosition = Position;
            LastReleaseWasValid = false;
            State = PetState.Idle;
            return true;
        }

        // 尺寸编辑只调整占地，不改变空间根；长大后越过桌边时拒绝这次预览。
        public bool TryResizeFootprint(float radius)
        {
            if(!Vec3.Finite(radius)||radius<=0||!PlaneConfirmed||(State!=PetState.Idle&&State!=PetState.Touched))return false;
            if(!ContainsFootprint(Position,radius))return false;
            RadiusMeters=radius;if(State==PetState.Touched)InvalidateInputs();State=PetState.Idle;return true;
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

        public void CancelTouch(){if(State==PetState.Touched)EndTouch(toucher);}
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
                (State != PetState.Idle && State != PetState.Touched && State != PetState.Descending && State != PetState.Settling))
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

            bool valid = ValidContact(position, planeCenter, halfWidth, halfDepth,planeYaw,RadiusMeters);
            Position = valid ? ProjectToPlane(position) : LastSafePosition;
            if (valid)
                LastSafePosition = Position;

            InvalidateInputs();
            LastReleaseWasValid = valid;
            State = PetState.Idle;
            return true;
        }

        /// <summary>Editor 选择的接触点仍须满足 MR 接触/足迹校验；空间根从当前持有位置受控下降。</summary>
        public bool ReleaseGrabWithDescent(string owner, int generation, Vec3 contact)
        {
            if (!OwnsGrab(owner, generation)) return false;
            bool valid = ValidContact(contact, planeCenter, halfWidth, halfDepth,planeYaw,RadiusMeters) && Position.IsFinite &&
                Position.Y >= planeCenter.Y && (double)Position.Y - planeCenter.Y <= 2 &&
                Math.Abs((double)Position.X - contact.X) <= 2 && Math.Abs((double)Position.Z - contact.Z) <= 2;
            // 松手永不直接写回落点。无效目标回到安全支撑点，同样保留连续轨迹。
            descentStart = Position; descentTarget = valid ? ProjectToPlane(contact) : LastSafePosition;
            descentDuration = (float)Math.Sqrt(2 * Math.Max(0, Position.Y - planeCenter.Y) / Gravity);
            InvalidateInputs(); descentElapsed = 0; SettleAmount = 0;
            VerticalVelocity = 0; LandingSpeed = 0;
            LastReleaseWasValid = valid; State = PetState.Descending;
            return true;
        }

        public bool StepDescent(float deltaTime, int expectedGeneration)
        {
            if (!PlaneConfirmed || expectedGeneration != Generation || !Vec3.Finite(deltaTime) || deltaTime <= 0 ||
                (State != PetState.Descending && State != PetState.Settling)) return false;
            // 暂停/大帧恢复限步，避免将积压时间一次倾倒到角色和次级物理。
            descentElapsed += Math.Min(deltaTime, .1f);
            if (State == PetState.Descending)
            {
                float seconds = Math.Min(descentDuration, descentElapsed);
                float t = descentDuration > 0 ? seconds / descentDuration : 1;
                VerticalVelocity = -Gravity * seconds;
                Position = new Vec3(descentStart.X + (descentTarget.X - descentStart.X) * t,
                    Math.Max(descentTarget.Y, descentStart.Y - .5f * Gravity * seconds * seconds),
                    descentStart.Z + (descentTarget.Z - descentStart.Z) * t);
                if (t >= 1) { LandingSpeed = -VerticalVelocity; VerticalVelocity = 0; Position = descentTarget; LastSafePosition = Position; State = PetState.Settling; descentElapsed = 0; }
            }
            else
            {
                float t = Math.Min(1, descentElapsed / .24f);
                SettleAmount = (float)Math.Pow(Math.Sin(Math.PI * t), 2);
                if (t >= 1) { SettleAmount = 0; InvalidateInputs(); State = PetState.Idle; }
            }
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
                expectedGeneration != Generation || !ValidContact(destination, planeCenter, halfWidth, halfDepth,planeYaw,RadiusMeters))
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
            planeYaw = 0f;
        }

        private bool OwnsGrab(string owner, int generation) =>
            State == PetState.Held && PlaneConfirmed && generation == Generation &&
            string.Equals(Holder, owner, StringComparison.Ordinal);

        public bool ContainsFootprint(Vec3 point,float radius)
        {
            if(!PlaneConfirmed||!point.IsFinite||!Vec3.Finite(radius)||radius<=0)return false;
            return ValidContact(new Vec3(point.X,planeCenter.Y,point.Z),planeCenter,halfWidth,halfDepth,planeYaw,radius);
        }

        private bool ValidContact(Vec3 value, Vec3 center, float width, float depth,float yaw,float radius)
        {
            if (!value.IsFinite)
                return false;

            // Double arithmetic prevents overflow for hostile but finite input coordinates.
            double y = (double)value.Y - center.Y;
            double dx=(double)value.X-center.X,dz=(double)value.Z-center.Z;
            double cosine=Math.Cos(yaw),sine=Math.Sin(yaw);
            double x = Math.Abs(dx*cosine-dz*sine);
            double z = Math.Abs(dx*sine+dz*cosine);
            return y >= 0d && y <= ReleaseToleranceMeters &&
                x <= (double)width - radius && z <= (double)depth - radius;
        }

        private Vec3 ProjectToPlane(Vec3 value) => new Vec3(value.X, planeCenter.Y, value.Z);

        private void InvalidateInputs()
        {
            Holder = null;
            toucher = null;
            SettleAmount = 0;
            VerticalVelocity = 0;
            AdvanceGeneration();
        }

        private void AdvanceGeneration()
        {
            // Exhaustion would need billions of interactions; fail closed instead of reusing a token.
            Generation = checked(Generation + 1);
        }
    }
}

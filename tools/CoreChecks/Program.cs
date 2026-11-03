using System;
using LingChatXR.Core;

internal static class Program
{
    private static int checks;

    private static int Main()
    {
        try
        {
            GravityRelease();
            PointGesture();
            SizeEditing();
            RotatedSurface();
            DialogueTyping();
            PlacementAndDefaultMovement();
            TenGrabReleaseCycles();
            InvalidReleaseAndInput();
            OwnershipAndStaleCommands();
            TouchAndGrabPriority();
            TrackingLossAndReset();
            DialogueOrdering();
            ControlledDescent();
            Console.WriteLine($"PASS: {checks} behavior assertions; no Unity, headset, visual or frame-time claims.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL after {checks} assertions: {exception.Message}");
            return 1;
        }
    }

    private static void RotatedSurface()
    {
        foreach(float angle in new[]{0f,(float)Math.PI/4,(float)Math.PI/2,-(float)Math.PI/3})
        {
            var center=new Vec3(.6f,1.1f,.55f);var pet=new PetInteraction(.04f);
            Vec3 Point(float x,float z,float y=0)=>new Vec3(center.X+(float)(x*Math.Cos(angle)+z*Math.Sin(angle)),center.Y+y,center.Z+(float)(-x*Math.Sin(angle)+z*Math.Cos(angle)));
            Check(pet.ConfirmPlane(center,.6f,.2f,center,angle),"rotated surface accepts center");
            Check(pet.ContainsFootprint(Point(.5f,.1f),.04f),"rotated footprint inside");
            Check(!pet.ContainsFootprint(Point(.58f,0),.04f),"rotated width edge rejected");
            Check(!pet.ContainsFootprint(Point(0,.18f),.04f),"rotated depth edge rejected");
            pet.SetAutonomousEnabled(true);Check(pet.TryMoveAutonomous(Point(.5f,.1f),pet.Generation),"autonomy uses rotated bounds");
            Check(!pet.TryResizeFootprint(.11f),"rotated resize respects near edge");
            Check(pet.BeginGrab("hand",out int token)&&pet.MoveGrab("hand",token,Point(.4f,0,.4f)),"grab above rotated table");
            Check(pet.ReleaseGrabWithDescent("hand",token,Point(.4f,0))&&pet.LastReleaseWasValid,"rotated descent contact valid");
            for(int i=0;i<100&&pet.State!=PetState.Idle;i++)pet.StepDescent(.02f,pet.Generation);
            Equal(pet.Position,Point(.4f,0),"rotated descent preserves world landing");
            Check(!pet.ConfirmPlane(center,.6f,.2f,center,float.NaN),"nonfinite rotation rejected");
        }
    }
    private static void PointGesture()
    {
        var dwell=new PointDwell();var goal=new Vec3(.2f,.74f,.1f);int sent=0;
        for(int i=0;i<10;i++)if(dwell.Step(true,goal,.05f,out _))sent++;Check(sent==0,"brief pointing does not command movement");
        for(int i=0;i<60;i++)if(dwell.Step(true,goal,.05f,out _))sent++;Check(sent==1,"stable pointing confirms once, holding does not repeat");
        dwell.Step(false,goal,.05f,out _);for(int i=0;i<20;i++)if(dwell.Step(true,goal,.05f,out _))sent++;Check(sent==1,"single missing frame does not rearm pointing");
        for(int i=0;i<5;i++)dwell.Step(false,goal,.05f,out _);for(int i=0;i<20;i++)if(dwell.Step(true,goal,.05f,out _))sent++;Check(sent==2,"release then point re-arms");
        dwell.Reset();for(int i=0;i<100;i++)Check(!dwell.Step(true,new Vec3(i%2==0?0:.03f,.74f,0),.05f,out _),"moving target never confirms");
        dwell.Reset();for(int i=0;i<50;i++)Check(!dwell.Step(true,new Vec3(float.NaN,0,0),.05f,out _),"nonfinite pointer rejected");
    }

    private static void SizeEditing()
    {
        var pet=new PetInteraction(.04f);Check(!pet.TryResizeFootprint(.06f),"unplaced resize rejected");
        Check(pet.ConfirmPlane(default,.4f,.3f,new Vec3(.34f,0,0)),"resize plane");
        Check(!pet.TryResizeFootprint(.07f)&&pet.RadiusMeters==.04f,"growth crossing edge rejected without mutation");
        Vec3 position=pet.Position;Check(pet.TryResizeFootprint(.05f),"valid growth");Equal(pet.Position,position,"resize preserves world position");
        Check(!pet.TryResizeFootprint(float.NaN)&&!pet.TryResizeFootprint(0),"invalid radius rejected");
        Check(pet.BeginGrab("mouse",out _),"grab resized pet");Check(!pet.TryResizeFootprint(.03f),"held resize rejected");
        pet.Suspend();Check(!pet.TryResizeFootprint(.03f),"suspended resize rejected");
    }
    private static void GravityRelease()
    {
        foreach (int fps in new[] {30, 60, 90})
        {
            var pet = Placed(); pet.BeginGrab("hand", out int g);
            pet.MoveGrab("hand", g, new Vec3(0, 1.75f, 0));
            pet.ReleaseGrabWithDescent("hand", g, new Vec3(0, .75f, 0));
            Check(pet.Position.Y == 1.75f, "release must preserve airborne position");
            for (int i=0;i<fps/5;i++) pet.StepDescent(1f/fps, pet.Generation);
            Check(Math.Abs(pet.Position.Y - (1.75f - .5f*9.81f*.2f*.2f)) < .002f,
                "gravity must fall 19.62 cm after 0.2 s, not tween or teleport; actual=" + pet.Position.Y);
        }
    }
    private static void DialogueTyping()
    {
        var writer=new DialoguePresentation();
        Check(writer.Text==""&&!writer.Typing,"no text before presentation begins");
        writer.Start("你好，小肥鱼！");Check(writer.Text=="你"&&writer.Typing,"audio start reveals only first character");
        writer.Step(.01);Check(writer.Text=="你","normal speed must not reveal full sentence in one frame");
        writer.Finish();Check(writer.Text=="你好，小肥鱼！"&&!writer.Typing,"first click finishes whole sentence");
        writer.Start("今天开心吗？",true);Check(writer.Text.StartsWith("你好，小肥鱼！ 今")&&writer.Typing,"merged short sentence appends instead of erasing");
        writer.Cancel();writer.Step(10);Check(writer.Text==""&&!writer.Typing,"cancel rejects late typewriter ticks");
        writer.Start("🐟你好");Check(writer.Text=="🐟","do not split surrogate pair");
        for(int i=0;i<300;i++)writer.Step(.01);Check(writer.Text=="🐟你好"&&!writer.Typing,"typing completes naturally");
    }

    private static void DialogueOrdering()
    {
        var q = new DialogueQueue();
        q.Begin("session", "fish", "turn", 2);
        Check(!q.Add("session", "fish", "turn", 1, new DialogueQueue.Cue { Index = 0 }), "reject late generation");
        Check(!q.Add("session", "other", "turn", 2, new DialogueQueue.Cue { Index = 0 }), "reject other role");
        Check(q.Add("session", "fish", "turn", 2, new DialogueQueue.Cue { Index = 1, Text = "second" }), "buffer out of order");
        Check(q.Take() == null, "never play second before first");
        Check(q.Add("session", "fish", "turn", 2, new DialogueQueue.Cue { Index = 0, Text = "first" }), "accept first");
        Check(!q.Add("session", "fish", "turn", 2, new DialogueQueue.Cue { Index = 0 }), "deduplicate audio");
        Check(q.Take().Text == "first", "first subtitle stays paired");
        Check(q.Take() == null, "no overlapping voice");
        q.End(); Check(!q.Empty, "stream end is not speech end");
        q.Complete(); Check(q.Take().Text == "second", "advance only after completion");
        q.Complete(); Check(q.Empty && q.Ended, "finished playback queue");
        q.Cancel(); Check(!q.Add("session", "fish", "turn", 2, new DialogueQueue.Cue { Index = 2 }), "cancel rejects delayed audio");
        q.Begin("new-session", "other", "new-turn", 3);
        Check(q.Empty && !q.Ended, "switch clears old queue");
        q.End(); Check(q.Empty && q.Ended, "empty final completes");
    }

    private static PetInteraction Placed()
    {
        var pet = new PetInteraction();
        Check(pet.ConfirmPlane(new Vec3(0, 0.75f, 0), 0.4f, 0.3f, new Vec3(0, 0.75f, 0)), "confirm fixture plane");
        return pet;
    }

    private static void ControlledDescent()
    {
        foreach (int fps in new[] { 30, 60, 90 })
        {
            var p = Placed(); p.SetAutonomousEnabled(true);
            for (int cycle = 0; cycle < 10; cycle++)
            {
                Check(p.BeginGrab("hand", out int grab), "grab before descent");
                float x = cycle % 2 == 0 ? .1f : -.1f;
                p.MoveGrab("hand", grab, new Vec3(x, 1.25f, 0));
                Check(p.ReleaseGrabWithDescent("hand", grab, new Vec3(x, .75f, 0)), "start controlled release");
                Check(p.Position.Y == 1.25f && p.State == PetState.Descending, "release has no teleport or early idle");
                Check(!p.BeginTouch("touch"), "touch does not steal descent");
                Check(!p.TryMoveAutonomous(new Vec3(0,.75f,0),p.Generation), "no walk while descending");
                float previousY = p.Position.Y; int trajectory = p.Generation; int ticks = 0; bool settled = false;
                while (p.State != PetState.Idle && ticks++ < fps * 3)
                {
                    Check(p.StepDescent(1f / fps, trajectory), "step owns trajectory");
                    Check(p.Position.Y >= .75f - .000001f && p.Position.Y <= previousY + .000001f, "monotonic descent without penetration");
                    if (p.State == PetState.Settling) settled = true;
                    previousY = p.Position.Y;
                }
                Check(settled && p.State == PetState.Idle && p.Position.Equals(new Vec3(x,.75f,0)), "contact settling then exact idle");
            }
            p.BeginGrab("hand", out int g); p.MoveGrab("hand", g, new Vec3(0,1.1f,0));
            p.ReleaseGrabWithDescent("hand", g, new Vec3(0,.75f,0)); int old = p.Generation;
            p.StepDescent(.1f, old); Vec3 mid = p.Position;
            Check(p.BeginGrab("new-hand", out int newer) && p.Position.Equals(mid), "regrab during descent has no jump");
            Check(!p.StepDescent(.1f,old) && !p.MoveGrab("hand",g,new Vec3(0,2,0)), "old trajectory and old hand rejected");
            p.MoveGrab("new-hand",newer,new Vec3(.1f,1,0));p.ReleaseGrabWithDescent("new-hand",newer,new Vec3(.39f,.75f,0));
            Check(p.State==PetState.Descending&&!p.LastReleaseWasValid,"invalid footprint returns continuously, never teleports");
            for(int i=0;i<100;i++) p.StepDescent(1f/fps,p.Generation);
            p.BeginGrab("hand",out g);p.MoveGrab("hand",g,new Vec3(0,1,0));p.ReleaseGrabWithDescent("hand",g,new Vec3(0,.75f,0));old=p.Generation;
            Check(!p.StepDescent(float.NaN,old),"invalid delta rejected");p.Suspend();
            Check(!p.StepDescent(.1f,old)&&!p.PlaneConfirmed,"space loss cancels descent");
        }
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

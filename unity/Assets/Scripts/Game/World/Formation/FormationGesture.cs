// FormationGesture.cs - the press, drag and release that draws a formation,
// as a state machine fed one PointerFrame a frame. A press that can start
// one waits. Released before the threshold it is a click, which the old
// path carries out. The classic right button, a cancel, must also be held
// a moment first, so a flick while the hand moves on stays a cancel. Past
// it the drag is live until the release sends the order, or Escape, the
// other button, lost focus or a missed release aborts it. A press on an
// enemy stays the attack it always was. Pure, so a test feeds it frames.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // One frame of the pointer and the keys the gesture reads.
    public struct PointerFrame
    {
        public Vector2 Screen;
        public bool LeftDown, LeftHeld, LeftUp, RightDown, RightHeld, RightUp;
        public bool Shift, Ctrl, Alt;
        public bool TabDown, FDown, GDown, EscapeDown;
        public bool Focused;
        public bool OverUi, OnGround;
        public bool OnEnemy;            // an enemy unit is under the pointer
        public Vector3 Ground;
        public float Dpi;
        public float Seconds;           // unscaled time of the frame

        public bool Down(int b) => b == 0 ? LeftDown : RightDown;
        public bool Held(int b) => b == 0 ? LeftHeld : RightHeld;
        public bool Up(int b) => b == 0 ? LeftUp : RightUp;
    }

    public enum GestureState { Idle, Pending, Live }

    public enum GestureEvent { None, Pending, Click, Started, Dragging, Committed, Aborted }

    public sealed class FormationGesture
    {
        public bool Classic = true;
        public bool ClassicRightDrag = true;
        // The shape sticks from drag to drag.
        public FormationShape Shape = FormationShape.Line;

        public GestureState State { get; private set; }
        public int Button { get; private set; } = -1;
        public PointerFrame Press { get; private set; }
        // Per drag: F faces about, G flips the pace. Alt and Shift as last seen.
        public bool FaceAbout { get; private set; }
        public bool PaceFlip { get; private set; }
        public bool Alt { get; private set; }
        public bool Shift { get; private set; }

        public bool Live => State == GestureState.Live;
        public bool Busy => State != GestureState.Idle;

        // The pixels a press must travel to become a drag, scaled for the screen.
        public float Threshold(int button, float dpi)
        {
            float px = Classic && button == 1 ? FormationTuning.ClassicRightDragPixels : FormationTuning.DragPixels;
            return px * Mathf.Max(1f, (dpi > 0 ? dpi : 96f) / 96f);
        }

        // canStart: some selected unit takes part. armed: a command waits for a click.
        public GestureEvent Feed(in PointerFrame f, bool canStart, bool armed)
        {
            switch (State)
            {
                case GestureState.Idle:
                {
                    int b = StartButton(f);
                    if (b < 0 || !canStart || armed || f.OverUi || !f.OnGround) return GestureEvent.None;
                    // Not while the other button drags a box, nor on an enemy
                    // with an order button: that press attacks.
                    if (f.Held(1 - b) || f.OnEnemy && !(Classic && b == 1)) return GestureEvent.None;
                    State = GestureState.Pending;
                    Button = b;
                    Press = f;
                    FaceAbout = PaceFlip = false;
                    Alt = f.Alt;
                    Shift = f.Shift;
                    return GestureEvent.Pending;
                }
                case GestureState.Pending:
                    if (armed || f.EscapeDown || LostFocus(f) || f.Down(1 - Button)) return Abort();
                    if (f.Up(Button)) { State = GestureState.Idle; return GestureEvent.Click; }
                    if (!f.Held(Button)) return Abort();
                    if ((f.Screen - Press.Screen).magnitude < Threshold(Button, f.Dpi)) return GestureEvent.Pending;
                    if (Classic && Button == 1 && f.Seconds - Press.Seconds < FormationTuning.ClassicRightHoldSeconds) return GestureEvent.Pending;
                    State = GestureState.Live;
                    Keys(f);
                    return GestureEvent.Started;
                default:
                    if (f.EscapeDown || LostFocus(f) || f.Down(1 - Button)) return Abort();
                    Keys(f);
                    if (f.Up(Button)) { State = GestureState.Idle; return GestureEvent.Committed; }
                    if (!f.Held(Button)) return Abort();
                    return GestureEvent.Dragging;
            }
        }

        public GestureEvent Abort()
        {
            State = GestureState.Idle;
            return GestureEvent.Aborted;
        }

        // Modern: the right button. Classic: Ctrl with the left, or the right.
        int StartButton(in PointerFrame f)
        {
            if (!Classic) return f.RightDown ? 1 : -1;
            if (f.LeftDown && f.Ctrl) return 0;
            if (f.RightDown && ClassicRightDrag) return 1;
            return -1;
        }

        bool LostFocus(in PointerFrame f) => Press.Focused && !f.Focused;

        void Keys(in PointerFrame f)
        {
            // Tab waits while Alt is down, so Alt+Tab stays the system's.
            if (f.TabDown && !f.Alt) Shape = FormationPlanner.Next(Shape);
            if (f.FDown) FaceAbout = !FaceAbout;
            if (f.GDown) PaceFlip = !PaceFlip;
            Alt = f.Alt;
            Shift = f.Shift;
        }
    }
}

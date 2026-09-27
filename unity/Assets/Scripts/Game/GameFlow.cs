// GameFlow.cs - the screens of the game as a small state machine: main
// menu, skirmish setup, options, loading, playing, paused, and the end
// screens. Pure C#, so the rules are tested without a scene. The screens
// listen to Changed and draw whatever state it names.
using System;
using System.Collections.Generic;

namespace OpenKingdomsUnity.Game
{
    public enum FlowState { MainMenu, Skirmish, Options, Loading, Playing, Paused, Victory, Defeat, Quit }

    public enum FlowEvent
    {
        OpenSkirmish, OpenOptions, Back, Start, Loaded, LoadFailed,
        Pause, Resume, Won, Lost, ToMenu, Exit,
    }

    public sealed class GameFlow
    {
        public FlowState State { get; private set; } = FlowState.MainMenu;
        // Where Back from Options returns: the main menu or the pause menu.
        public FlowState OptionsReturn { get; private set; } = FlowState.MainMenu;
        public event Action<FlowState, FlowState> Changed;

        static readonly Dictionary<(FlowState, FlowEvent), FlowState> Table = new Dictionary<(FlowState, FlowEvent), FlowState>
        {
            { (FlowState.MainMenu, FlowEvent.OpenSkirmish), FlowState.Skirmish },
            { (FlowState.MainMenu, FlowEvent.OpenOptions), FlowState.Options },
            { (FlowState.MainMenu, FlowEvent.Exit), FlowState.Quit },
            { (FlowState.Skirmish, FlowEvent.Back), FlowState.MainMenu },
            { (FlowState.Skirmish, FlowEvent.Start), FlowState.Loading },
            { (FlowState.Loading, FlowEvent.Loaded), FlowState.Playing },
            { (FlowState.Loading, FlowEvent.LoadFailed), FlowState.Skirmish },
            { (FlowState.Playing, FlowEvent.Pause), FlowState.Paused },
            { (FlowState.Playing, FlowEvent.Won), FlowState.Victory },
            { (FlowState.Playing, FlowEvent.Lost), FlowState.Defeat },
            { (FlowState.Paused, FlowEvent.Resume), FlowState.Playing },
            { (FlowState.Paused, FlowEvent.OpenOptions), FlowState.Options },
            { (FlowState.Paused, FlowEvent.ToMenu), FlowState.MainMenu },
            { (FlowState.Victory, FlowEvent.ToMenu), FlowState.MainMenu },
            { (FlowState.Defeat, FlowEvent.ToMenu), FlowState.MainMenu },
        };

        public bool CanFire(FlowEvent e)
        {
            if (State == FlowState.Options && e == FlowEvent.Back) return true;
            return Table.ContainsKey((State, e));
        }

        // Moves to the next state, or returns false and stays put when the
        // event means nothing here.
        public bool Fire(FlowEvent e)
        {
            FlowState next;
            if (State == FlowState.Options && e == FlowEvent.Back) next = OptionsReturn;
            else if (!Table.TryGetValue((State, e), out next)) return false;
            if (next == FlowState.Options) OptionsReturn = State;
            var was = State;
            State = next;
            Changed?.Invoke(was, next);
            return true;
        }

        // A game is on the table: the world exists in these states.
        public static bool InGame(FlowState s) =>
            s == FlowState.Playing || s == FlowState.Paused || s == FlowState.Victory || s == FlowState.Defeat;

        public bool InGameNow => InGame(State) || (State == FlowState.Options && OptionsReturn == FlowState.Paused);
    }
}

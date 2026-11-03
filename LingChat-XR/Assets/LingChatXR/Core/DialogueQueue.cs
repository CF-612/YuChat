using System;
using System.Collections.Generic;
namespace LingChatXR.Core
{
    // One generation, one ordered sentence queue. Transport and Unity audio are adapters.
    public sealed class DialogueQueue
    {
        public sealed class Cue { public int Index; public string Text; public string Audio; public string Emotion; public string Motion; public bool Failed; }
        private readonly SortedDictionary<int, Cue> pending = new SortedDictionary<int, Cue>();
        private readonly HashSet<int> accepted = new HashSet<int>();
        public string Session { get; private set; }
        public string Turn { get; private set; }
        public string Role { get; private set; }
        public long Generation { get; private set; }
        public bool Ended { get; private set; }
        public int NextIndex { get; private set; }
        public bool Playing { get; private set; }
        public bool Empty => pending.Count == 0 && !Playing;
        public Cue Peek() => pending.TryGetValue(NextIndex, out var cue) ? cue : null;
        public void Begin(string session, string role, string turn, long generation, int firstIndex = 0)
        { Session = session; Role = role; Turn = turn; Generation = generation; NextIndex = firstIndex; Ended = false; Playing = false; pending.Clear(); accepted.Clear(); }
        public bool Matches(string session, string role, string turn, long generation) => Session == session && Role == role && Turn == turn && Generation == generation;
        public bool Add(string session, string role, string turn, long generation, Cue cue)
        {
            if (!Matches(session, role, turn, generation) || cue.Index < NextIndex || !accepted.Add(cue.Index)) return false;
            pending.Add(cue.Index, cue); return true;
        }
        public Cue Take()
        {
            if (Playing || !pending.TryGetValue(NextIndex, out var cue)) return null;
            pending.Remove(NextIndex++); Playing = true; return cue;
        }
        public void Complete() => Playing = false;
        public void End() => Ended = true;
        public void Cancel() { Generation++; pending.Clear(); accepted.Clear(); Playing = false; Ended = true; }
    }
}

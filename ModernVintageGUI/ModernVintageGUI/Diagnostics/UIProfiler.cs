using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace IS2Mod.Diagnostics
{
    /// <summary>
    /// Counts and times the work the layout and the drawing do, per control type.
    ///
    /// It exists because "the dialog feels slow" is not something you can fix by reading code:
    /// the layout walks the tree several times over and every control draws itself, so the
    /// answer is always somewhere in a number that nobody has looked at. This produces those
    /// numbers, in the game and in the layout harness alike.
    ///
    /// Off by default and switched on by hand. While it is off the cost is one static bool read
    /// at each call site, which is why the instrumentation can sit in the hot paths at all:
    ///
    /// <code>
    /// UIProfiler.Reset();
    /// UIProfiler.Enabled = true;
    /// dialog.PerformLayout();
    /// UIProfiler.Enabled = false;
    /// capi.ShowChatMessage(UIProfiler.Report());
    /// </code>
    ///
    /// Not thread safe, and it does not need to be: everything it measures runs on the render
    /// thread.
    /// </summary>
    public static class UIProfiler
    {
        /// <summary>Whether anything is being recorded. Read on every instrumented call.</summary>
        public static bool Enabled;

        private struct Bucket
        {
            public long Calls;
            public long Ticks;
            public long SelfTicks;
        }

        private static readonly Dictionary<string, Bucket> Buckets = new Dictionary<string, Bucket>();

        /// <summary>
        /// How long the calls nested inside the one currently running took.
        ///
        /// This is what separates "this control is slow" from "this control has fifty children".
        /// A control's own cost is the time it took minus the time its children took, and the
        /// children are the ones that know their own total - so they add it here on the way out
        /// and the parent reads it.
        /// </summary>
        private static long _childTicks;

        /// <summary>What a nested measurement has to remember while it runs.</summary>
        public readonly struct Scope
        {
            public readonly long Start;
            public readonly long OuterChildTicks;

            public Scope(long start, long outerChildTicks)
            {
                Start = start;
                OuterChildTicks = outerChildTicks;
            }
        }

        /// <summary>Starts a nested measurement. Cheap and side effect free while switched off.</summary>
        public static Scope Begin()
        {
            if (!Enabled)
                return default;

            long outer = _childTicks;
            _childTicks = 0;

            return new Scope(Stopwatch.GetTimestamp(), outer);
        }

        /// <summary>
        /// Ends one, recording both what it cost in total and what it cost on its own, and
        /// giving its total to the measurement it was nested in.
        /// </summary>
        public static void End(string key, in Scope scope)
        {
            if (!Enabled)
                return;

            long inclusive = Stopwatch.GetTimestamp() - scope.Start;
            long children = _childTicks;

            _childTicks = scope.OuterChildTicks + inclusive;

            Buckets.TryGetValue(key, out Bucket bucket);

            bucket.Calls++;
            bucket.Ticks += inclusive;
            bucket.SelfTicks += Math.Max(0, inclusive - children);

            Buckets[key] = bucket;
        }

        /// <summary>How many times <see cref="Reset"/> was told a pass had run, for averaging.</summary>
        public static int Passes { get; private set; }

        public static void Reset()
        {
            Buckets.Clear();
            Passes = 0;
            _childTicks = 0;
        }

        /// <summary>Marks one complete pass - a layout, a frame - so the report can average.</summary>
        public static void CountPass()
        {
            if (Enabled)
                Passes++;
        }

        private static int _framesLeft;
        private static Action<string>? _finished;

        /// <summary>
        /// Records the next <paramref name="frames"/> rendered frames and then hands the report
        /// to <paramref name="onFinished"/>.
        ///
        /// A handful of frames is enough and a great deal better than a fixed duration: the
        /// numbers that matter are per frame, and leaving it running costs a timestamp pair per
        /// control per frame.
        /// </summary>
        public static void RunForFrames(int frames, Action<string> onFinished)
        {
            Reset();

            _framesLeft = Math.Max(1, frames);
            _finished = onFinished;
            Enabled = true;
        }

        /// <summary>
        /// One rendered frame is over. Called by the renderer; when the countdown from
        /// <see cref="RunForFrames"/> runs out this switches recording off and reports.
        ///
        /// With more than one dialog open this is called once per dialog, so a "pass" is one
        /// dialog's frame rather than one frame of the game. That is the more useful unit here -
        /// the question is what a dialog costs.
        /// </summary>
        public static void EndFrame()
        {
            if (!Enabled || _framesLeft <= 0)
                return;

            Passes++;

            if (--_framesLeft > 0)
                return;

            Enabled = false;

            Action<string>? finished = _finished;
            _finished = null;

            finished?.Invoke(Report("in game, per dialog frame"));
        }

        /// <summary>A timestamp to hand back to <see cref="Add"/>, or 0 while switched off.</summary>
        public static long Start()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0;
        }

        /// <summary>Records one call of <paramref name="key"/> and how long it took.</summary>
        public static void Add(string key, long startTimestamp)
        {
            if (!Enabled)
                return;

            long ticks = Stopwatch.GetTimestamp() - startTimestamp;

            Buckets.TryGetValue(key, out Bucket bucket);

            bucket.Calls++;
            bucket.Ticks += ticks;

            Buckets[key] = bucket;
        }

        /// <summary>Records one call of <paramref name="key"/> without timing it.</summary>
        public static void Count(string key)
        {
            if (!Enabled)
                return;

            Buckets.TryGetValue(key, out Bucket bucket);

            bucket.Calls++;

            Buckets[key] = bucket;
        }

        /// <summary>
        /// What was recorded, worst first, as a table.
        ///
        /// Two times per row, and the difference between them is the whole point: <c>total</c>
        /// covers everything that happened inside the call, children included, while
        /// <c>self</c> is what the control did itself. A container with a large total and a
        /// small self is not slow - the fifty rows in it are.
        /// </summary>
        public static string Report(string title = "UI profile")
        {
            var builder = new StringBuilder();
            int passes = Math.Max(1, Passes);

            builder.AppendLine(title + "  (" + Passes + " pass(es))");
            builder.AppendLine(new string('-', 78));
            builder.AppendLine("  calls/pass   self ms/pass   total ms/pass   what");

            var rows = new List<KeyValuePair<string, Bucket>>(Buckets);

            rows.Sort((left, right) => right.Value.SelfTicks.CompareTo(left.Value.SelfTicks));

            foreach (KeyValuePair<string, Bucket> row in rows)
            {
                double self = row.Value.SelfTicks * 1000.0 / Stopwatch.Frequency / passes;
                double total = row.Value.Ticks * 1000.0 / Stopwatch.Frequency / passes;

                builder.AppendLine(string.Format(
                    "{0,12:0.#} {1,13:0.###} {2,15:0.###}   {3}",
                    row.Value.Calls / (double)passes,
                    self,
                    total,
                    row.Key));
            }

            return builder.ToString();
        }
    }
}

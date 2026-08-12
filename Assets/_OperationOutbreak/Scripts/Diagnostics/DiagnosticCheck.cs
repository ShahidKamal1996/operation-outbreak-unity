using System.Collections.Generic;

namespace OperationOutbreak.Diagnostics
{
    /// <summary>
    /// Milestone 1O - outcome of a single diagnostic check.
    ///
    /// WARNING is deliberately distinct from FAILED: a warning marks something that is
    /// worth a human look (a Runner that never reached the player, a spawn that was
    /// nudged) while a failure marks a broken expectation (a section that spawned the
    /// wrong number of enemies). Only FAILED means "this build is wrong".
    /// </summary>
    public enum DiagnosticStatus
    {
        Passed = 0,
        Warning = 1,
        Failed = 2
    }

    /// <summary>
    /// Milestone 1O - one recorded expectation and its measured outcome.
    ///
    /// This is the ONLY shape a diagnostic result may take. Nothing in the diagnostics
    /// layer is allowed to compare values with an ad-hoc Debug.Log: every comparison
    /// produces one of these, so the end-of-run report can count PASSED / FAILED /
    /// WARNING reliably and every line carries its own expected-vs-actual evidence.
    ///
    /// Plain C# - not a MonoBehaviour, no Unity dependency - so the whole check model is
    /// exercisable from EditMode tests without entering Play Mode.
    /// </summary>
    public sealed class DiagnosticCheck
    {
        /// <summary>Stable short id, e.g. "MIS-04". Used to reference a check in reports.</summary>
        public string Id { get; }

        /// <summary>Human readable check title.</summary>
        public string Name { get; }

        public DiagnosticStatus Status { get; }

        /// <summary>What the check is verifying, in one sentence.</summary>
        public string Description { get; }

        /// <summary>The expected value, formatted for the console.</summary>
        public string Expected { get; }

        /// <summary>The value actually observed during the run.</summary>
        public string Actual { get; }

        /// <summary>Optional extra evidence (per-enemy lines, ordering, distances).</summary>
        public string Details { get; }

        public DiagnosticCheck(
            string id,
            string name,
            DiagnosticStatus status,
            string description,
            string expected,
            string actual,
            string details = null)
        {
            Id = id;
            Name = name;
            Status = status;
            Description = description;
            Expected = expected;
            Actual = actual;
            Details = details;
        }

        public static DiagnosticCheck Pass(
            string id, string name, string description, string expected, string actual, string details = null)
        {
            return new DiagnosticCheck(id, name, DiagnosticStatus.Passed, description, expected, actual, details);
        }

        public static DiagnosticCheck Fail(
            string id, string name, string description, string expected, string actual, string details = null)
        {
            return new DiagnosticCheck(id, name, DiagnosticStatus.Failed, description, expected, actual, details);
        }

        public static DiagnosticCheck Warn(
            string id, string name, string description, string expected, string actual, string details = null)
        {
            return new DiagnosticCheck(id, name, DiagnosticStatus.Warning, description, expected, actual, details);
        }

        /// <summary>
        /// Produces PASSED when <paramref name="condition"/> holds, otherwise the supplied
        /// failure severity. This is the workhorse used by the runtime evaluators, and it
        /// is what keeps "compare, then classify" in one place.
        /// </summary>
        public static DiagnosticCheck Evaluate(
            bool condition,
            string id,
            string name,
            string description,
            string expected,
            string actual,
            string details = null,
            DiagnosticStatus failureStatus = DiagnosticStatus.Failed)
        {
            return new DiagnosticCheck(
                id, name, condition ? DiagnosticStatus.Passed : failureStatus,
                description, expected, actual, details);
        }

        public override string ToString()
        {
            return $"[{Status.ToString().ToUpperInvariant()}] {Id} {Name}";
        }
    }

    /// <summary>
    /// Milestone 1O - an ordered, self-counting collection of <see cref="DiagnosticCheck"/>.
    /// The counts are what the RESULT block of the end-of-run report prints.
    /// </summary>
    public sealed class DiagnosticCheckList
    {
        private readonly List<DiagnosticCheck> _checks = new List<DiagnosticCheck>();

        public IReadOnlyList<DiagnosticCheck> Checks => _checks;

        public int Count => _checks.Count;

        public int PassedCount { get; private set; }

        public int WarningCount { get; private set; }

        public int FailedCount { get; private set; }

        /// <summary>True when nothing failed. Warnings do not spoil a passing run.</summary>
        public bool AllPassed => FailedCount == 0;

        public DiagnosticCheck Add(DiagnosticCheck check)
        {
            if (check == null)
            {
                return null;
            }

            _checks.Add(check);

            switch (check.Status)
            {
                case DiagnosticStatus.Passed:
                    PassedCount++;
                    break;
                case DiagnosticStatus.Warning:
                    WarningCount++;
                    break;
                default:
                    FailedCount++;
                    break;
            }

            return check;
        }

        public void Clear()
        {
            _checks.Clear();
            PassedCount = 0;
            WarningCount = 0;
            FailedCount = 0;
        }
    }
}

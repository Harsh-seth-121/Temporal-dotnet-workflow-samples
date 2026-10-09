namespace Sandbox.Samples.Onboarding;

/// <summary>
/// Everything the two workflows in this sample send each other.
///
/// These are records rather than loose parameters because the monitor's state
/// crosses a continue-as-new boundary: every cycle it is serialized, handed to a
/// brand new workflow run, and deserialized again. Keeping that state in one named
/// shape makes the round trip obvious rather than something you infer from a long
/// argument list.
/// </summary>
/// <param name="AccountId">Identifies the account across both workflows.</param>
/// <param name="HoldOpenFor">
/// A timer held after the welcome step, and nowhere else. Zero by default, so an
/// ordinary run records no timers at all.
///
/// Its one job is to park a run at the point where its history already records which
/// side of the live patch it took, which is the only state from which that patch has
/// anything to decide. Park a run earlier and it has finished replaying before it
/// reaches the patch, so the patch always sees a fresh run and always says yes. The
/// README walkthrough depends on this placement.
///
/// The value is read from the input, so it is fixed in history and replays identically.
/// </param>
/// <param name="Monitor">Timings handed to the child when the parent starts it.</param>
public sealed record OnboardingInput(
    string AccountId,
    TimeSpan HoldOpenFor,
    MonitorSettings Monitor);

/// <summary>
/// What the parent returns. The monitor's identifiers are in here because the parent
/// abandons it: once the parent completes, this result is the only thing that says
/// where the still-running child went.
/// </summary>
public sealed record OnboardingResult(
    string AccountId,
    bool WelcomedByEmail,
    string MonitorWorkflowId,
    string MonitorFirstRunId);

/// <summary>
/// How long the monitor watches and how often.
///
/// Two named sets rather than one, because the numbers that make this sample
/// watchable are nothing like the numbers you would run in production. The demo set
/// finishes inside a coffee break; the production set is the shape the comments and
/// the README talk about.
/// </summary>
public sealed record MonitorSettings(
    TimeSpan CheckInterval,
    TimeSpan CycleLength,
    int MaxCycles)
{
    /// <summary>Small enough to watch a continue-as-new happen: 8 checks a cycle, 3 cycles, about 6 minutes.</summary>
    public static MonitorSettings Demo { get; } =
        new(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2), MaxCycles: 3);

    /// <summary>A check every quarter hour, a fresh history every hour, and no end. MaxCycles 0 means forever.</summary>
    public static MonitorSettings Production { get; } =
        new(TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), MaxCycles: 0);
}

/// <summary>
/// The monitor's working state, carried from one continue-as-new run to the next.
///
/// <paramref name="ChecksDone"/> counts every check since the first run rather than
/// since this one. That is the number worth seeing: if it ever restarted at zero, the
/// state did not survive the handover and the whole point of continue-as-new was lost.
/// </summary>
public sealed record MonitorState(
    string AccountId,
    MonitorSettings Settings,
    int Cycle,
    int ChecksDone);

/// <summary>What the monitor returns once it has run out of cycles.</summary>
public sealed record MonitorResult(
    string AccountId,
    int ChecksDone,
    int Cycles);

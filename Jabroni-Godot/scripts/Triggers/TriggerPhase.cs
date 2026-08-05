using System;

namespace Jabroni.Triggers;

/// <summary>
/// Which crossing of a trigger's volume this is about. Flags, because an
/// <see cref="AreaTrigger"/> can be armed for either or both crossings -- but a
/// <see cref="TriggerEvent"/> always carries exactly one of them, describing the crossing that
/// just happened.
/// </summary>
[Flags]
public enum TriggerPhase
{
    None = 0,
    Entered = 1 << 0,
    Exited = 1 << 1,
}

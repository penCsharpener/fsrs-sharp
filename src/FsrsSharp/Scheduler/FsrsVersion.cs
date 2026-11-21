namespace FsrsSharp;

/// <summary>The FSRS model/parameter versions a <see cref="DefaultScheduler"/> can be created for (ts-fsrs <c>DefaultSchedulerVersion</c>).</summary>
public enum FsrsVersion
{
    /// <summary>Upstream <c>'FSRS-3'</c>.</summary>
    Fsrs3,

    /// <summary>Upstream <c>'FSRS-4'</c>.</summary>
    Fsrs4,

    /// <summary>Upstream <c>'FSRS-4.5'</c>.</summary>
    Fsrs4Dot5,

    /// <summary>Upstream <c>'FSRS-5'</c>.</summary>
    Fsrs5,

    /// <summary>Upstream <c>'FSRS-6'</c>.</summary>
    Fsrs6,

    /// <summary>Upstream <c>'FSRS-7'</c> (the default).</summary>
    Fsrs7,
}

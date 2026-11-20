using FsrsSharp.Kit;

namespace FsrsSharp.Chrono;

/// <summary>Typed access to the <see cref="DateTimeChrono"/> card and review-log fields.</summary>
public static class DateTimeCardExtensions
{
    extension<TState>(Card<TState> card) where TState : class
    {
        public DateTime DueAt => card.Get(DateTimeChrono.DueAt);
        public DateTime? LastReviewAt => card.Get(DateTimeChrono.LastReviewAt);
    }

    extension<TState>(ReviewLog<TState> revlog) where TState : class
    {
        public DateTime DueAt => revlog.Get(DateTimeChrono.DueAt);
        public DateTime? LastReviewAt => revlog.Get(DateTimeChrono.LastReviewAt);
        public DateTime ReviewTime => revlog.Get(DateTimeChrono.ReviewTime);
    }
}

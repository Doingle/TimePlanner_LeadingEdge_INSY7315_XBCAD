using TimePlanner.Core.Services;

namespace TimePlanner.Widget.Services
{
    public static class TimeProviderExtensions
    {
        /// <summary>
        /// The wall-clock time, as time entries store it. Everything in the widget reads the time
        /// through a <see cref="TimeProvider"/>, so the previews can stop the clock at the spec's times.
        /// </summary>
        public static DateTime LocalNow(this TimeProvider clock) => clock.GetLocalNow().DateTime;
    }

    /// <summary>The widget's clock for Core's check-in engine, so the engine sees the previews' time as well.</summary>
    public sealed class TimeProviderClock(TimeProvider clock) : IClock
    {
        public DateTime Now => clock.LocalNow();
    }
}

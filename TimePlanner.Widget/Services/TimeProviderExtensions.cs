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
}

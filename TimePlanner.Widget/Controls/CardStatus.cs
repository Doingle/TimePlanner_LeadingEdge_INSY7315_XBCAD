namespace TimePlanner.Widget.Controls
{
    /// <summary>
    /// The status on a card: the date beside a dot that is green while tracking and grey otherwise, and what
    /// the day is doing ("Not started"), which the Setup banner shows in words. <see cref="Spoken"/> says the
    /// state too, so it never rests on the colour alone.
    /// </summary>
    public sealed record CardStatus(string Date, string State, bool Live)
    {
        public string Spoken => $"{Date}, {State.ToLowerInvariant()}";
    }
}

namespace TimePlanner.Widget.Controls
{
    /// <summary>
    /// The status line on a card: the date beside a dot that is green while tracking and grey otherwise.
    /// <paramref name="Spoken"/> says the state in words, so it never rests on the colour alone.
    /// </summary>
    public sealed record CardStatus(string Date, bool Live, string Spoken);
}

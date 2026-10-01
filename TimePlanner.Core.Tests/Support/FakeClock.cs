using TimePlanner.Core.Services;

namespace TimePlanner.Core.Tests.Support
{
    //-----------------------------
    //a clock tests can set and move
    public sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; }

        //-----------------------------
        //moves the clock forward
        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }
}
//------------------------------EOF-----------------------------\\

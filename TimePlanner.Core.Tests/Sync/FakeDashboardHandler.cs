namespace TimePlanner.Core.Tests.Sync
{
    //-----------------------------
    //fake api that records requests and returns set answers
    public class FakeDashboardHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        public bool ThrowOffline { get; set; }
        public List<string> Bodies { get; } = new();
        public int Calls { get; private set; }

        //-----------------------------
        //records calls and returns configured test response
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            //simulates network offline failure
            if (ThrowOffline)
            {
                throw new HttpRequestException("Offline test failure");
            }

            //captures json body when present
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                Bodies.Add(body);
            }

            //returns set answer or default 200 OK
            if (Respond != null)
            {
                return Respond(request);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
//------------------------------EOF-----------------------------\\

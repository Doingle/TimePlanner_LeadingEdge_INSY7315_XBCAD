using System.Net;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //fake http handler recording requests and returning mock responses
    public class FakeAiHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        public List<string> Bodies { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();
        public bool ThrowConnectionRefused { get; set; }

        //-----------------------------
        //records requests and returns scripted answers
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            //simulate connection refused error
            if (ThrowConnectionRefused)
            {
                throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
            }

            //record request payload string
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                Bodies.Add(body);
            }

            //use scripted responder if provided
            if (Respond != null)
            {
                return Respond(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
//------------------------------EOF-----------------------------\\

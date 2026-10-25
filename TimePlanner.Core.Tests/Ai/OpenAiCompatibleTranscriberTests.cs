using System.Net;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for remote audio transcription
    public class OpenAiCompatibleTranscriberTests : IDisposable
    {
        private readonly string _tempFile;

        public OpenAiCompatibleTranscriberTests()
        {
            _tempFile = Path.GetTempFileName();
            File.WriteAllBytes(_tempFile, new byte[] { 0, 1, 2, 3 });
        }

        public void Dispose()
        {
            //clean up temporary audio file
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        //-----------------------------
        //verifies multipart form components and returned text
        [Fact]
        public async Task TranscribeAsync_SendsMultipartAndReadsText()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"text\":\"Hello world transcript\"}")
                }
            };

            var settings = new AiSettings
            {
                Transcriber = TranscriberKind.OpenAiCompatible,
                TranscriberModel = "whisper-large-v3-turbo",
                TranscriberUrl = "https://api.openai.com/v1"
            };

            var transcriber = new OpenAiCompatibleTranscriber(new HttpClient(handler), settings, "test-key");
            var hints = new TranscriptionHints(["Client", "Project"], "en");

            var text = await transcriber.TranscribeAsync(_tempFile, hints, CancellationToken.None);

            Assert.Equal("Hello world transcript", text);
            Assert.Single(handler.Requests);

            var req = handler.Requests[0];
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", req.Headers.Authorization!.Parameter);

            var body = handler.Bodies[0];
            Assert.True(body.Contains("name=\"model\"") || body.Contains("name=model"));
            Assert.Contains("whisper-large-v3-turbo", body);
            Assert.True(body.Contains("name=\"language\"") || body.Contains("name=language"));
            Assert.Contains("en", body);
            Assert.True(body.Contains("name=\"prompt\"") || body.Contains("name=prompt"));
            Assert.Contains("Client, Project", body);
            Assert.True(body.Contains("name=\"file\"") || body.Contains("name=file"));
        }
    }
}
//------------------------------EOF-----------------------------\\

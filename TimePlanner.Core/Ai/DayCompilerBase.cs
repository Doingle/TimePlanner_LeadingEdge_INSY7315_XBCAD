using System.Diagnostics;
using System.Text.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //base implementation for day compilers
    public abstract class DayCompilerBase : IDayCompiler
    {
        private readonly DayPromptBuilder _prompt;

        protected DayCompilerBase(DayPromptBuilder prompt)
        {
            _prompt = prompt;
        }

        public abstract string Model { get; }

        //-----------------------------
        //compiles a request into checked suggestions
        public async Task<DayBuildResult> BuildAsync(DayBuildRequest request, CancellationToken ct)
        {
            //requests with no windows return empty without calling the model
            if (request.Windows.Count == 0)
            {
                return new DayBuildResult([], [], TimeSpan.Zero, Model, 0);
            }

            var system = _prompt.System;
            var user = _prompt.BuildUser(request);
            var schema = _prompt.BuildSchema(request);

            var stopwatch = Stopwatch.StartNew();
            var (json, outputTokens) = await CompleteAsync(system, user, schema, ct);

            ModelReply? reply;
            try
            {
                reply = JsonSerializer.Deserialize<ModelReply>(json);
            }
            //retry once when the model produces malformed json
            catch (JsonException)
            {
                var retryUser = user + "\n\nYour last answer was not valid JSON. Answer with JSON only.";
                var retryResult = await CompleteAsync(system, retryUser, schema, ct);
                json = retryResult.Json;
                outputTokens = (outputTokens ?? 0) + (retryResult.OutputTokens ?? 0);

                try
                {
                    reply = JsonSerializer.Deserialize<ModelReply>(json);
                }
                //give up on repeated malformed responses
                catch (JsonException ex)
                {
                    throw new AiUnavailableException("The AI model returned an invalid response. Try again.", ex);
                }
            }

            stopwatch.Stop();
            reply ??= new ModelReply();

            var (entries, flags) = DayBuildChecker.Check(request, reply);
            return new DayBuildResult(entries, flags, stopwatch.Elapsed, Model, outputTokens);
        }

        protected abstract Task<(string Json, int? OutputTokens)> CompleteAsync(string system, string user, string schema, CancellationToken ct);

        public abstract Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);

        public abstract Task<string?> TestAsync(CancellationToken ct);
    }
}
//------------------------------EOF-----------------------------\\

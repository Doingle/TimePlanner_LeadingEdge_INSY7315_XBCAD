using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //formats prompts and schemas for the model
    public sealed class DayPromptBuilder
    {
        //-----------------------------
        //default prompt singleton using compiled text
        public static DayPromptBuilder Default { get; } = new(DefaultPrompt.Instructions, DefaultPrompt.Examples);

        //-----------------------------
        //combined system prompt text
        public string System { get; }

        public DayPromptBuilder(string instructions, string examples)
        {
            System = instructions + "\n\nEXAMPLES\n" + examples;
        }

        //-----------------------------
        //formats the compact user message
        public string BuildUser(DayBuildRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("DAY ").Append(request.Day.ToString("yyyy-MM-dd")).Append('\n');

            sb.Append("PROJECTS\n");
            //list all catalog projects with their identifiers
            foreach (var p in request.Projects)
            {
                sb.Append(p.Id).Append(' ').Append(p.Company).Append(" › ").Append(p.Project).Append('\n');
            }

            sb.Append("ACTIVITIES\n");
            //list all catalog activities with their identifiers
            foreach (var a in request.Activities)
            {
                sb.Append(a.Id).Append(' ').Append(string.Join(" › ", a.Path)).Append('\n');
            }

            sb.Append("WINDOWS\n");
            //format each window with its unlogged spans
            foreach (var w in request.Windows)
            {
                var freeText = w.Free.Count == 0
                    ? "free none"
                    : "free " + string.Join(", ", w.Free.Select(r => $"{r.Start:HH:mm}-{r.End:HH:mm}"));

                sb.Append(w.Id).Append(' ')
                  .Append(w.Start.ToString("HH:mm")).Append('-').Append(w.End.ToString("HH:mm"))
                  .Append(' ').Append(freeText).Append('\n');
            }

            sb.Append("NOTES\n");
            //collapse transcript whitespace for each window note
            foreach (var w in request.Windows)
            {
                var collapsed = Regex.Replace(w.Transcript.Trim(), @"\s+", " ");
                sb.Append(w.Id).Append(": ").Append(collapsed).Append('\n');
            }

            return sb.ToString().TrimEnd();
        }

        //-----------------------------
        //creates the json schema for structured outputs
        public string BuildSchema(DayBuildRequest request)
        {
            var windowIds = request.Windows.Select(w => w.Id).ToList();
            //guarantee at least one window value for valid schema enum
            if (windowIds.Count == 0)
            {
                windowIds.Add("W1");
            }

            var projectIds = new List<string> { "P0" };
            projectIds.AddRange(request.Projects.Select(p => p.Id));

            var activityIds = new List<string> { "A0" };
            activityIds.AddRange(request.Activities.Select(a => a.Id));

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["entries"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["w"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = windowIds },
                                ["s"] = new Dictionary<string, object> { ["type"] = "string" },
                                ["e"] = new Dictionary<string, object> { ["type"] = "string" },
                                ["p"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = projectIds },
                                ["a"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = activityIds },
                                ["note"] = new Dictionary<string, object> { ["type"] = "string" }
                            },
                            ["required"] = new[] { "w", "s", "e", "p", "a", "note" }
                        }
                    },
                    ["flags"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["items"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["w"] = new Dictionary<string, object> { ["type"] = "string" },
                                ["msg"] = new Dictionary<string, object> { ["type"] = "string" }
                            },
                            ["required"] = new[] { "w", "msg" }
                        }
                    }
                },
                ["required"] = new[] { "entries", "flags" }
            };

            return JsonSerializer.Serialize(schema);
        }
    }
}
//------------------------------EOF-----------------------------\\

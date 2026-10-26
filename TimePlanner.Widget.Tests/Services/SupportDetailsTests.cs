using System.IO;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //unit tests for diagnostic support details generation
    public class SupportDetailsTests
    {
        //-----------------------------
        //version and installation type are formatted properly
        [Theory]
        [InlineData("1.1.0", false, "portable")]
        [InlineData("1.2.3", true, "installed")]
        public void Build_ContainsVersionAndInstallType(string version, bool isInstalled, string expectedType)
        {
            var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TimePlanner");
            var processPath = isInstalled ? Path.Combine(localPrograms, "TimePlanner.Widget.exe") : @"C:\Tools\TimePlanner\TimePlanner.Widget.exe";

            var info = new SupportInfo(
                Version: version,
                ProcessPath: processPath);

            var output = SupportDetails.Build(info);

            Assert.Contains($"Time Planner {version} ({expectedType})", output);
        }

        //-----------------------------
        //dashboard url shows host only and sensitive data like email or keys are excluded
        [Fact]
        public void Build_OmitsSensitiveInformationAndShowsHostOnly()
        {
            var emailUser = "john.doe@company.example.com";
            var secretKey = "sk_live_1234567890abcdef";
            var fullUrl = $"https://{emailUser}:{secretKey}@dashboard.timeplanner.app:443/api/v1/sync";

            var info = new SupportInfo(
                Version: "1.0.0",
                ProcessPath: @"C:\Tools\TimePlanner.Widget.exe",
                DashboardUrl: fullUrl);

            var output = SupportDetails.Build(info);

            Assert.Contains("Dashboard: dashboard.timeplanner.app", output);
            Assert.DoesNotContain(emailUser, output);
            Assert.DoesNotContain(secretKey, output);
            Assert.DoesNotContain("/api/v1/sync", output);
        }

        //-----------------------------
        //missing parameters print unknown
        [Fact]
        public void Build_MissingValuesPrintUnknown()
        {
            var info = new SupportInfo();

            var output = SupportDetails.Build(info);

            Assert.Contains("Time Planner unknown (portable)", output);
            Assert.Contains("Dashboard: unknown", output);
            Assert.Contains("Signed in: unknown", output);
            Assert.Contains("Start with Windows: unknown", output);
            Assert.Contains("Voice notes: unknown · voice model: unknown", output);
            Assert.Contains("Day model: unknown · engine: unknown · hardware: unknown", output);
            Assert.Contains("AI engine: unknown · installed by Time Planner: unknown", output);
            Assert.Contains("Microphone: unknown", output);
        }
    }
}
//------------------------------EOF-----------------------------\\

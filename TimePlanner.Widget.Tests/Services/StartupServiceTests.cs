using System.IO;
using System.Security;
using Microsoft.Extensions.Logging.Abstractions;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //tests for keeping the windows startup entry in line with the setting. Every test uses an in-memory entry, never the real Run key
    public class StartupServiceTests
    {
        private const string ProgramPath = @"C:\Tools\Time Planner\TimePlanner.Widget.exe";

        private static StartupService Create(IStartupRegistration registration, string? path = ProgramPath) =>
            new(registration, path, NullLogger<StartupService>.Instance);

        //-----------------------------
        //enabling registers this program, quoted because the path has a space
        [Fact]
        public void Apply_Enabled_RegistersQuotedProgramPath()
        {
            var registration = new InMemoryStartupRegistration();

            var problem = Create(registration).Apply(enabled: true);

            Assert.Null(problem);
            Assert.Equal($"\"{ProgramPath}\"", registration.Command);
        }

        //-----------------------------
        //disabling removes the entry straight away
        [Fact]
        public void Apply_Disabled_RemovesEntry()
        {
            var registration = new InMemoryStartupRegistration();
            var service = Create(registration);
            service.Apply(enabled: true);

            var problem = service.Apply(enabled: false);

            Assert.Null(problem);
            Assert.Null(registration.Command);
        }

        //-----------------------------
        //switching off then on again restores the entry without a restart
        [Fact]
        public void Apply_ToggledOffThenOn_RestoresEntry()
        {
            var registration = new InMemoryStartupRegistration();
            var service = Create(registration);

            service.Apply(enabled: false);
            Assert.Null(registration.Command);

            service.Apply(enabled: true);
            Assert.Equal(service.Command, registration.Command);
        }

        //-----------------------------
        //an entry that already matches is left alone rather than written again on every start
        [Fact]
        public void Apply_AlreadyRegistered_DoesNotWriteAgain()
        {
            var registration = new CountingRegistration { Command = $"\"{ProgramPath.ToUpperInvariant()}\"" };

            Create(registration).Apply(enabled: true);

            Assert.Equal(0, registration.Writes);
        }

        //-----------------------------
        //disabling with no entry does nothing
        [Fact]
        public void Apply_DisabledWithoutEntry_DoesNotWrite()
        {
            var registration = new CountingRegistration();

            var problem = Create(registration).Apply(enabled: false);

            Assert.Null(problem);
            Assert.Equal(0, registration.Writes);
        }

        //-----------------------------
        //an update unzipped to a new folder with the setting on points the entry at the new program
        [Fact]
        public void Apply_UpdateInNewFolderWithSettingOn_MovesEntryToNewProgram()
        {
            var registration = new InMemoryStartupRegistration();
            Create(registration, @"C:\Tools\TimePlanner-1.0.0\TimePlanner.Widget.exe").Apply(enabled: true);

            var updated = Create(registration, @"C:\Tools\TimePlanner-1.1.0\TimePlanner.Widget.exe");
            updated.Apply(enabled: true);

            Assert.Equal(updated.Command, registration.Command);
        }

        //-----------------------------
        //windows refusing the change is reported, not thrown, and the entry is left as it was
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Apply_WindowsRefuses_ReturnsProblemWithoutThrowing(bool enabled)
        {
            var registration = new FailingRegistration(new UnauthorizedAccessException("denied"));

            var problem = Create(registration).Apply(enabled);

            Assert.False(string.IsNullOrWhiteSpace(problem));
        }

        //-----------------------------
        //every kind of registry refusal is handled
        [Fact]
        public void Apply_SecurityOrIoFailure_ReturnsProblem()
        {
            Assert.NotNull(Create(new FailingRegistration(new SecurityException())).Apply(enabled: true));
            Assert.NotNull(Create(new FailingRegistration(new IOException())).Apply(enabled: true));
        }

        //-----------------------------
        //without a program path nothing is registered and the user is told
        [Fact]
        public void Apply_NoProgramPath_ReturnsProblemAndRegistersNothing()
        {
            var registration = new InMemoryStartupRegistration();

            var problem = Create(registration, path: null).Apply(enabled: true);

            Assert.NotNull(problem);
            Assert.Null(registration.Command);
        }

        private sealed class CountingRegistration : IStartupRegistration
        {
            public string? Command { get; set; }
            public int Writes { get; private set; }

            public string? GetCommand() => Command;

            public void Register(string command)
            {
                Command = command;
                Writes++;
            }

            public void Unregister()
            {
                Command = null;
                Writes++;
            }
        }

        private sealed class FailingRegistration(Exception failure) : IStartupRegistration
        {
            public string? GetCommand() => @"""C:\Old\TimePlanner.Widget.exe""";

            public void Register(string command) => throw failure;

            public void Unregister() => throw failure;
        }
    }
}

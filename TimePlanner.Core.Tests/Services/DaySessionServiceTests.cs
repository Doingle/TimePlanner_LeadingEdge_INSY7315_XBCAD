using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Services;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for DaySessionService
    public class DaySessionServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public DaySessionServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //starting a day twice returns the existing session
        [Fact]
        public async Task StartDay_Twice_ReturnsSameSession()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var setup = scope.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            var session1 = await service.StartDayAsync(user.UserId, baseTime);
            var session2 = await service.StartDayAsync(user.UserId, baseTime.AddHours(1));

            Assert.Equal(session1.DaySessionId, session2.DaySessionId);
            Assert.Equal(baseTime, session2.StartedAt);
        }

        //-----------------------------
        //pause and resume records one finished pause
        [Fact]
        public async Task PauseAndResume_RecordOneFinishedPause()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope1 = _host.CreateScope();
            var setup = scope1.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", baseTime);

            var service1 = scope1.ServiceProvider.GetRequiredService<DaySessionService>();
            await service1.StartDayAsync(user.UserId, baseTime);

            var pauseTime = new DateTime(2026, 9, 28, 10, 0, 0);
            await service1.PauseAsync(user.UserId, pauseTime);

            var resumeTime = new DateTime(2026, 9, 28, 10, 30, 0);
            await service1.ResumeAsync(user.UserId, resumeTime);

            using var scope2 = _host.CreateScope();
            var service2 = scope2.ServiceProvider.GetRequiredService<DaySessionService>();
            var session = await service2.GetOpenSessionAsync(user.UserId);

            Assert.NotNull(session);
            var pause = Assert.Single(session.Pauses);
            Assert.Equal(pauseTime, pause.StartedAt);
            Assert.Equal(resumeTime, pause.EndedAt);
        }

        //-----------------------------
        //ending a day closes any running pause
        [Fact]
        public async Task EndDay_ClosesRunningPause()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope1 = _host.CreateScope();
            var setup = scope1.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", baseTime);

            var service1 = scope1.ServiceProvider.GetRequiredService<DaySessionService>();
            await service1.StartDayAsync(user.UserId, baseTime);

            var pauseTime = new DateTime(2026, 9, 28, 10, 0, 0);
            await service1.PauseAsync(user.UserId, pauseTime);

            var endTime = new DateTime(2026, 9, 28, 11, 0, 0);
            var endedSession = await service1.EndDayAsync(user.UserId, endTime);

            Assert.Equal(endTime, endedSession.EndedAt);
            var pause = Assert.Single(endedSession.Pauses);
            Assert.Equal(endTime, pause.EndedAt);
        }

        //-----------------------------
        //pausing without an open day throws an exception
        [Fact]
        public async Task Pause_WithoutOpenDay_Throws()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var setup = scope.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PauseAsync(user.UserId, baseTime));
        }
    }
}
//------------------------------EOF-----------------------------\\

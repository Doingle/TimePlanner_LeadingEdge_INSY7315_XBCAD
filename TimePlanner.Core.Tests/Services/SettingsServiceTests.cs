using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Services;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for SettingsService
    public class SettingsServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public SettingsServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //saving an interval outside allowed range throws an exception
        [Fact]
        public async Task Save_RejectsOutOfRangeInterval()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var settings = await settingsService.GetAsync(user.UserId);
            settings.CheckInIntervalMinutes = 2;

            await Assert.ThrowsAsync<ArgumentException>(() => settingsService.SaveAsync(settings));
        }

        //-----------------------------
        //saving valid settings persists values across scopes
        [Fact]
        public async Task Save_PersistsChangedValues()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope1 = _host.CreateScope();
            var user = await scope1.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var service1 = scope1.ServiceProvider.GetRequiredService<SettingsService>();
            var settings = await service1.GetAsync(user.UserId);
            settings.CheckInIntervalMinutes = 60;
            settings.SnoozeMinutes = 5;

            await service1.SaveAsync(settings);

            using var scope2 = _host.CreateScope();
            var service2 = scope2.ServiceProvider.GetRequiredService<SettingsService>();
            var reloaded = await service2.GetAsync(user.UserId);

            Assert.Equal(60, reloaded.CheckInIntervalMinutes);
            Assert.Equal(5, reloaded.SnoozeMinutes);
        }
    }
}
//------------------------------EOF-----------------------------\\

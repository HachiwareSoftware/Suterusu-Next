using Xunit;
using Suterusu.Configuration;
using Suterusu.Notifications;

namespace Suterusu.Tests
{
    public class NotificationFactoryTests
    {
        // -----------------------------------------------------------------------
        // Factory return types
        // -----------------------------------------------------------------------

        // Helper: build a minimal AppConfig with the given notification mode.
        private static AppConfig ConfigWith(NotificationMode mode) =>
            new AppConfig
            {
                NotificationMode      = mode,
                FlashWindowTarget     = "Chrome",
                FlashWindowDurationMs = 1600
            };

        [Fact]
        public void Create_FlashWindow_ReturnsFlashWindowNotificationService()
        {
            var service = NotificationServiceFactory.Create(ConfigWith(NotificationMode.FlashWindow));
            Assert.IsType<FlashWindowNotificationService>(service);
        }

        [Fact]
        public void Create_CircleDot_ReturnsCircleDotNotificationService()
        {
            var service = NotificationServiceFactory.Create(ConfigWith(NotificationMode.CircleDot));
            Assert.IsType<CircleDotNotificationService>(service);
        }

        [Fact]
        public void Create_Nothing_ReturnsNullNotificationService()
        {
            var service = NotificationServiceFactory.Create(ConfigWith(NotificationMode.Nothing));
            Assert.IsType<NullNotificationService>(service);
        }

        [Fact]
        public void Create_ReturnsINotificationService_ForAllModes()
        {
            INotificationService flash  = NotificationServiceFactory.Create(ConfigWith(NotificationMode.FlashWindow));
            INotificationService circle = NotificationServiceFactory.Create(ConfigWith(NotificationMode.CircleDot));
            INotificationService none   = NotificationServiceFactory.Create(ConfigWith(NotificationMode.Nothing));

            Assert.NotNull(flash);
            Assert.NotNull(circle);
            Assert.NotNull(none);
        }

        // -----------------------------------------------------------------------
        // NullNotificationService — no-op behaviour
        // -----------------------------------------------------------------------

        [Fact]
        public void NullNotificationService_NotifySuccess_DoesNotThrow()
        {
            var service = new NullNotificationService();
            var ex = Record.Exception(() => service.NotifySuccess());
            Assert.Null(ex);
        }

        [Fact]
        public void NullNotificationService_NotifyFailure_DoesNotThrow()
        {
            var service = new NullNotificationService();
            var ex = Record.Exception(() => service.NotifyFailure());
            Assert.Null(ex);
        }

        [Fact]
        public void NullNotificationService_ImplementsINotificationService()
        {
            var service = new NullNotificationService();
            Assert.IsAssignableFrom<INotificationService>(service);
        }

        [Fact]
        public void NullNotificationService_NotifySuccess_CalledMultipleTimes_DoesNotThrow()
        {
            var service = new NullNotificationService();
            var ex = Record.Exception(() =>
            {
                service.NotifySuccess();
                service.NotifySuccess();
                service.NotifySuccess();
            });
            Assert.Null(ex);
        }

        [Fact]
        public void NullNotificationService_NotifyFailure_CalledMultipleTimes_DoesNotThrow()
        {
            var service = new NullNotificationService();
            var ex = Record.Exception(() =>
            {
                service.NotifyFailure();
                service.NotifyFailure();
                service.NotifyFailure();
            });
            Assert.Null(ex);
        }
    }
}

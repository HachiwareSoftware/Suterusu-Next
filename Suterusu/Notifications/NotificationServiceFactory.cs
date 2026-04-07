using Suterusu.Configuration;

namespace Suterusu.Notifications
{
    public static class NotificationServiceFactory
    {
        public static INotificationService Create(AppConfig config)
        {
            switch (config.NotificationMode)
            {
                case NotificationMode.FlashWindow:
                    return new FlashWindowNotificationService(
                        config.FlashWindowTarget,
                        config.FlashWindowDurationMs);
                case NotificationMode.CircleDot:
                    return new CircleDotNotificationService();
                case NotificationMode.Nothing:
                default:
                    return new NullNotificationService();
            }
        }
    }
}

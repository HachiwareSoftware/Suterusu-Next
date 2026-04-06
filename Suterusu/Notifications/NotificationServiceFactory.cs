using Suterusu.Configuration;

namespace Suterusu.Notifications
{
    public static class NotificationServiceFactory
    {
        public static INotificationService Create(NotificationMode mode)
        {
            switch (mode)
            {
                case NotificationMode.FlashWindow:
                    return new FlashWindowNotificationService();
                case NotificationMode.CircleDot:
                    return new CircleDotNotificationService();
                case NotificationMode.Nothing:
                default:
                    return new NullNotificationService();
            }
        }
    }
}

namespace Suterusu.Notifications
{
    /// <summary>No-op notification – used when NotificationMode is Nothing.</summary>
    public class NullNotificationService : INotificationService
    {
        public void NotifySuccess() { }
        public void NotifyFailure() { }
    }
}

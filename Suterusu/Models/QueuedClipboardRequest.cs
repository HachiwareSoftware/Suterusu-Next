using System;

namespace Suterusu.Models
{
    public class QueuedClipboardRequest
    {
        public DateTime QueuedAt { get; }

        public QueuedClipboardRequest()
        {
            QueuedAt = DateTime.UtcNow;
        }
    }
}

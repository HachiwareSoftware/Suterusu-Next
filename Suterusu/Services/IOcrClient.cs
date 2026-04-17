using System.Threading;
using System.Threading.Tasks;
using Suterusu.Models;

namespace Suterusu.Services
{
    public interface IOcrClient
    {
        Task<AiSingleAttemptResult> RunOcrAsync(
            byte[] imageData,
            string prompt,
            int timeoutMs,
            CancellationToken cancellationToken);
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EFYVLabyMake.Core.Logic
{
    public interface IDebounceScheduler
    {
        DateTimeOffset UtcNow { get; }
        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class TaskDebounceScheduler : IDebounceScheduler
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            return delay <= TimeSpan.Zero
                ? Task.CompletedTask
                : Task.Delay(delay, cancellationToken);
        }
    }
}

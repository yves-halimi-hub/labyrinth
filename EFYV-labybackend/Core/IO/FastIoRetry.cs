using System;
using System.IO;
using System.Threading;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.IO
{
    // Bounded IOException retry (#12) shared by every atomic publish/replace
    // call site (FastExporter, ExportEngine, FastSaveEngine). Unity re-importing
    // a just-published file (or an antivirus scanner) briefly holds the
    // destination, making File.Replace/Move fail with a transient sharing
    // violation; a short bounded backoff absorbs it without masking real errors.
    public static class FastIoRetry
    {
        public static void Run(Action operation)
        {
            Run(operation, Thread.Sleep);
        }

        // Delay-injectable core so the retry contract is unit-testable without
        // real sleeps. Retries only IOException; everything else propagates on
        // the first attempt. The last attempt's IOException propagates.
        public static void Run(Action operation, Action<int> delay)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (delay == null) throw new ArgumentNullException(nameof(delay));

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    operation();
                    return;
                }
                catch (IOException) when (attempt < BackendConfig.IO.PublishRetryAttempts)
                {
                    delay(GetDelayMilliseconds(attempt));
                }
            }
        }

        // 20ms after the first failure, 50ms after every later one (bounded by
        // PublishRetryAttempts, so at most two waits with the shipped config).
        public static int GetDelayMilliseconds(int attempt)
        {
            return attempt <= 1
                ? BackendConfig.IO.PublishRetryFirstDelayMilliseconds
                : BackendConfig.IO.PublishRetryMaxDelayMilliseconds;
        }
    }
}

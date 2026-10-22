using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Launch;

/// <summary>Bounded file staging. Callers still validate plans and commit on their owning thread.</summary>
internal static class DownloadBatch
{
    internal const int Concurrency = 8;
    internal static Task RunAsync<T>(IEnumerable<T> items, System.Func<T, CancellationToken, Task> download, CancellationToken token) =>
        Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = token },
            async (item, cancellation) => await download(item, cancellation).ConfigureAwait(false));
}

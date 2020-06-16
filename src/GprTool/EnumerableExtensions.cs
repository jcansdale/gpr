using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GprTool
{
    internal static class EnumerableExtensions
    {
        public static Task ForEachAsync<T>([NotNull] this IEnumerable<T> source, 
            [NotNull] Func<T, CancellationToken, Task> onExecuteFunc, Action<T, Exception> onExceptionAction = null,
            CancellationToken cancellationToken = default, int concurrency = 1)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (onExecuteFunc == null) throw new ArgumentNullException(nameof(onExecuteFunc));
            if (concurrency <= 0) throw new ArgumentOutOfRangeException(nameof(concurrency));

            // https://devblogs.microsoft.com/pfxteam/implementing-a-simple-foreachasync-part-2/
            return Task.WhenAll(
                Partitioner
                    .Create(source)
                    .GetPartitions(concurrency)
                    .Select(partition => Task.Run(async delegate
                    {
                        using (partition)
                        {
                            while (partition.MoveNext())
                            {
                                try
                                {
                                    await onExecuteFunc(partition.Current, cancellationToken);
                                }
                                catch (Exception e)
                                {
                                    onExceptionAction?.Invoke(partition.Current, e);
                                    throw;
                                }
                            }
                        }
                    }, default)));
        }    
    }
}
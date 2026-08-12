using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    public static class UnityMcpMainThread
    {
        private const int MaxQueuedWorkItems = 128;
        private sealed class WorkItem
        {
            public Func<object> Call;
            public TaskCompletionSource<object> Completion;
            public CancellationToken Cancellation;
        }

        private sealed class PumpBehaviour : MonoBehaviour
        {
            private void Update() => Pump();
        }

        private static readonly ConcurrentQueue<WorkItem> Queue = new ConcurrentQueue<WorkItem>();
        private static int mainThreadId;
        private static int queuedWorkItems;

        public static void Initialize(bool createBehaviour)
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            if (!createBehaviour) return;
            var gameObject = new GameObject("UnityMCP Main Thread Dispatcher") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<PumpBehaviour>();
        }

        public static Task<object> RunAsync(Func<object> call, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<object>(cancellationToken);
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                try { return Task.FromResult(call()); }
                catch (Exception exception) { return Task.FromException<object>(exception); }
            }
            if (Interlocked.Increment(ref queuedWorkItems) > MaxQueuedWorkItems)
            {
                Interlocked.Decrement(ref queuedWorkItems);
                return Task.FromException<object>(new InvalidOperationException("UnityMCP main-thread queue is full."));
            }
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Enqueue(new WorkItem { Call = call, Completion = completion, Cancellation = cancellationToken });
            return completion.Task;
        }

        public static void Pump()
        {
            while (Queue.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref queuedWorkItems);
                if (item.Cancellation.IsCancellationRequested) { item.Completion.TrySetCanceled(); continue; }
                try { item.Completion.TrySetResult(item.Call()); }
                catch (Exception exception) { item.Completion.TrySetException(exception); }
            }
        }
    }
}

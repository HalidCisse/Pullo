using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using Polly.Wrap;

/// <inheritdoc />
/// <summary>
/// High performance parallel task runner
/// </summary>
// ReSharper disable once CheckNamespace
public class Pullo : IDisposable
{
    private Action<ParallelLoopState, Action<CancellationToken>, Exception> _onError;
    private Action<ParallelLoopState, Action<CancellationToken>> _onStart;
    private Action<ParallelLoopState, Action<CancellationToken>> _onSuccess;

    private readonly ILogger<Pullo> _logger;
    private readonly BlockingCollection<Action<CancellationToken>> _queue;
    private readonly CancellationTokenSource _localCancellation;
    private CancellationTokenSource _cancellation;
    private PolicyWrap _resiliencePolicy;
    private int _maxDegreeOfParallelism = 1;
    private IEnumerable<Action<CancellationToken>> _tasksEnumerable;

    public Pullo(ILogger<Pullo> logger = null)
    {
        _logger = logger;
        _queue = new BlockingCollection<Action<CancellationToken>>();
        //_queues            = new ConcurrentQueue<IEnumerable<Action<CancellationToken>>>();
        _resiliencePolicy = Policy.Wrap(Policy.NoOp(), Policy.NoOp());
        _localCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_localCancellation.Token);
    }

    #region INIT

    public Pullo WithTimeout(TimeSpan timeout, int retryCount)
    {
        bool IsCancelationException(Exception e)
        {
            var inner = e;
            while (e.InnerException != null) inner = e.InnerException;
            return e is TaskCanceledException || e is OperationCanceledException || e is TimeoutRejectedException || inner is TaskCanceledException || inner is OperationCanceledException || inner is TimeoutRejectedException;
        }
        _resiliencePolicy = Policy
            .Wrap(Policy.Timeout(timeout, TimeoutStrategy.Optimistic, (context, span, task) => _logger.LogWarning("Task Timeout", context.PolicyKey, span)), Policy
                .Handle<Exception>(e => !IsCancelationException(e))
                .WaitAndRetry(retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (exception, timeSpan, retries, context) => _logger.LogWarning(exception, exception.Message, retries.ToString())));
        return this;
    }

    public Pullo WithMaxDegreeOfParallelism(int maxDegreeOfParallelism)
    {
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        return this;
    }

    public Pullo With(CancellationToken cancellation)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_localCancellation.Token, cancellation);
        return this;
    }

    //public void With(IEnumerable<Action<CancellationToken>> tasksEnumerable)
    //{
    //    if (!IsCompleted && _tasksEnumerable != null || _queue.Any())
    //        throw new NotSupportedException();
    //    _tasksEnumerable = tasksEnumerable;
    //}

    public Pullo OnError(Action<ParallelLoopState, Action<CancellationToken>, Exception> onError)
    {
        _onError = onError;
        return this;
    }

    public Pullo OnStart(Action<ParallelLoopState, Action<CancellationToken>> onTaskStart)
    {
        _onStart = onTaskStart;
        return this;
    }

    public Pullo OnSuccess(Action<ParallelLoopState, Action<CancellationToken>> onTaskSuccess)
    {
        _onSuccess = onTaskSuccess;
        return this;
    }

    #endregion

    public bool Enqueue(Action<CancellationToken> task) => _queue.TryAdd(task, TimeSpan.FromHours(1).Milliseconds, _cancellation.Token);

    public int Size() => _queue?.Count ?? 0;

    public void Stop() => _localCancellation.Cancel();

    public void Done() => _queue?.CompleteAdding();

    public bool IsCompleted { get; private set; }

    private Task Run(bool waitUntilDone)
    {
        IsCompleted = false;
        try
        {
            Parallel.ForEach(
                Partitioner.Create(
                    _tasksEnumerable ?? Enumerable.Empty<Action<CancellationToken>>()
                        .Concat(_queue.GetConsumingEnumerable(_cancellation.Token)),
                    EnumerablePartitionerOptions.NoBuffering),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = _cancellation.Token
                }, (task, loop) =>
                {
                    try
                    {
                        if (loop.ShouldExitCurrentIteration) return;
                        _onStart?.Invoke(loop, task);
                        _resiliencePolicy.Execute(task, _cancellation.Token);
                        _onSuccess?.Invoke(loop, task);
                        if (!waitUntilDone && _tasksEnumerable == null && _queue.Count == 0) Done();
                    }
                    catch (Exception e)
                    {
                        _onError?.Invoke(loop, task, e);
                    }
                });
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
        finally
        {
            IsCompleted = true;
        }
        return Task.CompletedTask;
    }

    public Task Run(IEnumerable<Action<CancellationToken>> tasksEnumerable)
    {
        _tasksEnumerable = tasksEnumerable;
        return Run(false);
    }

    public Task StartAndWait() => Run(true);

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        try
        {
            _logger.LogInformation("Dispose " + nameof(Pullo));
            IsCompleted = true;
            if (!(_queue?.IsAddingCompleted ?? false))
                _queue?.CompleteAdding();
            _localCancellation?.Cancel(false);
            _queue?.Dispose();
            _localCancellation?.Dispose();
        }
        catch (Exception) {/**/}
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Pullo()
    {
        Dispose(false);
    }
}
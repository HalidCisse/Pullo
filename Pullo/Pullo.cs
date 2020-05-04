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
    private Action<Func<Task>, Exception> _onError;
    private Action<Func<Task>> _onStart;
    private Action<Func<Task>> _onSuccess;

    private readonly ILogger<Pullo> _logger;
    private readonly ConcurrentQueue<Func<Task>> _queue;
    private readonly CancellationTokenSource _localCancellation;
    private CancellationTokenSource _cancellation;
    private AsyncPolicyWrap _resiliencePolicy;
    private int _maxDegreeOfParallelism = 100;
    private readonly ConcurrentQueue<IEnumerable<Func<Task>>> _enumerables;
    private int _running;

    public Pullo(ILogger<Pullo> logger)
    {
        _logger = logger;

        _enumerables = new ConcurrentQueue<IEnumerable<Func<Task>>>();
        _queue = new ConcurrentQueue<Func<Task>>();

        _resiliencePolicy  = Policy.WrapAsync(Policy.NoOpAsync(), Policy.NoOpAsync());
        _localCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        _cancellation      = CancellationTokenSource.CreateLinkedTokenSource(_localCancellation.Token);
    }

    #region INIT
    private bool IsCancelationException(Exception e)
    {
        if (_cancellation.IsCancellationRequested || _localCancellation.IsCancellationRequested)
            return true;
        var inner = e;
        while (inner?.InnerException != null) inner = inner.InnerException;
        return e is TaskCanceledException || e is OperationCanceledException || inner is TaskCanceledException || inner is OperationCanceledException;
    }

    public Pullo WithTimeout(TimeSpan timeout, int retryCount)
    {
        _resiliencePolicy = Policy
            .WrapAsync(
                Policy
                    .TimeoutAsync(timeout, TimeoutStrategy.Optimistic, (context, time, task) =>
                    {
                        _logger?.LogWarning($"[TIMEOUT] : {context.PolicyKey} at {context.OperationKey}: execution timed out after {time.TotalSeconds} seconds, task timeout.");

                        return task?.ContinueWith(t => {
                            if (t.IsFaulted)
                                _logger?.LogError(
                                    $"{context.PolicyKey} at {context.OperationKey}: execution timed out after {time.TotalSeconds} seconds, eventually terminated with: {t.Exception}.");
                            else if (t.IsCanceled)
                                _logger?.LogWarning(
                                    $"[CANCELLED] : {context.PolicyKey} at {context.OperationKey}: execution timed out after {time.TotalSeconds} seconds, task canceled.");
                        });
                    }),
                Policy
                    .Handle<Exception>(e => !IsCancelationException(e))
                    .WaitAndRetryAsync(retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (exception, timeSpan, retries, context) => _logger?.LogWarning(exception, exception.Message, retries.ToString())));
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
        _cancellation.Token.Register(() => IsRunning = false);
        return this;
    }

    public Pullo OnError(Action<Func<Task>, Exception> onError)
    {
        _onError = onError;
        return this;
    }

    public Pullo OnStart(Action<Func<Task>> onTaskStart)
    {
        _onStart = onTaskStart;
        return this;
    }

    public Pullo OnSuccess(Action<Func<Task>> onTaskSuccess)
    {
        _onSuccess = onTaskSuccess;
        return this;
    }

    #endregion

    public Pullo Enqueue(Func<Task> task)
    {
        _queue.Enqueue(task);
        return this;
    }

    public Pullo Enqueue(IEnumerable<Func<Task>> tasks)
    {
        _enumerables.Enqueue(tasks);
        return this;
    }

    public int QueueSize() => _queue?.Count ?? 0;

    public void Stop()
    {
        _localCancellation.Cancel();
    }

    public bool IsRunning { get; private set; }

    private IEnumerable<Func<Task>> Enumerate()
    {
        while ((_enumerables.Any() || _queue.Any()) && !_cancellation.IsCancellationRequested)
        {
            while (_queue.TryDequeue(out var task))
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                yield return task;
            }
            while (_enumerables.TryDequeue(out var enumerable))
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                foreach (var task in enumerable)
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    yield return task;
                }
            }
        }
    }

    public async Task Wait()
    {
        try
        {
            IsRunning = true;

            await RunAsync(Enumerate(), async task =>
            {
                _running++;
                try
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    _onStart?.Invoke(task);
                    var result = await _resiliencePolicy.ExecuteAndCaptureAsync(task);
                    if (result.Outcome == OutcomeType.Failure)
                    {
                        if (!IsCancelationException(result.FinalException))
                            _onError?.Invoke(task, result.FinalException);
                    }
                    else
                        _onSuccess?.Invoke(task);
                }
                catch (Exception e)
                {
                    if (!IsCancelationException(e))
                        _onError?.Invoke(task, e);
                }
                finally
                {
                    _running--;
                }
            }, _maxDegreeOfParallelism);
        }
        catch (Exception e) when (!IsCancelationException(e))
        {
            _logger?.LogError(e, e.Message);
            throw;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunAsync<T>(IEnumerable<T> source, Func<T, Task> body, int maxThreadCount)
    {
        var guard = new SemaphoreSlim(maxThreadCount);
                await Task.WhenAll(
                        source
                            .Select(async task =>
                                {
                                     await guard.WaitAsync();
                                     await body(task);
                                     guard.Release();
                                }));
                guard.Dispose();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        try
        {
            _logger.LogInformation("Dispose " + nameof(Pullo));
            IsRunning = false;
        }
        catch (Exception) {/**/}
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Pullo() => Dispose(false);
       
}

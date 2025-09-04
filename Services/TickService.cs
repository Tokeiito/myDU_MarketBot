using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using MarketBot.Interfaces;

namespace MarketBot.Services
{
    public class TickService : ITickService
    {
        private readonly List<ITickable> tickables;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _tickTask;
        private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(1);
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(Environment.ProcessorCount);

        public TickService()
        {
            tickables = new List<ITickable>();
        }

        public void RegisterTickable(ITickable tickable)
        {
            tickables.Add(tickable);
        }

        public void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _tickTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var tasks = tickables.Select(async t =>
                    {
                        await _semaphore.WaitAsync();
                        try
                        {
                            await Task.Run(() => t.Tick());
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    }).ToArray();

                    await Task.WhenAll(tasks);
                    await Task.Delay(_tickInterval);
                }
            });
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _tickTask.Wait();
        }
    }
}

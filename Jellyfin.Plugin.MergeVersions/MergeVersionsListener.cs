using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsListener : IHostedService, IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ITaskManager _taskManager;
        private readonly ILogger _logger;

        private CancellationTokenSource _mergeCancellationTokenSource;
        private bool _mergeScheduled = false;
        private bool _mergeInProgress = false;
        private static readonly object _mergeLock = new object();
        
        private enum MediaType
        {
            Movie,
            Episode
        }

        public MergeVersionsListener(
            ILibraryManager libraryManager, 
            ITaskManager taskManager,
            ILogger<MergeVersionsListener> logger)
        {
            _libraryManager = libraryManager;
            _taskManager = taskManager;
            _logger = logger;
        }

        private void OnLibraryManagerItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (!(e.Item is Movie) && !(e.Item is Episode) || e.Item.LocationType == LocationType.Virtual)
            {
                return;
            }

            ScheduleMerge();
        }

        private void ScheduleMerge()
        {
            lock (_mergeLock)
            {
                if (_mergeScheduled || _mergeInProgress)
                {
                    return;
                }

                _mergeScheduled = true;
                _mergeCancellationTokenSource = new CancellationTokenSource();
                _ = MergeItemsAsync(_mergeCancellationTokenSource.Token);
            }
        }


        private async Task MergeItemsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                lock (_mergeLock)
                {
                    _mergeScheduled = false;
                }
                return;
            }

            lock (_mergeLock)
            {
                _mergeScheduled = false;

                if (_mergeInProgress)
                {
                    return;
                }
                _mergeInProgress = true;
            }

            try
            {
                foreach (var taskName in new[] { "Merge All Movies", "Merge All Episodes" })
                {
                    var task = _taskManager.ScheduledTasks.FirstOrDefault(t => t.Name == taskName);
                    if (task != null)
                    {
                        await _taskManager.Execute(task, new TaskOptions());
                    }
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                lock (_mergeLock)
                {
                    _mergeInProgress = false;
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded += OnLibraryManagerItemAdded;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded -= OnLibraryManagerItemAdded;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) { }
        }

    }
}

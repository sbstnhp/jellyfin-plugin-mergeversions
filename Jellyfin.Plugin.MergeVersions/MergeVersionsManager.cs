using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsManager : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<MergeVersionsManager> _logger;
        private readonly IFileSystem _fileSystem;

        public MergeVersionsManager(
            ILibraryManager libraryManager,
            ILogger<MergeVersionsManager> logger,
            IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
        }

        public async Task MergeMoviesAsync(String name, int? productionYear, bool singleMerge, IProgress<double> progress)
        {
            if (name != null && productionYear != null && singleMerge == true)
            {
                _logger.LogInformation($"Scanning for repeated movies: {name} ({productionYear})");
            } 
            else if (name == null && productionYear == null && singleMerge == false)
            {
                _logger.LogInformation("Scanning for repeated movies");
            }
            else
            {
                return;
            }

            var duplicateMovies = GetMoviesFromLibrary(true)
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear)
                    )
                .GroupBy(x => x.ProviderIds["Tmdb"])
                .Where(x => x.Count() > 1)
                .ToList();
            //_logger.LogInformation($"Movies to process: {duplicateMovies.Count}");

            var current = 0;
            foreach (var m in duplicateMovies)
                {
                    current++;
                    var percent = current / (double)duplicateMovies.Count * 100;
                    progress?.Report((int)percent);
                    //_logger.LogInformation($"Merging {m.ElementAt(0).Name} ({m.ElementAt(0).ProductionYear})");
                    //Stopwatch stopwatch = new Stopwatch();
                    //stopwatch.Start();
                    await MergeVersions(m.Select(m => m.Id).ToList());
                    //stopwatch.Stop();
                    //_logger.LogInformation($"MergeVersions Execution Time: {stopwatch.ElapsedMilliseconds} ms");
                }
            progress?.Report(100);
        }

        public async Task SplitMoviesAsync(String name, int? productionYear, bool checkIsEligible, IProgress<double> progress)
        {
            var movies = GetMoviesFromLibrary(checkIsEligible)
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear)
                    )
                .ToList();
            
            var current = 0;
            foreach (var m in movies)
            {
                current++;
                var percent = current / (double)movies.Count * 100;
                progress?.Report((int)percent);

                _logger.LogInformation($"Splitting Movie: {m.Name} ({m.ProductionYear})");
                //Stopwatch stopwatch = new Stopwatch();
                //stopwatch.Start();
                await DeleteAlternateSources(m.Id);
                //stopwatch.Stop();
                //_logger.LogInformation($"DeleteAlternateSources Execution Time : {stopwatch.ElapsedMilliseconds} ms");                
            }
            progress?.Report(100);
        }

        public async Task MergeEpisodesAsync(
            String name, int? productionYear, string seriesName, int? parentIndexNumber, int? indexNumber, bool singleMerge, IProgress<double> progress)
        {   
            if (name != null && productionYear != null && seriesName != null && parentIndexNumber != null && indexNumber != null && singleMerge == true)
            {
                _logger.LogInformation($"Scanning for repeated episodes on: {seriesName}: S{parentIndexNumber} E{indexNumber} - {name} ({productionYear})");
            } 
            else if (name == null && productionYear == null && seriesName == null && parentIndexNumber == null && indexNumber == null && singleMerge == false)
            {
                _logger.LogInformation("Scanning for repeated episodes");
            }
            else 
            {
                return;
            }

            var duplicateEpisodes = GetEpisodesFromLibrary(true)
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear) &&
                    (string.IsNullOrEmpty(seriesName) || x.SeriesName == seriesName) &&
                    (parentIndexNumber == null || x.ParentIndexNumber == parentIndexNumber) &&
                    (indexNumber == null || x.IndexNumber == indexNumber)
                    )
                .GroupBy(x => x.ProviderIds["Tvdb"])
                .Where(x => x.Count() > 1)
                .ToList();
            //_logger.LogInformation($"Episodes to process: {duplicateEpisodes.Count}");

            var current = 0;
            foreach (var e in duplicateEpisodes)
            {
                current++;
                var percent = current / (double)duplicateEpisodes.Count * 100;
                progress?.Report((int)percent);
                // _logger.LogInformation($"Merging {e.ElementAt(0).SeriesName}: S{e.ElementAt(0).ParentIndexNumber} E{e.ElementAt(0).IndexNumber} - {e.ElementAt(0).Name} ({e.ElementAt(0).ProductionYear})");
                //Stopwatch stopwatch = new Stopwatch();
                //stopwatch.Start();
                await MergeVersions(e.Select(e => e.Id).ToList());
                //stopwatch.Stop();
                //_logger.LogInformation($"MergeVersions Execution Time : {stopwatch.ElapsedMilliseconds} ms");
            }
            progress?.Report(100);
        }

        public async Task SplitEpisodesAsync(
            String name, int? productionYear, string seriesName, int? parentIndexNumber, int? indexNumber, bool checkIsEligible, IProgress<double> progress)
        {
            var episodes = GetEpisodesFromLibrary(checkIsEligible)
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear) &&
                    (string.IsNullOrEmpty(seriesName) || x.SeriesName == seriesName) &&
                    (parentIndexNumber == null || x.ParentIndexNumber == parentIndexNumber) &&
                    (indexNumber == null || x.IndexNumber == indexNumber)
                    )
                .ToList();
            
            var current = 0;
            foreach (var e in episodes)
            {
                current++;
                var percent = current / (double)episodes.Count * 100;
                progress?.Report((int)percent);

                _logger.LogInformation($"Splitting Episode: {e.SeriesName}: S{e.ParentIndexNumber} E{e.IndexNumber}, {e.Name} ({e.ProductionYear})");
                //Stopwatch stopwatch = new Stopwatch();
                //stopwatch.Start();
                await DeleteAlternateSources(e.Id);
                //stopwatch.Stop();
                //_logger.LogInformation($"DeleteAlternateSources Execution Time : {stopwatch.ElapsedMilliseconds} ms");
            }
            progress?.Report(100);
        }

        private List<Movie> GetMoviesFromLibrary(bool checkIsEligible)
        {
            if (checkIsEligible)
            {
                return _libraryManager
                    .GetItemList(
                        new InternalItemsQuery
                        {
                            IncludeItemTypes = [BaseItemKind.Movie],
                            IsVirtualItem = false,
                            Recursive = true,
                        }
                    )
                    .Select(m => m as Movie)
                    .Where(m => m.ProviderIds.ContainsKey("Tmdb"))
                    .Where(IsEligible)
                    .ToList();
            }
            else
            {
                return _libraryManager
                    .GetItemList(
                        new InternalItemsQuery
                        {
                            IncludeItemTypes = [BaseItemKind.Movie],
                            IsVirtualItem = false,
                            Recursive = true,
                        }
                    )
                    .Select(m => m as Movie)
                    .Where(m => m.ProviderIds.ContainsKey("Tmdb"))
                    .ToList();
            }


        }

        private List<Episode> GetEpisodesFromLibrary(bool checkIsEligible)
        {
            if (checkIsEligible)
            {
                return _libraryManager
                    .GetItemList(
                        new InternalItemsQuery
                        {
                            IncludeItemTypes = [BaseItemKind.Episode],
                            IsVirtualItem = false,
                            Recursive = true,
                        }
                    )
                    .Select(m => m as Episode)
                    .Where(m => m.ProviderIds.ContainsKey("Tvdb"))
                    .Where(IsEligible)
                    .ToList();
            }
            else
            {
                return _libraryManager
                    .GetItemList(
                        new InternalItemsQuery
                        {
                            IncludeItemTypes = [BaseItemKind.Episode],
                            IsVirtualItem = false,
                            Recursive = true,
                        }
                    )
                    .Select(m => m as Episode)
                    .Where(m => m.ProviderIds.ContainsKey("Tvdb"))
                    .ToList();
            }
        }

        private async Task MergeVersions(List<Guid> ids)
        {
            var items = ids
                .Select(i => _libraryManager.GetItemById<BaseItem>(i, null))
                .OfType<Video>()
                .OrderBy(i => i.Id)
                .ToList();

            //_logger.LogInformation($"Items to merge: {items.Count}");

            if (items.Count < 2)
            {
                return;
            }

            var primaryVersion = items
                .OrderBy(i => i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile ? 1 : 0)
                .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                .ThenByDescending(i => i.GetDefaultVideoStream()?.BitRate ?? 0)
                .First();

            //_logger.LogInformation($"Got primaryVersion: {primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)}");
            //_logger.LogInformation($"primaryVersion.Path: {primaryVersion.Path}");

            var alternateVersionsOfPrimary = primaryVersion
                .LinkedAlternateVersions
                .ToList();
            //_logger.LogInformation($"Got previous linked alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => ((Guid)l.ItemId).ToString("N", CultureInfo.InvariantCulture)))}");

            foreach (var item in items.Where(i => !i.Id.Equals(primaryVersion.Id)))
            {
                var originalItem = item;

                //_logger.LogInformation($"Currently processing item.Id: {item.Id.ToString("N", CultureInfo.InvariantCulture)}");
                //_logger.LogInformation($"item.Path: {item.Path}");

                //_logger.LogInformation($"item.PrimaryVersionId: {item.PrimaryVersionId} vs. primaryVersion.Id: {primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)}");
                // Only update if the PrimaryVersionId has been changed
                if (item.PrimaryVersionId != primaryVersion.Id)
                {
                    item.SetPrimaryVersionId(primaryVersion.Id);
                    item.OwnerId = primaryVersion.Id;
                    PreserveAlternateVersionLinks(item);
                    
                    await item.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);

                    await _libraryManager.RerouteLinkedChildReferencesAsync(
                        item.Id, primaryVersion.Id
                    )
                    .ConfigureAwait(false);
                }

                if (
                    !alternateVersionsOfPrimary.Any(i => 
                        i.ItemId.HasValue && i.ItemId.Value.Equals(item.Id))
                    )
                {
                    alternateVersionsOfPrimary.Add(
                        new LinkedChild { ItemId = item.Id, Type = LinkedChildType.LinkedAlternateVersion }
                    );

                }
                //_logger.LogInformation($"alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => ((Guid)l.ItemId).ToString("N", CultureInfo.InvariantCulture)))}");

                foreach (var linkedItem in item.LinkedAlternateVersions)
                {
                    if (
                        linkedItem.ItemId.HasValue && 
                        !alternateVersionsOfPrimary.Any(i => 
                            i.ItemId.HasValue && 
                            i.ItemId.Value.Equals(linkedItem.ItemId.Value)
                            )
                        )
                    {
                        alternateVersionsOfPrimary.Add(linkedItem);
                    }
                }

                //_logger.LogInformation($"item.LinkedAlternateVersions.Length: {item.LinkedAlternateVersions.Length}");
                if (item.LinkedAlternateVersions.Length > 0)
                {
                    //_logger.LogInformation($"originalItem.LinkedAlternateVersions: {string.Join(", ", originalItem.LinkedAlternateVersions.Select(l => l.ItemId))} vs. item.LinkedAlternateVersions: {string.Join(", ", item.LinkedAlternateVersions.Select(l => l.ItemId))}");
                    if (!originalItem.LinkedAlternateVersions.SequenceEqual(item.LinkedAlternateVersions))
                    {
                        // Clear LinkedAlternateVersions if there were changes
                        item.LocalAlternateVersions = Array.Empty<string>();
                        item.LinkedAlternateVersions = Array.Empty<LinkedChild>();
                        await item.UpdateToRepositoryAsync(
                            ItemUpdateType.MetadataEdit,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    }
                }
            }

            //_logger.LogInformation($"alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => l.ItemId))} vs. primaryVersion.LinkedAlternateVersions: {string.Join(", ", primaryVersion.LinkedAlternateVersions.Select(l => l.ItemId))} ");
            // Update primary version's LinkedAlternateVersions only if there are changes
            if (!primaryVersion.LinkedAlternateVersions.SequenceEqual(alternateVersionsOfPrimary))
            {
                primaryVersion.LocalAlternateVersions = Array.Empty<string>();
                primaryVersion.LinkedAlternateVersions = alternateVersionsOfPrimary.ToArray();
                primaryVersion.SetPrimaryVersionId(null);
                primaryVersion.OwnerId = Guid.Empty;
                await primaryVersion.UpdateToRepositoryAsync(
                    ItemUpdateType.MetadataEdit,
                    CancellationToken.None
                )
                .ConfigureAwait(false);

                foreach (var alternate in alternateVersionsOfPrimary)
                {
                    _libraryManager.UpsertLinkedChild(
                        primaryVersion.Id,
                        alternate.ItemId.Value,
                        LinkedChildType.LinkedAlternateVersion
                    );
                }
            }
        }

        private async Task DeleteAlternateSources(Guid itemId)
        {
            var item = _libraryManager.GetItemById<Video>(itemId);
            if (item is null)
            {
                return;
            }

            if (item.LinkedAlternateVersions.Length == 0 && item.PrimaryVersionId.HasValue)
            {
                item = _libraryManager.GetItemById<Video>(item.PrimaryVersionId.Value);
            }

            if (item is null)
            {
                return;
            }

            var alternateVersions = GetAllAlternateVersions([item])
                .Where(i => !i.Id.Equals(item.Id))
                .ToList();

            foreach (var alternate in alternateVersions)
            {
                alternate.SetPrimaryVersionId(null);
                alternate.OwnerId = Guid.Empty;
                PreserveAlternateVersionLinks(alternate);

                await alternate.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }

            foreach (var alternate in alternateVersions)
            {
                alternate.LocalAlternateVersions = Array.Empty<string>();
                alternate.LinkedAlternateVersions = Array.Empty<LinkedChild>();
                await alternate.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None
                    )
                .ConfigureAwait(false);
                
            }

            item.LocalAlternateVersions = Array.Empty<string>();
            item.LinkedAlternateVersions = Array.Empty<LinkedChild>();
            item.SetPrimaryVersionId(null);
            item.OwnerId = Guid.Empty;
            await item.UpdateToRepositoryAsync(
                ItemUpdateType.MetadataEdit,
                CancellationToken.None
                )
                .ConfigureAwait(false);
        }

        private void PreserveAlternateVersionLinks(Video version)
        {
            version.LocalAlternateVersions = _libraryManager.GetLocalAlternateVersionIds(version)
                .Select(id => _libraryManager.GetItemById<Video>(id)?.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray();
            version.LinkedAlternateVersions = _libraryManager.GetLinkedAlternateVersions(version)
                .Select(alternate => new LinkedChild
                {
                    ItemId = alternate.Id,
                    Type = LinkedChildType.LinkedAlternateVersion
                })
                .ToArray();
        }

        private List<Video> GetAllAlternateVersions(IEnumerable<Video> initialVersions)
        {
            var versions = new Dictionary<Guid, Video>();
            var pending = new Queue<Video>(initialVersions);

            while (pending.Count > 0)
            {
                var version = pending.Dequeue();
                if (!versions.TryAdd(version.Id, version))
                {
                    continue;
                }

                foreach (var alternateId in _libraryManager.GetLocalAlternateVersionIds(version))
                {
                    if (_libraryManager.GetItemById<Video>(alternateId) is Video alternate)
                    {
                        pending.Enqueue(alternate);
                    }
                }

                foreach (var alternate in _libraryManager.GetLinkedAlternateVersions(version))
                {
                    pending.Enqueue(alternate);
                }
            }

            return versions.Values.ToList();
        }

        private bool IsEligible(BaseItem item)
        {
            if (
                Plugin.Instance.PluginConfiguration.LocationsExcluded != null
                && Plugin.Instance.PluginConfiguration.LocationsExcluded.Any(s =>
                    _fileSystem.ContainsSubPath(s, item.Path)
                )
            )
            {
                return false;
            }
            return true;
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

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Logging;
using Trakt.Api;
using Trakt.Model;
using Timer = System.Timers.Timer;

namespace Trakt.Helpers
{
    internal class LibraryManagerEventsHelper
    {
        private List<LibraryEvent> _queuedEvents;
        private Timer _queueTimer;
        private readonly ILogger _logger ;
        private readonly TraktApi _traktApi;
 
        /// <summary>
        /// 
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="traktApi"></param>
        public LibraryManagerEventsHelper(ILogger logger, TraktApi traktApi)
        {
            _queuedEvents = new List<LibraryEvent>();
            _logger = logger;
            _traktApi = traktApi;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="eventType"></param>
        public void QueueItem(BaseItem item, EventType eventType)
        {
            if (item == null)
                throw new ArgumentNullException("item");

            if (_queueTimer == null)
            {
                _logger.Info("Trakt: Creating queue timer");
                _queueTimer = new Timer(3000); // fire every 3 seconds
                _queueTimer.Elapsed += QueueTimerElapsed;
            }
            else if (_queueTimer.Enabled)
            {
                // If enabled then multiple LibraryManager events are firing. Restart the timer
                _logger.Info("Trakt: Resetting queue timer");
                _queueTimer.Stop();
                _queueTimer.Start();
            }

            if (!_queueTimer.Enabled)
            {
                _logger.Info("Trakt: Starting queue timer");
                _queueTimer.Enabled = true;
            }


            var users = Plugin.Instance.PluginConfiguration.TraktUsers;

            if (users == null || users.Length == 0) return;

            // we need to process the video for each user
            foreach (var user in users)
            {
                foreach (
                    var location in
                        user.TraktLocations.Where(location => item.Path.Contains(location + "\\")))
                {
                    _logger.Info("Trakt: Creating library event for " + item.Name);
                    // we have a match, this user is watching the folder the video is in. Add to queue and they
                    // will be processed when the next timer elapsed event fires.
                    var libraryEvent = new LibraryEvent {Item = item, TraktUser = user, EventType = eventType};
                    _queuedEvents.Add(libraryEvent);
                }
            }

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void QueueTimerElapsed(object sender, ElapsedEventArgs e)
        {
            _logger.Info("Trakt: Timer elapsed - Processing queued items");

            if (!_queuedEvents.Any())
            {
                _logger.Info("Trakt: No events... Stopping queue timer");
                // This may need to go
                _queueTimer.Enabled = false;
                return;
            }

            foreach (var traktUser in Plugin.Instance.PluginConfiguration.TraktUsers)
            {
                var queuedMovieDeletes = _queuedEvents.Where(ev => 
                    ev.TraktUser.LinkedMbUserId == traktUser.LinkedMbUserId && 
                    ev.Item is Movie &&
                    ev.EventType == EventType.Remove).ToList();

                if (queuedMovieDeletes.Any())
                {
                    _logger.Info("Trakt: " + queuedMovieDeletes.Count + " Movie Deletes to Process");
                    ProcessQueuedMovieEvents(queuedMovieDeletes, traktUser, EventType.Remove);
                }
                else
                {
                    _logger.Info("Trakt: No Movie Deletes to Process");
                }

                var queuedMovieAdds = _queuedEvents.Where(ev =>
                    ev.TraktUser.LinkedMbUserId == traktUser.LinkedMbUserId &&
                    ev.Item is Movie &&
                    ev.EventType == EventType.Add).ToList();

                if (queuedMovieAdds.Any())
                {
                    _logger.Info("Trakt: " + queuedMovieAdds.Count + " Movie Adds to Process");
                    ProcessQueuedMovieEvents(queuedMovieDeletes, traktUser, EventType.Add);
                }
                else
                {
                    _logger.Info("Trakt: No Movie Adds to Process");
                }

                var queuedEpisodeDeletes = _queuedEvents.Where(ev =>
                    ev.TraktUser.LinkedMbUserId == traktUser.LinkedMbUserId &&
                    ev.Item is Episode &&
                    ev.EventType == EventType.Remove).ToList();

                if (queuedEpisodeDeletes.Any())
                {
                    _logger.Info("Trakt: " + queuedEpisodeDeletes + " Episode Deletes to Process");
                    ProcessQueuedEpisodeEvents(queuedEpisodeDeletes, traktUser, EventType.Remove);
                }
                else
                {
                    _logger.Info("Trakt: No Episode Deletes to Process");
                }

                var queuedEpisodeAdds = _queuedEvents.Where(ev =>
                    ev.TraktUser.LinkedMbUserId == traktUser.LinkedMbUserId &&
                    ev.Item is Episode &&
                    ev.EventType == EventType.Add).ToList();

                if (queuedEpisodeAdds.Any())
                {
                    _logger.Info("Trakt: " + queuedEpisodeAdds.Count + " Episode Adds to Process");
                    ProcessQueuedEpisodeEvents(queuedEpisodeAdds, traktUser, EventType.Add);
                }
                else
                {
                    _logger.Info("Trakt: No Episode Adds to Process");
                }
            }

            // Everything is processed. Reset the event list.
            _queueTimer.Enabled = false;
            _queuedEvents = new List<LibraryEvent>();

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="events"></param>
        /// <param name="traktUser"></param>
        /// <param name="eventType"></param>
        /// <returns></returns>
        private async Task ProcessQueuedMovieEvents(IEnumerable<LibraryEvent> events, TraktUser traktUser, EventType eventType)
        {
            var movies = events.Select(libraryEvent => (Movie) libraryEvent.Item).ToList();

            await _traktApi.SendLibraryUpdateAsync(movies, traktUser, CancellationToken.None, eventType);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="events"></param>
        /// <param name="traktUser"></param>
        /// <param name="eventType"></param>
        /// <returns></returns>
        private async Task ProcessQueuedEpisodeEvents(IEnumerable<LibraryEvent> events, TraktUser traktUser, EventType eventType)
        {
            var episodes = events.Select(libraryEvent => (Episode) libraryEvent.Item).OrderBy(i => i.SeriesItemId).ToList();

            // Can't progress further without episodes
            if (!episodes.Any())
            {
                _logger.Info("Trakt: episodes count is 0");

                return;
            }

            _logger.Info("Trakt: episodes count - " + episodes.Count);

            var payload = new List<Episode>();
            var currentSeriesId = episodes[0].SeriesItemId; 

            foreach (var ep in episodes)
            {
                if (!currentSeriesId.Equals(ep.SeriesItemId))
                {
                    // We're starting a new series. Time to send the current one to trakt.tv
                    await _traktApi.SendLibraryUpdateAsync(payload, traktUser, CancellationToken.None, eventType);
                    
                    currentSeriesId = ep.SeriesItemId;
                    payload.Clear();
                }

                payload.Add(ep);
            }

            if (payload.Any())
                await _traktApi.SendLibraryUpdateAsync(payload, traktUser, CancellationToken.None, eventType);
        }
    }

    #region internal helper types

    internal class LibraryEvent
    {
        public BaseItem Item { get; set; }
        public TraktUser TraktUser { get; set; }
        public EventType EventType { get; set; }
    }

    public enum EventType
    {
        Add,
        Remove,
        Update
    }

    #endregion
}

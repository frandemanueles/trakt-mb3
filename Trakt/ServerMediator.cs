﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Serialization;
using System.Linq;
using Trakt.Api;

namespace Trakt
{
    /// <summary>
    /// All communication between the server and the plugins server instance should occur in this class.
    /// </summary>
    public class ServerMediator : IServerEntryPoint
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;
        private readonly IUserManager _userManager;
        private TraktApi _traktApi;



        /// <summary>
        /// 
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="jsonSerializer"></param>
        /// <param name="userManager"></param>
        public ServerMediator(IHttpClient httpClient, IJsonSerializer jsonSerializer, IUserManager userManager)
        {
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _userManager = userManager;
            _traktApi = new TraktApi(_httpClient, _jsonSerializer);
        }



        /// <summary>
        /// 
        /// </summary>
        public void Run()
        {
            _userManager.PlaybackStart += KernelPlaybackStart;
            _userManager.PlaybackProgress += KernelPlaybackProgress;
            _userManager.PlaybackStopped += KernelPlaybackStopped;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void KernelPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            // Since MB3 is user profile friendly, I'm going to need to do a user lookup every time something starts
            var traktUser = UserHelper.GetTraktUser(e.User);

            if (traktUser == null) return;
            // Still need to make sure it's a trakt monitored location before sending notice to trakt.tv
            if (traktUser.TraktLocations == null) return;

            foreach (
                var location in
                    traktUser.TraktLocations.Where(location => e.Argument.Path.Contains(location + "\\")).Where(
                        location => e.Argument is Episode || e.Argument is Movie))
            {
                var video = e.Argument as Video;

                if (video is Movie)
                {
                    await _traktApi.SendMovieStatusUpdateAsync(video as Movie, MediaStatus.Watching, traktUser).ConfigureAwait(false);
                }
                else if (video is Episode)
                {
                    await _traktApi.SendEpisodeStatusUpdateAsync(video as Episode, MediaStatus.Watching, traktUser).ConfigureAwait(false);
                }
            }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void KernelPlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void KernelPlaybackStopped(object sender, PlaybackProgressEventArgs e)
        {
            if (e.PlaybackPositionTicks == null) return;

            var userData = e.Argument.GetUserData(e.User, false);

            if (userData.Played)
            {
                var traktUser = UserHelper.GetTraktUser(e.User);

                if (traktUser == null) return;

                // Still need to make sure it's a trakt monitored location before sending notice to trakt.tv
                if (traktUser.TraktLocations == null) return;

                foreach (
                    var location in 
                        traktUser.TraktLocations.Where(location => e.Argument.Path.Contains(location + "\\")).Where(
                            location => e.Argument is Episode || e.Argument is Movie))
                {
                    var video = e.Argument as Video;

                    if (video is Movie)
                    {
                        await _traktApi.SendMovieStatusUpdateAsync(video as Movie, MediaStatus.Scrobble, traktUser).ConfigureAwait(false);
                    }
                    else if (video is Episode)
                    {
                        await _traktApi.SendEpisodeStatusUpdateAsync(video as Episode, MediaStatus.Scrobble, traktUser).ConfigureAwait(false);
                    }
                }
            }
        }



        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _userManager.PlaybackStart -= KernelPlaybackStart;
            _userManager.PlaybackProgress -= KernelPlaybackProgress;
            _userManager.PlaybackStopped -= KernelPlaybackStopped;
            _traktApi = null;
        }
    }
}
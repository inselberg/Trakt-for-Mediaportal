﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using TraktPlugin.TraktAPI.DataStructures;
using TraktPlugin.TraktHandlers;

namespace TraktPlugin.TraktAPI
{
    #region Enumerables
    /// <summary>
    /// List of Scrobble States
    /// </summary>
    public enum TraktScrobbleStates
    {
        watching,
        scrobble,
        cancelwatching
    }

    /// <summary>
    /// List of Sync Modes
    /// </summary>
    public enum TraktSyncModes
    {
        library,
        seen,
        unlibrary,
        unseen
    }

    /// <summary>
    /// List of Clearing Modes
    /// </summary>
    public enum TraktClearingModes
    {
        all,
        movies,
        episodes
    }

    /// <summary>
    /// List of Rate Types
    /// </summary>
    public enum TraktRateType
    {
        episode,
        show,
        movie
    }

    /// <summary>
    /// List of Rate Values
    /// </summary>
    public enum TraktRateValue
    {
        love,
        hate
    }

    #endregion

    /// <summary>
    /// Object that communicates with the Trakt API
    /// </summary>
    class TraktAPI
    {
        public static string UserAgent { get; set; }

        #region Scrobbling

        /// <summary>
        /// Sends Scrobble data to Trakt
        /// </summary>
        /// <param name="scrobbleData">The Data to send</param>
        /// <param name="status">The mode to send it as</param>
        /// <returns>The response from Trakt</returns>
        public static TraktResponse ScrobbleMovieState(TraktMovieScrobble scrobbleData, TraktScrobbleStates status)
        {
            //If we are cancelling a scrobble we don't need data
            if (status != TraktScrobbleStates.cancelwatching)
            {
                // check that we have everything we need
                // server can accept title if movie id is not supplied
                if (scrobbleData == null || string.IsNullOrEmpty(scrobbleData.Title) || string.IsNullOrEmpty(scrobbleData.Year))
                {
                    TraktResponse error = new TraktResponse
                    {
                        Error = "Not enough information to send to server",
                        Status = "failure"
                    };
                    return error;
                }
            }
            TraktLogger.Info(string.Format("Trakt: {0} '{1}'", status, scrobbleData.Title));

            // serialize Scrobble object to JSON and send to server
            string response = Transmit(string.Format(TraktURIs.ScrobbleMovie, status.ToString()), scrobbleData.ToJSON());

            // return success or failure
            return response.FromJSON<TraktResponse>();
        }

        /// <summary>
        /// Sends Scrobble data to Trakt
        /// </summary>
        /// <param name="scrobbleData">The Data to send</param>
        /// <param name="status">The mode to send it as</param>
        /// <returns>The response from Trakt</returns>
        public static TraktResponse ScrobbleEpisodeState(TraktEpisodeScrobble scrobbleData, TraktScrobbleStates status)
        {
            // check that we have everything we need
            // server can accept title if movie id is not supplied
            if (status != TraktScrobbleStates.cancelwatching)
            {
                if (scrobbleData == null || string.IsNullOrEmpty(scrobbleData.SeriesID))
                {
                    TraktResponse error = new TraktResponse
                    {
                        Error = "Not enough information to send to server",
                        Status = "failure"
                    };
                    return error;
                }
                TraktLogger.Info(string.Format("Trakt: {0} '{1}'", status, scrobbleData.Title));
            }
            
            // serialize Scrobble object to JSON and send to server
            string response = Transmit(string.Format(TraktURIs.ScrobbleShow, status.ToString()), scrobbleData.ToJSON());

            // return success or failure
            return response.FromJSON<TraktResponse>();
        }

        #endregion

        #region Syncing

        /// <summary>
        /// Sends movie sync data to Trakt
        /// </summary>
        /// <param name="syncData">The sync data to send</param>
        /// <param name="mode">The sync mode to use</param>
        /// <returns>The response from trakt</returns>
        public static TraktResponse SyncMovieLibrary(TraktMovieSync syncData, TraktSyncModes mode)
        {
            // check that we have everything we need
            // server can accept title/year if imdb id is not supplied
            if (syncData == null || syncData.MovieList.Count == 0)
            {
                TraktResponse error = new TraktResponse
                {
                    Error = "Not enough information to send to server",
                    Status = "failure"
                };
                return error;
            }

            // serialize Scrobble object to JSON and send to server
            string response = Transmit(string.Format(TraktURIs.SyncMovieLibrary, mode.ToString()), syncData.ToJSON());

            // return success or failure
            return response.FromJSON<TraktResponse>();
        }

        /// <summary>
        /// Sends episode sync data to Trakt
        /// </summary>
        /// <param name="syncData">The sync data to send</param>
        /// <param name="mode">The sync mode to use</param>
        public static TraktResponse SyncEpisodeLibrary(TraktEpisodeSync syncData, TraktSyncModes mode)
        {
            // check that we have everything we need
            // server can accept title/year if imdb id is not supplied
            if (syncData == null || string.IsNullOrEmpty(syncData.SeriesID))
            {
                TraktResponse error = new TraktResponse
                {
                    Error = "Not enough information to send to server",
                    Status = "failure"
                };
                return error;
            }

            // serialize Scrobble object to JSON and send to server
            string response = Transmit(string.Format(TraktURIs.SyncEpisodeLibrary, mode.ToString()), syncData.ToJSON());

            // return success or failure
            return response.FromJSON<TraktResponse>();
        }

        #endregion

        #region Trakt Library Calls

        /// <summary>
        /// Gets the trakt movie library for a user
        /// </summary>
        /// <param name="user">The user to get</param>
        /// <returns>The trakt movie library</returns>
        public static IEnumerable<TraktLibraryMovies> GetMoviesForUser(string user)
        {
            TraktLogger.Info("Trakt: Getting user {0}'s movies", user);
            //Get the library
            string moviesForUser = Transmit(string.Format(TraktURIs.UserLibraryMovies, user), GetUserAuthentication());
            TraktLogger.Debug("Trakt: {0}", moviesForUser);
            //hand it on
            return moviesForUser.FromJSONArray<TraktLibraryMovies>();
        }

        /// <summary>
        /// Gets the trakt episode library for a user
        /// </summary>
        /// <param name="user">The user to get</param>
        /// <returns>The trakt episode library</returns>
        public static IEnumerable<TraktLibraryShows> GetLibraryEpisodesForUser(string user)
        {
            TraktLogger.Info("Trakt: Getting user {0}'s 'library' episodes", user);
            string showsForUser = Transmit(string.Format(TraktURIs.UserLibraryEpisodes, user), GetUserAuthentication());
            TraktLogger.Debug("Trakt: {0}", showsForUser);
            return showsForUser.FromJSONArray<TraktLibraryShows>();
        }

        /// <summary>
        /// Gets the trakt watched/seen episodes for a user
        /// </summary>
        /// <param name="user">The user to get</param>
        /// <returns>The trakt episode library</returns>
        public static IEnumerable<TraktLibraryShows> GetWatchedEpisodesForUser(string user)
        {
            TraktLogger.Info("Trakt: Getting user {0}'s 'watched/seen' episodes", user);
            string showsForUser = Transmit(string.Format(TraktURIs.UserWatchedEpisodes, user), GetUserAuthentication());
            TraktLogger.Debug("Trakt: {0}", showsForUser);
            return showsForUser.FromJSONArray<TraktLibraryShows>();
        }

        #endregion

        #region Rating

        /// <summary>
        /// Sends episode rate data to Trakt
        /// </summary>
        /// <param name="episode">The Trakt rate data to send</param>
        /// <returns>The response from Trakt</returns>
        public static TraktRateResponse RateEpisode(TraktRateEpisode episode)
        {
            string response = Transmit(string.Format(TraktURIs.RateItem, TraktRateType.episode.ToString()), episode.ToJSON());
            return response.FromJSON<TraktRateResponse>();
        }

        /// <summary>
        /// Sends series rate data to Trakt
        /// </summary>
        /// <param name="episode">The Trakt rate data to send</param>
        /// <returns>The response from Trakt</returns>
        public static TraktRateResponse RateSeries(TraktRateSeries series)
        {
            string response = Transmit(string.Format(TraktURIs.RateItem, TraktRateType.show.ToString()), series.ToJSON());
            return response.FromJSON<TraktRateResponse>();
        }

        /// <summary>
        /// Sends movie rate data to Trakt
        /// </summary>
        /// <param name="episode">The Trakt rate data to send</param>
        /// <returns>The response from Trakt</returns>
        public static TraktRateResponse RateMovie(TraktRateMovie movie)
        {
            string response = Transmit(string.Format(TraktURIs.RateItem, TraktRateType.movie.ToString()), movie.ToJSON());
            return response.FromJSON<TraktRateResponse>();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets a User Authentication object
        /// </summary>       
        /// <returns>The User Authentication json string</returns>
        private static string GetUserAuthentication()
        {
            return new TraktAuthentication { Username = TraktSettings.Username, Password = TraktSettings.Password }.ToJSON();
        }

        /// <summary>
        /// Communicates to and from Trakt
        /// </summary>
        /// <param name="address">The URI to use</param>
        /// <param name="data">The Data to send</param>
        /// <returns>The response from Trakt</returns>
        private static string Transmit(string address, string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                TraktLogger.Debug("Trakt Post: {0} Address: {1}", data, address);
            }

            try
            {
                WebClient client = new WebClient();
                client.Headers.Add("user-agent", UserAgent);
                return client.UploadString(address, data);
            }
            catch (WebException e)
            {
                // something bad happened e.g. invalid login
                TraktResponse error = new TraktResponse
                {
                    Status = "failure",
                    Error = e.Message
                };
                return error.ToJSON();
            }
        }

        /// <summary>
        /// Clears our library on Trakt as best as the api lets us
        /// </summary>
        /// <param name="mode">What to remove from Trakt</param>
        public static void ClearLibrary(TraktClearingModes mode, ProgressDialog progressDialog)
        {
            progressDialog.Title = "Clearing Library";
            progressDialog.CancelMessage = "Attempting to Cancel";
            progressDialog.Maximum = 100;
            progressDialog.Value = 0;
            progressDialog.Line1 = "Clearing your Trakt Library";
            progressDialog.ShowDialog(ProgressDialog.PROGDLG.Modal, ProgressDialog.PROGDLG.NoMinimize, ProgressDialog.PROGDLG.NoTime);

            //Movies
            if (mode == TraktClearingModes.all || mode == TraktClearingModes.movies)
            {
                TraktLogger.Info("Trakt: Removing Movies from Trakt");
                TraktLogger.Info("Trakt: NOTE WILL NOT REMOVE SCROBBLED MOVIES DUE TO API LIMITATION");
                progressDialog.Line2 = "Getting movies for user";
                List<TraktLibraryMovies> movies = GetMoviesForUser(TraktSettings.Username).ToList();

                var syncData = BasicHandler.CreateMovieSyncData(movies);

                TraktLogger.Info("Trakt: First removing movies from seen");
                progressDialog.Line2 = "Setting seen movies as unseen";
                TraktResponse response = SyncMovieLibrary(syncData, TraktSyncModes.unseen);
                LogTraktResponse(response);
                
                TraktLogger.Info("Trakt: Now removing movies from library");
                progressDialog.Line2 = "Removing movies from library";
                response = SyncMovieLibrary(syncData, TraktSyncModes.unlibrary);
                LogTraktResponse(response);

                TraktLogger.Info("Trakt: Removed all movies possible, some manual clean up may be required");
            }
            if(mode == TraktClearingModes.all)
                progressDialog.Value = 15;
            if (progressDialog.HasUserCancelled)
            {
                TraktLogger.Info("Trakt: Cancelling Library Clearing");
                progressDialog.CloseDialog();
                return;
            }
            //Episodes
            if (mode == TraktClearingModes.all || mode == TraktClearingModes.episodes)
            {
                TraktLogger.Info("Trakt: Removing Shows from Trakt");
                TraktLogger.Info("Trakt: NOTE WILL NOT REMOVE SCROBBLED SHOWS DUE TO API LIMITATION");

                TraktLogger.Info("Trakt: First removing shows from seen");
                progressDialog.Line2 = "Getting Watched Episodes from Trakt";
                foreach (var series in GetWatchedEpisodesForUser(TraktSettings.Username).ToList())
                {
                    TraktLogger.Info("Trakt: Removing '{0}' from seen", series.ToString());
                    progressDialog.Line2 = string.Format("Setting {0} as unseen", series.ToString());
                    TraktResponse response = SyncEpisodeLibrary(BasicHandler.CreateEpisodeSyncData(series), TraktSyncModes.unseen);
                    LogTraktResponse(response);
                    System.Threading.Thread.Sleep(500);
                    if (progressDialog.HasUserCancelled)
                    {
                        TraktLogger.Info("Trakt: Cancelling Library Clearing");
                        progressDialog.CloseDialog();
                        return;
                    }
                }
                progressDialog.Value = 85;
                TraktLogger.Info("Trakt: Now removing shows from library");
                progressDialog.Line2 = "Getting Library Episodes from Trakt";
                foreach(var series in GetLibraryEpisodesForUser(TraktSettings.Username).ToList())
                {
                    TraktLogger.Info("Trakt: Removing '{0}' from library", series.ToString());
                    progressDialog.Line2 = string.Format("Removing {0} from library", series.ToString());
                    TraktResponse response = SyncEpisodeLibrary(BasicHandler.CreateEpisodeSyncData(series), TraktSyncModes.unlibrary);
                    LogTraktResponse(response);
                    System.Threading.Thread.Sleep(500);
                    if (progressDialog.HasUserCancelled)
                    {
                        TraktLogger.Info("Trakt: Cancelling Library Clearing");
                        progressDialog.CloseDialog();
                        return;
                    }
                }
                TraktLogger.Info("Trakt: Removed all shows possible, some manual clean up may be required");
            }
            progressDialog.Value = 100;
            progressDialog.CloseDialog();
        }

        /// <summary>
        /// Logs the result of Trakt api call
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="response"></param>
        public static void LogTraktResponse<T>(T response)
        {
            var r = response as TraktResponse;

            if (r == null || r.Status == null)
            {
                TraktLogger.Error("Response from server was unexpected.");
                return;
            }

            // check response error status
            if (r.Status != "success")
            {
                TraktLogger.Error(r.Error);
            }
            else
            {
                // success
                TraktLogger.Info("Trakt Response: {0}", r.Message);
            }
        }

        #endregion
    }
}

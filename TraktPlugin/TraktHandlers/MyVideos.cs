﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TraktPlugin.GUI;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;
using MediaPortal.Player;
using System.Reflection;
using System.ComponentModel;
using MediaPortal.Video.Database;
using System.Threading;

namespace TraktPlugin.TraktHandlers
{
    class MyVideos : ITraktHandler
    {
        #region Variables

        Timer TraktTimer;        
        IMDBMovie CurrentMovie = null;

        #endregion

        #region Constructor

        public MyVideos(int priority)
        {
            Priority = priority;
        }

        #endregion

        #region ITraktHandler

        public string Name
        {
            get { return "My Videos"; }
        }

        public int Priority { get; set; }
       
        public void SyncLibrary()
        {
            TraktLogger.Info("My Videos Starting Sync");

            // get all movies
            ArrayList myvideos = new ArrayList();
            VideoDatabase.GetMovies(ref myvideos);

            List<IMDBMovie> MovieList = (from IMDBMovie movie in myvideos select movie).ToList();

            #region Remove Blocked Movies
            MovieList.RemoveAll(m => TraktSettings.BlockedFolders.Any(f => m.Path.ToLowerInvariant().Contains(f.ToLowerInvariant())));

            List<int> blockedMovieIds = new List<int>();
            foreach(string file in TraktSettings.BlockedFilenames)
            {
                int pathId = 0;
                int movieId = 0;

                // get a list of ids for blocked filenames
                // filename seems to always be empty for an IMDBMovie object!
                if (VideoDatabase.GetFile(file, out pathId, out movieId, false) > 0)
                {
                    blockedMovieIds.Add(movieId);
                }
            }
            MovieList.RemoveAll(m => blockedMovieIds.Contains(m.ID));
            #endregion

            #region Skipped Movies Check
            // Remove Skipped Movies from previous Sync
            if (TraktSettings.SkippedMovies != null)
            {
                // allow movies to re-sync again after 7-days in the case user has addressed issue ie. edited movie or added to themoviedb.org
                if (TraktSettings.SkippedMovies.LastSkippedSync.FromEpoch() > DateTime.UtcNow.Subtract(new TimeSpan(7, 0, 0, 0)))
                {
                    if (TraktSettings.SkippedMovies.Movies != null && TraktSettings.SkippedMovies.Movies.Count > 0)
                    {
                        TraktLogger.Info("Skipping {0} movies due to invalid data or movies don't exist on http://themoviedb.org. Next check will be {1}.", TraktSettings.SkippedMovies.Movies.Count, TraktSettings.SkippedMovies.LastSkippedSync.FromEpoch().Add(new TimeSpan(7, 0, 0, 0)));
                        foreach (var movie in TraktSettings.SkippedMovies.Movies)
                        {
                            TraktLogger.Info("Skipping movie, Title: {0}, Year: {1}, IMDb: {2}", movie.Title, movie.Year, movie.IMDBID);
                            MovieList.RemoveAll(m => (m.Title == movie.Title) && (m.Year.ToString() == movie.Year) && (m.IMDBNumber == movie.IMDBID));
                        }
                    }
                }
                else
                {
                    if (TraktSettings.SkippedMovies.Movies != null) TraktSettings.SkippedMovies.Movies.Clear();
                    TraktSettings.SkippedMovies.LastSkippedSync = DateTime.UtcNow.ToEpoch();
                }
            }
            #endregion

            #region Already Exists Movie Check
            // Remove Already-Exists Movies, these are typically movies that are using aka names and no IMDb/TMDb set
            // When we compare our local collection with trakt collection we have english only titles, so if no imdb/tmdb exists
            // we need to fallback to title matching. When we sync aka names are sometimes accepted if defined on themoviedb.org so we need to 
            // do this to revent syncing these movies every sync interval.
            if (TraktSettings.AlreadyExistMovies != null && TraktSettings.AlreadyExistMovies.Movies != null && TraktSettings.AlreadyExistMovies.Movies.Count > 0)
            {
                TraktLogger.Debug("Skipping {0} movies as they already exist in trakt library but failed local match previously.", TraktSettings.AlreadyExistMovies.Movies.Count.ToString());
                var movies = new List<TraktMovieSync.Movie>(TraktSettings.AlreadyExistMovies.Movies);
                foreach (var movie in movies)
                {
                    Predicate<IMDBMovie> criteria = m => (m.Title == movie.Title) && (m.Year.ToString() == movie.Year) && (m.IMDBNumber == movie.IMDBID);
                    if (MovieList.Exists(criteria))
                    {
                        TraktLogger.Debug("Skipping movie, Title: {0}, Year: {1}, IMDb: {2}", movie.Title, movie.Year, movie.IMDBID);
                        MovieList.RemoveAll(criteria);
                    }
                    else
                    {
                        // remove as we have now removed from our local collection or updated movie signature
                        if (TraktSettings.MoviePluginCount == 1)
                        {
                            TraktLogger.Debug("Removing 'AlreadyExists' movie, Title: {0}, Year: {1}, IMDb: {2}", movie.Title, movie.Year, movie.IMDBID);
                            TraktSettings.AlreadyExistMovies.Movies.Remove(movie);
                        }
                    }
                }
            }
            #endregion

            TraktLogger.Info("{0} movies available to sync in My Videos database", MovieList.Count.ToString());

            // get the movies that we have watched
            List<IMDBMovie> SeenList = MovieList.Where(m => m.Watched > 0).ToList();

            TraktLogger.Info("{0} watched movies available to sync in My Videos database", SeenList.Count.ToString());

            // get the movies that we have yet to watch                        
            IEnumerable<TraktLibraryMovies> traktMoviesAll = TraktAPI.TraktAPI.GetAllMoviesForUser(TraktSettings.Username);
            if (traktMoviesAll == null)
            {
                TraktLogger.Error("Error getting movies from trakt server, cancelling sync.");
                return;
            }
            TraktLogger.Info("{0} movies in trakt library", traktMoviesAll.Count().ToString());

            #region Movies to Sync to Collection
            List<IMDBMovie> moviesToSync = new List<IMDBMovie>(MovieList);
            List<TraktLibraryMovies> NoLongerInOurCollection = new List<TraktLibraryMovies>();            
            //Filter out a list of movies we have already sync'd in our collection
            foreach (TraktLibraryMovies tlm in traktMoviesAll)
            {
                bool notInLocalCollection = true;
                // if it is in both libraries
                foreach (IMDBMovie libraryMovie in MovieList.Where(m => BasicHandler.GetProperMovieImdbId(m.IMDBNumber) == tlm.IMDBID || (string.Compare(m.Title, tlm.Title, true) == 0 && m.Year.ToString() == tlm.Year)))
                {
                    // If the users IMDb Id is empty/invalid and we have matched one then set it
                    if (BasicHandler.IsValidImdb(tlm.IMDBID) && !BasicHandler.IsValidImdb(libraryMovie.IMDBNumber))
                    {
                        TraktLogger.Info("Movie '{0}' inserted IMDb Id '{1}'", libraryMovie.Title, tlm.IMDBID);
                        libraryMovie.IMDBNumber = tlm.IMDBID;
                        IMDBMovie details = libraryMovie;
                        VideoDatabase.SetMovieInfoById(libraryMovie.ID, ref details);
                    }

                    // if it is watched in Trakt but not My Videos update
                    // skip if movie is watched but user wishes to have synced as unseen locally
                    if (tlm.Plays > 0 && !tlm.UnSeen && libraryMovie.Watched == 0)
                    {
                        TraktLogger.Info("Movie '{0}' is watched on Trakt updating Database", libraryMovie.Title);
                        libraryMovie.Watched = 1;
                        IMDBMovie details = libraryMovie;
                        VideoDatabase.SetMovieInfoById(libraryMovie.ID, ref details);
                    }

                    // mark movies as unseen if watched locally
                    if (tlm.UnSeen && libraryMovie.Watched > 0)
                    {
                        TraktLogger.Info("Movie '{0}' is unseen on Trakt, updating database", libraryMovie.Title);
                        libraryMovie.Watched = 0;
                        IMDBMovie details = libraryMovie;
                        VideoDatabase.SetMovieInfoById(libraryMovie.ID, ref details);
                    }

                    notInLocalCollection = false;

                    //filter out if its already in collection
                    if (tlm.InCollection)
                    {
                        moviesToSync.RemoveAll(m => (BasicHandler.GetProperMovieImdbId(m.IMDBNumber) == tlm.IMDBID) || (string.Compare(m.Title, tlm.Title, true) == 0 && m.Year.ToString() == tlm.Year));
                    }
                    break;
                }

                if (notInLocalCollection && tlm.InCollection)
                    NoLongerInOurCollection.Add(tlm);
            }
            #endregion

            #region Movies to Sync to Seen Collection
            // filter out a list of movies already marked as watched on trakt
            // also filter out movie marked as unseen so we dont reset the unseen cache online
            List<IMDBMovie> watchedMoviesToSync = new List<IMDBMovie>(SeenList);
            foreach (TraktLibraryMovies tlm in traktMoviesAll.Where(t => t.Plays > 0 || t.UnSeen))
            {
                foreach (IMDBMovie watchedMovie in SeenList.Where(m => BasicHandler.GetProperMovieImdbId(m.IMDBNumber) == tlm.IMDBID || (string.Compare(m.Title, tlm.Title, true) == 0 && m.Year.ToString() == tlm.Year)))
                {
                    //filter out
                    watchedMoviesToSync.Remove(watchedMovie);
                }
            }
            #endregion

            //Send Library/Collection
            TraktLogger.Info("{0} movies need to be added to Library", moviesToSync.Count.ToString());
            foreach (IMDBMovie m in moviesToSync)
                TraktLogger.Info("Sending movie to trakt library, Title: {0}, Year: {1}, IMDb: {2}", m.Title, m.Year.ToString(), m.IMDBNumber);

            if (moviesToSync.Count > 0)
            {
                TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(moviesToSync), TraktSyncModes.library);
                BasicHandler.InsertSkippedMovies(response);
                BasicHandler.InsertAlreadyExistMovies(response);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            }

            //Send Seen
            TraktLogger.Info("{0} movies need to be added to SeenList", watchedMoviesToSync.Count.ToString());
            foreach (IMDBMovie m in watchedMoviesToSync)
                TraktLogger.Info("Sending movie to trakt as seen, Title: {0}, Year: {1}, IMDb: {2}", m.Title, m.Year.ToString(), m.IMDBNumber);

            if (watchedMoviesToSync.Count > 0)
            {
                TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(watchedMoviesToSync), TraktSyncModes.seen);
                BasicHandler.InsertSkippedMovies(response);
                BasicHandler.InsertAlreadyExistMovies(response);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            }

            //Dont clean library if more than one movie plugin installed
            if (TraktSettings.KeepTraktLibraryClean && TraktSettings.MoviePluginCount == 1)
            {
                //Remove movies we no longer have in our local database from Trakt
                foreach (var m in NoLongerInOurCollection)
                    TraktLogger.Info("Removing from Trakt Collection {0}", m.Title);

                TraktLogger.Info("{0} movies need to be removed from Trakt Collection", NoLongerInOurCollection.Count.ToString());

                if (NoLongerInOurCollection.Count > 0)
                {
                    if (TraktSettings.AlreadyExistMovies != null && TraktSettings.AlreadyExistMovies.Movies != null && TraktSettings.AlreadyExistMovies.Movies.Count > 0)
                    {
                        TraktLogger.Warning("DISABLING CLEAN LIBRARY!!!, there are trakt library movies that can't be determined to be local in collection.");
                        TraktLogger.Warning("To fix this, check the 'already exist' entries in log, then check movies in local collection against this list and ensure IMDb id is set then run sync again.");
                    }
                    else
                    {
                        //Then remove from library
                        TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(BasicHandler.CreateMovieSyncData(NoLongerInOurCollection), TraktSyncModes.unlibrary);
                        TraktAPI.TraktAPI.LogTraktResponse(response);
                    }
                }
            }

            TraktLogger.Info("My Videos Sync Completed");
        }

        public bool Scrobble(string filename)
        {
            StopScrobble();

            // stop check if not valid player type for plugin handler
            if (g_Player.IsTV || g_Player.IsTVRecording) return false;

            // lookup movie by filename
            IMDBMovie movie = new IMDBMovie();
            int result = VideoDatabase.GetMovieInfo(filename, ref movie);
            if (result == -1) return false;

            CurrentMovie = movie;

            // create timer 15 minute timer to send watching status
            #region scrobble timer
            TraktTimer = new Timer(new TimerCallback((stateInfo) =>
            {
                Thread.CurrentThread.Name = "Scrobble Movie";

                IMDBMovie currentMovie = stateInfo as IMDBMovie;

                TraktLogger.Info("Scrobbling Movie {0}", currentMovie.Title);

                // get online runtime if local one unavailable to be retrieved
                double duration = g_Player.Duration == 0.0 ? currentMovie.RunTime * 60.0 : g_Player.Duration;
                double progress = 0.0;

                // get current progress of player (in seconds) to work out percent complete
                if (duration > 0.0)
                    progress = (g_Player.CurrentPosition / duration) * 100.0;

                TraktLogger.Debug(string.Format("Percentage of {0} is {1}%", currentMovie.Title, progress.ToString("N2")));

                // create Scrobbling Data
                TraktMovieScrobble scrobbleData = CreateScrobbleData(currentMovie);
                if (scrobbleData == null) return;

                // set duration/progress in scrobble data
                scrobbleData.Duration = Convert.ToInt32(duration / 60).ToString();
                scrobbleData.Progress = Convert.ToInt32(progress).ToString();

                // set watching status on trakt
                TraktResponse response = TraktAPI.TraktAPI.ScrobbleMovieState(scrobbleData, TraktScrobbleStates.watching);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            }), movie, 3000, 900000);
            #endregion

            return true;
        }

        public void StopScrobble()
        {
            if (TraktTimer != null)
                TraktTimer.Dispose();

            if (CurrentMovie == null) return;

            // if movie is atleast 90% complete, consider watched
            if ((g_Player.CurrentPosition / (g_Player.Duration == 0.0 ? CurrentMovie.RunTime * 60.0 : g_Player.Duration)) >= 0.9)
            {
                ShowRateDialog(CurrentMovie);

                #region scrobble
                Thread scrobbleMovie = new Thread(delegate(object o)
                {
                    IMDBMovie movie = o as IMDBMovie;
                    if (movie == null) return;

                    TraktLogger.Info("MyVideos movie considered watched '{0}'", movie.Title);

                    // get scrobble data to send to api
                    TraktMovieScrobble scrobbleData = CreateScrobbleData(movie);
                    if (scrobbleData == null) return;

                    // set duration/progress in scrobble data
                    Double duration = g_Player.Duration == 0.0 ? movie.RunTime * 60.0 : g_Player.Duration;
                    scrobbleData.Duration = Convert.ToInt32(duration / 60.0).ToString();
                    scrobbleData.Progress = "100";

                    TraktResponse response = TraktAPI.TraktAPI.ScrobbleMovieState(scrobbleData, TraktScrobbleStates.scrobble);
                    TraktAPI.TraktAPI.LogTraktResponse(response);
                })
                {
                    IsBackground = true,
                    Name = "Scrobble Movie"
                };

                scrobbleMovie.Start(CurrentMovie);
                #endregion
            }
            else
            {
                #region cancel watching
                TraktLogger.Info("Stopped MyVideos movies playback '{0}'", CurrentMovie.Title);

                // stop scrobbling
                Thread cancelWatching = new Thread(delegate()
                {
                    TraktMovieScrobble scrobbleData = new TraktMovieScrobble { UserName = TraktSettings.Username, Password = TraktSettings.Password };
                    TraktResponse response = TraktAPI.TraktAPI.ScrobbleMovieState(scrobbleData, TraktScrobbleStates.cancelwatching);
                    TraktAPI.TraktAPI.LogTraktResponse(response);
                })
                {
                    IsBackground = true,
                    Name = "Cancel Watching Movie"
                };

                cancelWatching.Start();
                #endregion
            }

            CurrentMovie = null;            
        }

        #endregion

        #region DataCreators

        /// <summary>
        /// Creates Sync Data based on a List of IMDBMovie objects
        /// </summary>
        /// <param name="Movies">The movies to base the object on</param>
        /// <returns>The Trakt Sync data to send</returns>
        public static TraktMovieSync CreateSyncData(List<IMDBMovie> Movies)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            List<TraktMovieSync.Movie> moviesList = (from m in Movies
                                                     select new TraktMovieSync.Movie
                                                     {
                                                         IMDBID = m.IMDBNumber,
                                                         Title = m.Title,
                                                         Year = m.Year.ToString()
                                                     }).ToList();

            TraktMovieSync syncData = new TraktMovieSync
            {
                UserName = username,
                Password = password,
                MovieList = moviesList
            };
            return syncData;
        }

        /// <summary>
        /// Creates Sync Data based on a single IMDBMovie object
        /// </summary>
        /// <param name="Movie">The movie to base the object on</param>
        /// <returns>The Trakt Sync data to send</returns>
        public static TraktMovieSync CreateSyncData(IMDBMovie Movie)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            List<TraktMovieSync.Movie> moviesList = new List<TraktMovieSync.Movie>();
            moviesList.Add(new TraktMovieSync.Movie
            {
                IMDBID = Movie.IMDBNumber,
                Title = Movie.Title,
                Year = Movie.Year.ToString()
            });

            TraktMovieSync syncData = new TraktMovieSync
            {
                UserName = username,
                Password = password,
                MovieList = moviesList
            };
            return syncData;
        }

        /// <summary>
        /// Creates Scrobble data based on a IMDBMovie object
        /// </summary>
        /// <param name="movie">The movie to base the object on</param>
        /// <returns>The Trakt scrobble data to send</returns>
        public static TraktMovieScrobble CreateScrobbleData(IMDBMovie movie)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            TraktMovieScrobble scrobbleData = new TraktMovieScrobble
            {
                Title = movie.Title,
                Year = movie.Year.ToString(),
                IMDBID = movie.IMDBNumber,
                PluginVersion = TraktSettings.Version,
                MediaCenter = "Mediaportal",
                MediaCenterVersion = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                MediaCenterBuildDate = String.Empty,
                UserName = username,
                Password = password
            };
            return scrobbleData;
        }

        #endregion

        #region Other Public Methods
        public static bool FindMovieID(string title, int year, string imdbid, ref IMDBMovie imdbMovie)
        {
            // get all movies
            ArrayList myvideos = new ArrayList();
            VideoDatabase.GetMovies(ref myvideos);

            // get all movies in local database
            List<IMDBMovie> movies = (from IMDBMovie m in myvideos select m).ToList();

            // try find a match
            IMDBMovie movie = movies.Find(m => BasicHandler.GetProperMovieImdbId(m.IMDBNumber) == imdbid || (string.Compare(m.Title, title, true) == 0 && m.Year == year));
            if (movie == null) return false;

            imdbMovie = movie;
            return true;
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Shows the Rate Movie Dialog after playback has ended
        /// </summary>
        /// <param name="movie">The movie being rated</param>
        private void ShowRateDialog(IMDBMovie movie)
        {
            if (!TraktSettings.ShowRateDialogOnWatched) return;     // not enabled            

            TraktLogger.Debug("Showing rate dialog for '{0}'", movie.Title);

            new Thread((o) =>
            {
                IMDBMovie movieToRate = o as IMDBMovie;
                if (movieToRate == null) return;

                TraktRateMovie rateObject = new TraktRateMovie
                {
                    IMDBID = movieToRate.IMDBNumber,
                    Title = movieToRate.Title,
                    Year = movieToRate.Year.ToString(),
                    UserName = TraktSettings.Username,
                    Password = TraktSettings.Password
                };

                // get the rating submitted to trakt
                int rating = int.Parse(GUIUtils.ShowRateDialog<TraktRateMovie>(rateObject));

                if (rating > 0)
                {
                    TraktLogger.Debug("Rating {0} as {1}/10", movieToRate.Title, rating.ToString());
                    // note: no user rating field to update locally
                }
            })
            {
                Name = "Rate",
                IsBackground = true
            }.Start(movie);
        }

        #endregion

    }
}

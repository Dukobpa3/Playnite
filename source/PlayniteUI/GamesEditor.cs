﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Playnite;
using Playnite.Database;
using Playnite.Models;
using PlayniteUI.Windows;
using System.Windows.Shell;
using System.Reflection;
using System.IO;
using System.Drawing;
using System.Windows.Media.Imaging;
using NLog;
using System.ComponentModel;
using LiteDB;
using PlayniteUI.ViewModels;
using System.Diagnostics;

namespace PlayniteUI
{
    public class GamesEditor : INotifyPropertyChanged
    {
        private static NLog.Logger logger = LogManager.GetCurrentClassLogger();
        private IResourceProvider resources = new ResourceProvider();
        private static GamesEditor instance;
        public static GamesEditor Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new GamesEditor();
                }

                return instance;
            }
        }       

        public IEnumerable<IGame> LastGames
        {
            get
            {
                return App.Database.GamesCollection?.Find(Query.All("LastActivity", Query.Descending))?.Where(a => a.LastActivity != null && a.IsInstalled)?.Take(10);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public bool? SetGameCategories(IGame game)
        {
            var model = new CategoryConfigViewModel(CategoryConfigWindowFactory.Instance, App.Database, game, true);
            return model.ShowDialog();
        }

        public bool? SetGamesCategories(IEnumerable<IGame> games)
        {
            var model = new CategoryConfigViewModel(CategoryConfigWindowFactory.Instance, App.Database, games, true);
            return model.ShowDialog();
        }

        public bool? EditGame(IGame game)
        {
            var model = new GameEditViewModel(
                            game,
                            App.Database,
                            GameEditWindowFactory.Instance,
                            new DialogsFactory(),
                            new ResourceProvider());
            return model.ShowDialog();
        }

        public bool? EditGames(IEnumerable<IGame> games)
        {
            var model = new GameEditViewModel(
                            games,
                            App.Database,
                            GameEditWindowFactory.Instance,
                            new DialogsFactory(),
                            new ResourceProvider());
            return model.ShowDialog();
        }

        public void PlayGame(IGame game)
        {
            // Set parent for message boxes in this method
            // because this method can be invoked from tray icon which otherwise bugs the dialog
            if (App.Database.GamesCollection.FindOne(a => a.ProviderId == game.ProviderId) == null)
            {
                PlayniteMessageBox.Show(Application.Current.MainWindow, $"Cannot start game. '{game.Name}' was not found in database.", "Game Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateJumpList();
                return;
            }

            try
            {
                if (game.IsInstalled)
                {
                    game.PlayGame(App.Database.EmulatorsCollection.FindAll().ToList());
                }
                else
                {
                    game.InstallGame();
                }

                App.Database.UpdateGameInDatabase(game);
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                logger.Error(exc, "Cannot start game: ");
                PlayniteMessageBox.Show(Application.Current.MainWindow, "Cannot start game: " + exc.Message, "Game Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                UpdateJumpList();                
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                logger.Error(exc, "Failed to set jump list data: ");
            }

            if (Settings.Instance.MinimizeAfterLaunch)
            {
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            }
        }

        public void ActivateAction(IGame game, GameTask action)
        {
            try
            {
                GameHandler.ActivateTask(action, game as Game, App.Database.EmulatorsCollection.FindAll().ToList());
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                PlayniteMessageBox.Show("Cannot start action: " + exc.Message, "Action Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenGameLocation(IGame game)
        {
            try
            {
                Process.Start(game.InstallDirectory);
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                PlayniteMessageBox.Show("Cannot open game location: " + exc.Message, "Game Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SetHideGame(IGame game, bool state)
        {
            game.Hidden = state;
            App.Database.UpdateGameInDatabase(game);
        }

        public void SetHideGames(IEnumerable<IGame> games, bool state)
        {
            foreach (var game in games)
            {
                SetHideGame(game, state);
            }
        }

        public void ToggleHideGame(IGame game)
        {
            game.Hidden = !game.Hidden;
            App.Database.UpdateGameInDatabase(game);
        }

        public void ToggleHideGames(IEnumerable<IGame> games)
        {
            foreach (var game in games)
            {
                ToggleHideGame(game);
            }
        }

        public void SetFavoriteGame(IGame game, bool state)
        {
            game.Favorite = state;
            App.Database.UpdateGameInDatabase(game);
        }

        public void SetFavoriteGames(IEnumerable<IGame> games, bool state)
        {
            foreach (var game in games)
            {
                SetFavoriteGame(game, state);
            }
        }

        public void ToggleFavoriteGame(IGame game)
        {
            game.Favorite = !game.Favorite;
            App.Database.UpdateGameInDatabase(game);
        }

        public void ToggleFavoriteGame(IEnumerable<IGame> games)
        {
            foreach (var game in games)
            {
                ToggleFavoriteGame(game);
            }
        }

        public void RemoveGame(IGame game)
        {
            if (PlayniteMessageBox.Show(
                resources.FindString("GameRemoveAskMessage"),
                resources.FindString("GameRemoveAskTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            App.Database.DeleteGame(game);
        }

        public void RemoveGames(IEnumerable<IGame> games)
        {
            if (PlayniteMessageBox.Show(
                string.Format(resources.FindString("GamesRemoveAskMessage"), games.Count()),
                resources.FindString("GameRemoveAskTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }           

            foreach (var game in games)
            {
                App.Database.DeleteGame(game);
            }
        }

        public void CreateShortcut(IGame game)
        {
            try
            {
                var path = Environment.ExpandEnvironmentVariables(Path.Combine("%userprofile%", "Desktop", FileSystem.GetSafeFilename(game.Name) + ".lnk"));
                string icon = string.Empty;

                if (!string.IsNullOrEmpty(game.Icon) && Path.GetExtension(game.Icon) == ".ico")
                {
                    FileSystem.CreateFolder(Path.Combine(Paths.DataCachePath, "icons"));
                    icon = Path.Combine(Paths.DataCachePath, "icons", game.Id + ".ico");
                    App.Database.SaveFile(game.Icon, icon);
                }
                else if (game.PlayTask.Type == GameTaskType.File)
                {
                    icon = Path.Combine(game.PlayTask.WorkingDir, game.PlayTask.Path);
                }

                Programs.CreateShortcut(Paths.ExecutablePath, "-command launch:" + game.Id, icon, path);
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                logger.Error(exc, "Failed to create shortcut: ");
                PlayniteMessageBox.Show("Failed to create shortcut: " + exc.Message, "Shortcut Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CreateShortcuts(IEnumerable<IGame> games)
        {
            foreach (var game in games)
            {
                CreateShortcut(game);
            }
        }

        public void InstallGame(IGame game)
        {
            try
            {
                game.InstallGame();
                App.Database.UpdateGameInDatabase(game);
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                logger.Error(exc, "Cannot install game: ");
                PlayniteMessageBox.Show("Cannot install game: " + exc.Message, "Game Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void UnInstallGame(IGame game)
        {
            try
            {
                game.UninstallGame();
                App.Database.UpdateGameInDatabase(game);
            }
            catch (Exception exc) when (!PlayniteEnvironment.ThrowAllErrors)
            {
                logger.Error(exc, "Cannot un-install game: ");
                PlayniteMessageBox.Show("Cannot un-install game: " + exc.Message, "Game Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void UpdateJumpList()
        {
            OnPropertyChanged("LastGames");
            var jumpList = new JumpList();
            foreach (var lastGame in LastGames)
            {
                JumpTask task = new JumpTask
                {
                    Title = lastGame.Name,
                    Arguments = "-command launch:" + lastGame.Id,
                    Description = string.Empty,
                    CustomCategory = "Recent",
                    ApplicationPath = Paths.ExecutablePath
                };

                if (lastGame.PlayTask != null && lastGame.PlayTask.Type == GameTaskType.File)
                {
                    if (string.IsNullOrEmpty(lastGame.PlayTask.WorkingDir))
                    {
                        task.IconResourcePath = lastGame.PlayTask.Path;
                    }
                    else
                    {
                        task.IconResourcePath = Path.Combine(lastGame.PlayTask.WorkingDir, lastGame.PlayTask.Path);
                    }
                }

                jumpList.JumpItems.Add(task);
                jumpList.ShowFrequentCategory = false;
                jumpList.ShowRecentCategory = false;
            }

            JumpList.SetJumpList(Application.Current, jumpList);
        }
    }
}

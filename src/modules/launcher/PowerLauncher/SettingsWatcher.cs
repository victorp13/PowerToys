﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Input;
using Microsoft.PowerToys.Settings.UI.Lib;
using Newtonsoft.Json;
using PowerLauncher.Helper;
using Wox.Core.Plugin;
using Wox.Infrastructure.Hotkey;
using Wox.Infrastructure.Logger;
using Wox.Infrastructure.UserSettings;
using Wox.Plugin;
using JsonException = System.Text.Json.JsonException;

namespace PowerLauncher
{
    // Watch for /Local/Microsoft/PowerToys/Launcher/Settings.json changes
    public class SettingsWatcher : BaseModel
    {
        private const int MaxRetries = 10;
        private static readonly object _watcherSyncObject = new object();
        private readonly FileSystemWatcher _watcher;
        private readonly Settings _settings;

        public SettingsWatcher(Settings settings)
        {
            _settings = settings;

            // Set up watcher
            _watcher = Microsoft.PowerToys.Settings.UI.Lib.Utilities.Helper.GetFileWatcher(PowerLauncherSettings.ModuleName, "settings.json", OverloadSettings);

            // Load initial settings file
            OverloadSettings();
        }

        public static void CreateSettingsIfNotExists()
        {
            if (!SettingsUtils.SettingsExists(PowerLauncherSettings.ModuleName))
            {
                Log.Info("|SettingsWatcher.OverloadSettings|PT Run settings.json was missing, creating a new one");
                var defaultSettings = new PowerLauncherSettings();
                defaultSettings.Save();
            }
        }

        public void OverloadSettings()
        {
            Monitor.Enter(_watcherSyncObject);
            var retry = true;
            var retryCount = 0;
            while (retry)
            {
                try
                {
                    retryCount++;
                    CreateSettingsIfNotExists();

                    var overloadSettings = SettingsUtils.GetSettings<PowerLauncherSettings>(PowerLauncherSettings.ModuleName);

                    var openPowerlauncher = ConvertHotkey(overloadSettings.Properties.OpenPowerLauncher);
                    if (_settings.Hotkey != openPowerlauncher)
                    {
                        _settings.Hotkey = openPowerlauncher;
                    }

                    var shell = PluginManager.AllPlugins.Find(pp => pp.Metadata.Name == "Shell");
                    if (shell != null)
                    {
                        var shellSettings = shell.Plugin as ISettingProvider;
                        shellSettings.UpdateSettings(overloadSettings);
                    }

                    if (_settings.MaxResultsToShow != overloadSettings.Properties.MaximumNumberOfResults)
                    {
                        _settings.MaxResultsToShow = overloadSettings.Properties.MaximumNumberOfResults;
                    }

                    if (_settings.IgnoreHotkeysOnFullscreen != overloadSettings.Properties.IgnoreHotkeysInFullscreen)
                    {
                        _settings.IgnoreHotkeysOnFullscreen = overloadSettings.Properties.IgnoreHotkeysInFullscreen;
                    }

                    var indexer = PluginManager.AllPlugins.Find(p => p.Metadata.Name.Equals("Windows Indexer Plugin", StringComparison.OrdinalIgnoreCase));
                    if (indexer != null)
                    {
                        var indexerSettings = indexer.Plugin as ISettingProvider;
                        indexerSettings.UpdateSettings(overloadSettings);
                    }

                    if (_settings.ClearInputOnLaunch != overloadSettings.Properties.ClearInputOnLaunch)
                    {
                        _settings.ClearInputOnLaunch = overloadSettings.Properties.ClearInputOnLaunch;
                    }

                    retry = false;
                }

                // the settings application can hold a lock on the settings.json file which will result in a IOException.
                // This should be changed to properly synch with the settings app instead of retrying.
                catch (IOException e)
                {
                    if (retryCount > MaxRetries)
                    {
                        retry = false;
                        Log.Exception($"|SettingsWatcher.OverloadSettings| Failed to Deserialize PowerToys settings, Retrying {e.Message}", e);
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }
                catch (JsonException e)
                {
                    if (retryCount > MaxRetries)
                    {
                        retry = false;
                        Log.Exception($"|SettingsWatcher.OverloadSettings| Failed to Deserialize PowerToys settings, Creating new settings as file could be corrupted {e.Message}", e);

                        // Settings.json could possibly be corrupted. To mitigate this we delete the
                        // current file and replace it with a correct json value.
                        SettingsUtils.DeleteSettings(PowerLauncherSettings.ModuleName);
                        CreateSettingsIfNotExists();
                        ErrorReporting.ShowMessageBox(Properties.Resources.deseralization_error_title, Properties.Resources.deseralization_error_message);
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }
            }

            Monitor.Exit(_watcherSyncObject);
        }

        private static string ConvertHotkey(HotkeySettings hotkey)
        {
            Key key = KeyInterop.KeyFromVirtualKey(hotkey.Code);
            HotkeyModel model = new HotkeyModel(hotkey.Alt, hotkey.Shift, hotkey.Win, hotkey.Ctrl, key);
            return model.ToString();
        }
    }
}

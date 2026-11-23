using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Wox.Infrastructure;
using Wox.Plugin.Logger;
using BrowserInfo = Wox.Plugin.Common.DefaultBrowserInfo;

namespace BrowserSearch.Browsers
{
    internal class Chromium : IBrowser
    {
        protected string[] UserDataDirCandidates { get; }
        protected Dictionary<string, ChromiumProfile> Profiles { get; } = [];
        private readonly string? _selectedProfileName;
        private readonly List<HistoryResult> _history = [];
        private readonly List<HistoryResult> _bookmarks = [];
        // Key is query, Value is a list of predictions for that query
        private readonly Dictionary<string, List<ChromiumPrediction>> _predictions = [];

        public Chromium(string[] userDataDirCandidates, string? profileName)
        {
            UserDataDirCandidates = userDataDirCandidates;
            _selectedProfileName = profileName;
        }

        void IBrowser.Init()
        {
            CreateProfiles();

            // Load history and bookmarks from all profiles
            if (_selectedProfileName is null)
            {
                foreach (ChromiumProfile profile in Profiles.Values)
                {
                    profile.Init(_history, _predictions, _bookmarks);
                }

                return;
            }

            // Load history and bookmarks from selected profile
            if (!Profiles.TryGetValue(_selectedProfileName.ToLower(), out ChromiumProfile? selectedProfile))
            {
                Log.Error($"Couldn't find profile '{_selectedProfileName}'", typeof(Chromium));
                MessageBox.Show($"No profile with the name '{_selectedProfileName}' was found.", "BrowserSearch");

                return;
            }
            selectedProfile.Init(_history, _predictions, _bookmarks);
        }

        protected virtual void CreateProfiles()
        {
            string? userDataDir = null;
            foreach (string candidate in UserDataDirCandidates)
            {
                if (Directory.Exists(candidate))
                {
                    userDataDir = candidate;
                    break;
                }
            }

            if (userDataDir is null)
                throw new NullReferenceException("Couldn\'t find UserData directory");

            Log.Info($"Found UserData directory: {userDataDir}", typeof(Chromium));
            using StreamReader jsonFileReader = new(
                new FileStream(Path.Join(userDataDir, "Local State"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            );

            JsonDocument localState = JsonDocument.Parse(jsonFileReader.ReadToEnd());
            jsonFileReader.Close();

            string[] nameProperties = ["gaia_given_name", "gaia_name", "name", "shortcut_name"];
            JsonElement infoCache = localState.RootElement.GetProperty("profile").GetProperty("info_cache");
            foreach (JsonProperty profileInfo in infoCache.EnumerateObject())
            {
                ChromiumProfile profile = new(Path.Join(userDataDir, profileInfo.Name));
                Profiles[profileInfo.Name.ToLower()] = profile;

                foreach (string nameProp in nameProperties)
                {
                    if (profileInfo.Value.TryGetProperty(nameProp, out JsonElement nameElem))
                    {
                        string? name = nameElem.GetString()?.ToLower();
                        if (!string.IsNullOrEmpty(name))
                        {
                            Profiles[name] = profile;
                        }
                    }
                }
            }
        }

        List<HistoryResult> IBrowser.GetHistory()
        {
            return _history;
        }

        List<HistoryResult> IBrowser.GetBookmarks()
        {
            return _bookmarks;
        }

        public int CalculateExtraScore(string query, string title, string url)
        {
            if (!_predictions.TryGetValue(query, out List<ChromiumPrediction>? predictions))
            {
                return 0;
            }

            foreach (ChromiumPrediction prediction in predictions)
            {
                if (prediction.url == url)
                {
                    return (int)prediction.hits;
                }
            }

            return 0;
        }
    }

    internal class ChromiumPrediction(string url, long hits)
    {
        public readonly string url = url;
        public readonly long hits = hits;
    }

    internal class ChromiumProfile
    {
        private readonly string _path;
        private bool _initialized, _bookmarksInitialized;
        private SqliteConnection? _historyDbConnection, _predictorDbConnection;

        public ChromiumProfile(string path)
        {
            _path = path;
        }

        public void Init(List<HistoryResult> history, Dictionary<string, List<ChromiumPrediction>> predictions, List<HistoryResult> bookmarks)
        {
            if (_initialized)
            {
                return;
            }
            Log.Info($"Initializing Chromium profile: '{_path}'", typeof(ChromiumProfile));

            // Bookmarks don't depend on the databases, so load them even if those are missing.
            // A profile can be registered under multiple names, so make sure to only load them once
            if (!_bookmarksInitialized)
            {
                PopulateBookmarks(bookmarks);
                _bookmarksInitialized = true;
            }

            try
            {
                CopyDatabases();
            }
            catch (FileNotFoundException)
            {
                Log.Warn($"Couldn't find database files in '{_path}'", typeof(ChromiumProfile));
                return;
            }
            ArgumentNullException.ThrowIfNull(_historyDbConnection);
            ArgumentNullException.ThrowIfNull(_predictorDbConnection);

            PopulatePredictions(predictions);
            PopulateHistory(history);

            _historyDbConnection.Close();
            _predictorDbConnection.Close();
            _historyDbConnection.Dispose();
            _predictorDbConnection.Dispose();
            _initialized = true;
        }

        private void CopyDatabases()
        {
            string _dirName = _path[(_path.LastIndexOf('\\') + 1)..];
            string historyCopy = Path.GetTempPath() + @"\BrowserSearch_History_" + _dirName;
            string predictorCopy = Path.GetTempPath() + @"\BrowserSearch_ActionPredictor_" + _dirName;

            // We need to copy the databases, otherwise we can't open them while the browser is running
            File.Copy(
                Path.Join(_path, @"\History"), historyCopy, true
            );
            File.Copy(
                Path.Join(_path, @"\Network Action Predictor"), predictorCopy, true
            );

            _historyDbConnection = new($"Data Source={historyCopy}");
            _predictorDbConnection = new($"Data Source={predictorCopy}");
        }

        private static SqliteDataReader ExecuteCmd(SqliteConnection connection, SqliteCommand cmd)
        {
            cmd.Connection = connection;
            connection.Open();

            return cmd.ExecuteReader();
        }

        public void PopulatePredictions(Dictionary<string, List<ChromiumPrediction>> predictions)
        {
            ArgumentNullException.ThrowIfNull(_predictorDbConnection);

            using SqliteCommand cmd = new("SELECT user_text, url, number_of_hits FROM network_action_predictor");
            using SqliteDataReader reader = ExecuteCmd(_predictorDbConnection, cmd);
            while (reader.Read())
            {
                string query = (string)reader[0];
                string url = (string)reader[1]; // Predicted URL for that query
                long hits = (long)reader[2]; // Amount of times the prediction was correct and the user selected it

                if (!predictions.TryGetValue(query, out List<ChromiumPrediction>? value))
                {
                    value = [];
                    predictions[query] = value;
                }

                value.Add(new ChromiumPrediction(url, hits));
            }
        }

        public void PopulateHistory(List<HistoryResult> history)
        {
            ArgumentNullException.ThrowIfNull(_historyDbConnection);

            using SqliteCommand historyReadCmd = new("SELECT url, title FROM urls ORDER BY visit_count DESC");
            using SqliteDataReader reader = ExecuteCmd(_historyDbConnection, historyReadCmd);
            while (reader.Read())
            {
                string url = (string)reader[0];
                string title = (string)reader[1];

                history.Add(CreateEntry(url, title, null));
            }
        }

        private void PopulateBookmarks(List<HistoryResult> bookmarks)
        {
            foreach (string fileName in new[] { "Bookmarks", "AccountBookmarks" })
            {
                string filePath = Path.Join(_path, fileName);
                if (!File.Exists(filePath))
                {
                    continue;
                }

                try
                {
                    using StreamReader jsonFileReader = new(
                        new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
                    );
                    using JsonDocument document = JsonDocument.Parse(jsonFileReader.ReadToEnd());

                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !document.RootElement.TryGetProperty("roots", out JsonElement roots)
                        || roots.ValueKind != JsonValueKind.Object)
                    {
                        Log.Warn($"Unexpected bookmarks format in '{filePath}'", typeof(ChromiumProfile));
                        continue;
                    }

                    foreach (JsonProperty root in roots.EnumerateObject())
                    {
                        if (root.Name != "trash" && root.Value.ValueKind == JsonValueKind.Object)
                        {
                            PopulateBookmarkNode(root.Value, null, bookmarks);
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
                {
                    Log.Warn($"Couldn't read bookmarks from '{filePath}': {e.Message}", typeof(ChromiumProfile));
                }
            }
        }

        private static void PopulateBookmarkNode(JsonElement node, string? folder, List<HistoryResult> bookmarks)
        {
            string? name = GetStringProperty(node, "name");

            if (GetStringProperty(node, "type") == "url")
            {
                string? url = GetStringProperty(node, "url");
                // Bookmarklets can't be opened from outside the browser
                if (!string.IsNullOrEmpty(url) && !url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                {
                    bookmarks.Add(CreateEntry(url, string.IsNullOrEmpty(name) ? url : name, folder));
                }

                return;
            }

            if (!node.TryGetProperty("children", out JsonElement children) || children.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            string? path = string.IsNullOrEmpty(name) ? folder : (folder is null ? name : $"{folder}/{name}");
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object)
                {
                    PopulateBookmarkNode(child, path, bookmarks);
                }
            }
        }

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static HistoryResult CreateEntry(string url, string title, string? folder)
        {
            return new()
            {
                URL = url,
                Title = title,
                Folder = folder,
                IcoPath = BrowserInfo.IconPath,
                Action = action =>
                {
                    // Open URL in default browser
                    if (!Helper.OpenInShell(url))
                    {
                        Log.Error($"Couldn't open '{url}'", typeof(ChromiumProfile));
                        return false;
                    }

                    return true;
                },
            };
        }
    }
}

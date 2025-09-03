using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NorthernRogue.CCCP.Editor.Settings;

// Detects file changes in the server directory to be able to notify and auto compile ///

namespace NorthernRogue.CCCP.Editor
{
    public class ServerDetectionProcess
    {
        // Callback delegate for change notifications
        public delegate void ServerChangesCallback(bool changesDetected);
        public event ServerChangesCallback OnServerChangesDetected;

        // Constants
        private const int MaxFilesPerScan = 100;
        
        // File extensions to monitor for script changes
        private static readonly string[] ScriptExtensions = { ".rs", ".cs", ".toml", ".sql", ".json", ".yaml", ".yml", ".md" };
        
        // Member variables
        private string serverDirectory;
        private bool detectServerChanges;
        private bool debugMode;
        
        // File tracking state - using both size and last write time for better change detection
        private Dictionary<string, (long size, DateTime lastWrite)> originalFileInfo = new Dictionary<string, (long, DateTime)>();
        private Dictionary<string, (long size, DateTime lastWrite)> currentFileInfo = new Dictionary<string, (long, DateTime)>();
        private bool serverChangesDetected = false;
        
        // Session flags
        private bool initialScanPerformed = false;
        private int pendingFileScanCount = 0;
        private bool hadFocus = true; // Track focus state

        public ServerDetectionProcess(bool debugMode)
        {
            this.debugMode = debugMode;
            LoadState();
            
            // Subscribe to focus change events
            EditorApplication.focusChanged += OnEditorFocusChanged;
        }

        public void Configure(string serverDirectory, bool detectServerChanges)
        {
            bool directoryChanged = this.serverDirectory != serverDirectory;
            this.serverDirectory = serverDirectory;
            this.detectServerChanges = detectServerChanges;
            
            // Reset tracking when directory changes
            if (directoryChanged)
            {
                ResetTracking();
            }
            
            // Save configuration
            CCCPSettingsAdapter.SetServerDirectory(serverDirectory);
            CCCPSettingsAdapter.SetDetectServerChanges(detectServerChanges);
        }

        public bool IsDetectingChanges()
        {
            return detectServerChanges;
        }

        public bool AreChangesDetected()
        {
            return serverChangesDetected;
        }

        public void SetDetectChanges(bool detect)
        {
            if (this.detectServerChanges != detect)
            {
                this.detectServerChanges = detect;
                CCCPSettingsAdapter.SetDetectServerChanges(detect);

                if (detect && !initialScanPerformed)
                {
                    // Run initial scan when enabling detection
                    DetectServerChanges();
                }
                else if (!detect)
                {
                    // Clear detected state when disabling
                    SetChangesDetected(false);
                }
            }
        }

        public void ResetTracking()
        {
            if (debugMode) Debug.Log("[ServerDetectionProcess] Resetting file tracking state");
            originalFileInfo.Clear();
            currentFileInfo.Clear();
            serverChangesDetected = false;
            initialScanPerformed = false;
            SaveTrackingState();
            
            // Run initial scan if detection is enabled
            if (detectServerChanges)
            {
                DetectServerChanges();
            }
        }

        public void Dispose()
        {
            // Unsubscribe from events
            EditorApplication.focusChanged -= OnEditorFocusChanged;
        }

        private void OnEditorFocusChanged(bool hasFocus)
        {
            // If Unity just gained focus, check for changes immediately
            if (hasFocus && !hadFocus && detectServerChanges)
            {
                if (debugMode) Debug.Log("[ServerDetectionProcess] Unity regained focus, checking for server changes");
                DetectServerChanges();
            }
            hadFocus = hasFocus;
        }

        public void CheckForChanges()
        {
            if (detectServerChanges)
            {
                DetectServerChanges();
            }
        }

        public void DetectServerChanges()
        {
            if (!detectServerChanges || string.IsNullOrEmpty(serverDirectory))
                return;

            try
            {
                string srcDirectory = Path.Combine(serverDirectory, "src");
                string cargoTomlPath = Path.Combine(serverDirectory, "Cargo.toml");
                
                // Initialize dictionaries if empty
                var newFileInfo = new Dictionary<string, (long size, DateTime lastWrite)>();
                
                // Check for Cargo.toml existence and add it to tracking
                if (File.Exists(cargoTomlPath))
                {
                    try 
                    {
                        var fileInfo = new FileInfo(cargoTomlPath);
                        newFileInfo[cargoTomlPath] = (fileInfo.Length, fileInfo.LastWriteTime);
                    }
                    catch (IOException ex)
                    {
                        if (debugMode) Debug.LogWarning($"[ServerDetectionProcess] Could not get info for Cargo.toml: {ex.Message}");
                    }
                }
                
                // No src directory means we're only tracking Cargo.toml
                if (!Directory.Exists(srcDirectory))
                {
                    if (originalFileInfo.Count > 0 || currentFileInfo.Count > 0 || newFileInfo.Count > 0) 
                    {
                        // If src dir disappeared but we were tracking files or have Cargo.toml, that's a change
                        originalFileInfo.Clear();
                        currentFileInfo.Clear();
                        if (newFileInfo.Count > 0) 
                        {
                            originalFileInfo = new Dictionary<string, (long, DateTime)>(newFileInfo);
                            currentFileInfo = new Dictionary<string, (long, DateTime)>(newFileInfo);
                        }
                        
                        // Only mark as changed if we lost everything
                        SetChangesDetected(newFileInfo.Count == 0);
                        initialScanPerformed = true;
                        SaveTrackingState();
                    }
                    return;
                }

                // Scan files in a way that's safe for the main thread, but limit number of files checked per frame
                // Get all script files (filtered by extensions) from src directory and subdirectories
                var scriptFiles = new List<string>();
                foreach (string extension in ScriptExtensions)
                {
                    try
                    {
                        string[] filesWithExtension = Directory.GetFiles(srcDirectory, "*" + extension, SearchOption.AllDirectories);
                        scriptFiles.AddRange(filesWithExtension);
                    }
                    catch (Exception ex)
                    {
                        if (debugMode) Debug.LogWarning($"[ServerDetectionProcess] Error scanning for {extension} files: {ex.Message}");
                    }
                }
                
                int filesToProcess = Math.Min(scriptFiles.Count, MaxFilesPerScan);
                
                // Update pending file count for multi-frame processing
                pendingFileScanCount = scriptFiles.Count - filesToProcess;
                
                // Process a subset of script files
                for (int i = 0; i < filesToProcess; i++)
                {
                    string file = scriptFiles[i];
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        newFileInfo[file] = (fileInfo.Length, fileInfo.LastWriteTime);
                    }
                    catch (IOException ex)
                    {
                        if (debugMode) Debug.LogWarning($"[ServerDetectionProcess] Could not get info for file {file}: {ex.Message}");
                    }
                }
                
                // If we've only processed a partial list, we'll process more next time
                if (filesToProcess < scriptFiles.Count)
                {
                    if (debugMode) Debug.Log($"[ServerDetectionProcess] Partially scanned server directory: {filesToProcess}/{scriptFiles.Count} files");
                    // Process the changes we do have to see if we need to update
                }
                        
                // Check for changed/new files
                foreach (var kvp in newFileInfo)
                {
                    if (!currentFileInfo.TryGetValue(kvp.Key, out var currentInfo) || 
                        currentInfo.size != kvp.Value.size || 
                        currentInfo.lastWrite != kvp.Value.lastWrite)
                    {
                        currentFileInfo[kvp.Key] = kvp.Value;
                    }
                }
                
                // Skip checking for deleted files if we're only doing a partial scan
                if (filesToProcess >= scriptFiles.Count)
                {
                    // Check for deleted files only on complete scans
                    var filesToRemove = currentFileInfo.Keys.Where(k => 
                        k != cargoTomlPath && !newFileInfo.ContainsKey(k)).ToList();
                        
                    foreach (var fileToRemove in filesToRemove)
                    {
                        currentFileInfo.Remove(fileToRemove);
                    }
                }

                // Check initial scan 
                if (!initialScanPerformed)
                {
                    if (originalFileInfo.Count == 0)
                    {
                        // First scan, so set original = current
                        originalFileInfo = new Dictionary<string, (long, DateTime)>(currentFileInfo);
                        initialScanPerformed = true;
                        SetChangesDetected(false);
                        SaveTrackingState();
                        return;
                    }
                }

                // Compare current state with original state
                bool differenceFromOriginal = false;
                
                // First check if counts are different
                if (currentFileInfo.Count != originalFileInfo.Count)
                {
                    differenceFromOriginal = true;
                }
                else
                {
                    // Only check a sample of files to avoid blocking for too long
                    int checkedFiles = 0;
                    foreach (var kvp in currentFileInfo)
                    {
                        if (checkedFiles++ > MaxFilesPerScan) break; // Limit number of checks
                        
                        if (!originalFileInfo.TryGetValue(kvp.Key, out var originalInfo) || 
                            originalInfo.size != kvp.Value.size || 
                            originalInfo.lastWrite != kvp.Value.lastWrite)
                        {
                            differenceFromOriginal = true;
                            break;
                        }
                    }
                }

                // Update serverChangesDetected state
                if (serverChangesDetected != differenceFromOriginal)
                {
                    SetChangesDetected(differenceFromOriginal);
                }
                
                // Mark initial scan as performed
                initialScanPerformed = true;
                
                // Save state
                SaveTrackingState();
            }
            catch (Exception ex)
            {
                if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error checking server changes: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // Reset tracking after publishing
        public void ResetTrackingAfterPublish()
        {
            if (detectServerChanges)
            {
                // Keep current file info as the new baseline
                if (currentFileInfo.Count > 0)
                {
                    originalFileInfo = new Dictionary<string, (long, DateTime)>(currentFileInfo);
                    SetChangesDetected(false);
                    SaveTrackingState();
                    
                    if (debugMode) Debug.Log("[ServerDetectionProcess] Tracking reset after publish");
                }
                else
                {
                    // If we don't have current info, reset completely
                    ResetTracking();
                }
            }
        }
        
        private void SetChangesDetected(bool detected)
        {
            bool changed = serverChangesDetected != detected;
            serverChangesDetected = detected;
            
            // Save state
            CCCPSettingsAdapter.SetServerChangesDetected(serverChangesDetected);

            // Notify subscribers if state changed
            if (changed && OnServerChangesDetected != null)
            {
                OnServerChangesDetected(serverChangesDetected);
            }
        }
        
        // Load state from Settings
        private void LoadState()
        {
            serverDirectory = CCCPSettingsAdapter.GetServerDirectory();
            detectServerChanges = CCCPSettingsAdapter.GetDetectServerChanges();
            serverChangesDetected = CCCPSettingsAdapter.GetServerChangesDetected();

            // Also load file tracking data from Settings if exists
            string originalInfoJson = CCCPSettingsAdapter.GetOriginalFileInfo();
            string currentInfoJson = CCCPSettingsAdapter.GetCurrentFileInfo();

            if (!string.IsNullOrEmpty(originalInfoJson))
            {
                try
                {
                    // Deserialize file tracking data
                    originalFileInfo = JsonUtility.FromJson<SerializableFileInfoDictionary>(originalInfoJson).ToDictionary();
                    initialScanPerformed = originalFileInfo.Count > 0;
                }
                catch (Exception ex)
                {
                    if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error loading original file info: {ex.Message}");
                    originalFileInfo.Clear();
                }
            }
            
            if (!string.IsNullOrEmpty(currentInfoJson))
            {
                try
                {
                    // Deserialize file tracking data
                    currentFileInfo = JsonUtility.FromJson<SerializableFileInfoDictionary>(currentInfoJson).ToDictionary();
                }
                catch (Exception ex)
                {
                    if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error loading current file info: {ex.Message}");
                    currentFileInfo.Clear();
                }
            }
        }

        // Save tracking state to Settings
        private void SaveTrackingState()
        {
            try
            {
                // Serialize dictionaries to JSON
                string originalInfoJson = JsonUtility.ToJson(new SerializableFileInfoDictionary(originalFileInfo));
                string currentInfoJson = JsonUtility.ToJson(new SerializableFileInfoDictionary(currentFileInfo));

                // Save to Settings
                CCCPSettingsAdapter.SetOriginalFileInfo(originalInfoJson);
                CCCPSettingsAdapter.SetCurrentFileInfo(currentInfoJson);
                CCCPSettingsAdapter.SetServerChangesDetected(serverChangesDetected);
            }
            catch (Exception ex)
            {
                if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error saving tracking state: {ex.Message}");
            }
        }
        
        // Helper class for serializing Dictionary<string, (long, DateTime)>
        [Serializable]
        private class SerializableFileInfoDictionary
        {
            [Serializable]
            public struct KeyValuePair
            {
                public string key;
                public long size;
                public string lastWrite; // DateTime as string
            }
            
            public List<KeyValuePair> entries = new List<KeyValuePair>();
            
            public SerializableFileInfoDictionary() { }
            
            public SerializableFileInfoDictionary(Dictionary<string, (long size, DateTime lastWrite)> dictionary)
            {
                foreach (var kvp in dictionary)
                {
                    entries.Add(new KeyValuePair 
                    { 
                        key = kvp.Key, 
                        size = kvp.Value.size,
                        lastWrite = kvp.Value.lastWrite.ToBinary().ToString()
                    });
                }
            }
            
            public Dictionary<string, (long size, DateTime lastWrite)> ToDictionary()
            {
                Dictionary<string, (long, DateTime)> result = new Dictionary<string, (long, DateTime)>();
                foreach (var entry in entries)
                {
                    try
                    {
                        long binaryTime = Convert.ToInt64(entry.lastWrite);
                        DateTime dateTime = DateTime.FromBinary(binaryTime);
                        result[entry.key] = (entry.size, dateTime);
                    }
                    catch (Exception)
                    {
                        // Skip corrupted entries
                        continue;
                    }
                }
                return result;
            }
        }
    }
} 
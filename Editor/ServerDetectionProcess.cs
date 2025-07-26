using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Detects file changes in the server directory to be able to notify and auto compile ///

namespace NorthernRogue.CCCP.Editor
{
    public class ServerDetectionProcess
    {
        // Callback delegate for change notifications
        public delegate void ServerChangesCallback(bool changesDetected);
        public event ServerChangesCallback OnServerChangesDetected;

        // Constants
        private const string PrefsKeyPrefix = "CCCP_";
        private const int MaxFilesPerScan = 100;
        
        // File extensions to monitor for script changes
        private static readonly string[] ScriptExtensions = { ".rs", ".cs", ".toml", ".sql", ".json", ".yaml", ".yml", ".md" };
        
        // Member variables
        private string serverDirectory;
        private bool detectServerChanges;
        private bool debugMode;
        private double lastChangeCheckTime;
        private const double changeCheckInterval = 3.0;
        
        // File tracking state
        private Dictionary<string, long> originalFileSizes = new Dictionary<string, long>();
        private Dictionary<string, long> currentFileSizes = new Dictionary<string, long>();
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
            EditorPrefs.SetString(PrefsKeyPrefix + "ServerDirectory", serverDirectory);
            EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", detectServerChanges);
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
                EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", detect);
                
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
            originalFileSizes.Clear();
            currentFileSizes.Clear();
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
            if (detectServerChanges && !string.IsNullOrEmpty(serverDirectory))
            {
                double currentTime = EditorApplication.timeSinceStartup;
                if (currentTime - lastChangeCheckTime > changeCheckInterval)
                {
                    lastChangeCheckTime = currentTime;
                    DetectServerChanges();
                }
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
                var newSizes = new Dictionary<string, long>();
                
                // Check for Cargo.toml existence and add it to tracking
                if (File.Exists(cargoTomlPath))
                {
                    try 
                    {
                        newSizes[cargoTomlPath] = new FileInfo(cargoTomlPath).Length;
                    }
                    catch (IOException ex)
                    {
                        if (debugMode) Debug.LogWarning($"[ServerDetectionProcess] Could not get size for Cargo.toml: {ex.Message}");
                    }
                }
                
                // No src directory means we're only tracking Cargo.toml
                if (!Directory.Exists(srcDirectory))
                {
                    if (originalFileSizes.Count > 0 || currentFileSizes.Count > 0 || newSizes.Count > 0) 
                    {
                        // If src dir disappeared but we were tracking files or have Cargo.toml, that's a change
                        originalFileSizes.Clear();
                        currentFileSizes.Clear();
                        if (newSizes.Count > 0) 
                        {
                            originalFileSizes = new Dictionary<string, long>(newSizes);
                            currentFileSizes = new Dictionary<string, long>(newSizes);
                        }
                        
                        // Only mark as changed if we lost everything
                        SetChangesDetected(newSizes.Count == 0);
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
                        newSizes[file] = new FileInfo(file).Length;
                    }
                    catch (IOException ex)
                    {
                        if (debugMode) Debug.LogWarning($"[ServerDetectionProcess] Could not get size for file {file}: {ex.Message}");
                    }
                }
                
                // If we've only processed a partial list, we'll process more next time
                if (filesToProcess < scriptFiles.Count)
                {
                    if (debugMode) Debug.Log($"[ServerDetectionProcess] Partially scanned server directory: {filesToProcess}/{scriptFiles.Count} files");
                    // Process the changes we do have to see if we need to update
                }
                        
                // Check for changed/new files
                foreach (var kvp in newSizes)
                {
                    if (!currentFileSizes.TryGetValue(kvp.Key, out long currentSize) || currentSize != kvp.Value)
                    {
                        currentFileSizes[kvp.Key] = kvp.Value;
                    }
                }
                
                // Skip checking for deleted files if we're only doing a partial scan
                if (filesToProcess >= scriptFiles.Count)
                {
                    // Check for deleted files only on complete scans
                    var filesToRemove = currentFileSizes.Keys.Where(k => 
                        k != cargoTomlPath && !newSizes.ContainsKey(k)).ToList();
                        
                    foreach (var fileToRemove in filesToRemove)
                    {
                        currentFileSizes.Remove(fileToRemove);
                    }
                }

                // Check initial scan 
                if (!initialScanPerformed)
                {
                    if (originalFileSizes.Count == 0)
                    {
                        // First scan, so set original = current
                        originalFileSizes = new Dictionary<string, long>(currentFileSizes);
                        initialScanPerformed = true;
                        SetChangesDetected(false);
                        SaveTrackingState();
                        return;
                    }
                }

                // Compare current state with original state
                bool differenceFromOriginal = false;
                
                // First check if counts are different
                if (currentFileSizes.Count != originalFileSizes.Count)
                {
                    differenceFromOriginal = true;
                }
                else
                {
                    // Only check a sample of files to avoid blocking for too long
                    int checkedFiles = 0;
                    foreach (var kvp in currentFileSizes)
                    {
                        if (checkedFiles++ > MaxFilesPerScan) break; // Limit number of checks
                        
                        if (!originalFileSizes.TryGetValue(kvp.Key, out long originalSize) || originalSize != kvp.Value)
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
                // Keep current file sizes as the new baseline
                if (currentFileSizes.Count > 0)
                {
                    originalFileSizes = new Dictionary<string, long>(currentFileSizes);
                    SetChangesDetected(false);
                    SaveTrackingState();
                    
                    if (debugMode) Debug.Log("[ServerDetectionProcess] Tracking reset after publish");
                }
                else
                {
                    // If we don't have current sizes, reset completely
                    ResetTracking();
                }
            }
        }
        
        private void SetChangesDetected(bool detected)
        {
            bool changed = serverChangesDetected != detected;
            serverChangesDetected = detected;
            
            // Save state
            EditorPrefs.SetBool(PrefsKeyPrefix + "ServerChangesDetected", serverChangesDetected);
            
            // Notify subscribers if state changed
            if (changed && OnServerChangesDetected != null)
            {
                OnServerChangesDetected(serverChangesDetected);
            }
        }
        
        // Load state from EditorPrefs
        private void LoadState()
        {
            serverDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ServerDirectory", "");
            detectServerChanges = EditorPrefs.GetBool(PrefsKeyPrefix + "DetectServerChanges", true);
            serverChangesDetected = EditorPrefs.GetBool(PrefsKeyPrefix + "ServerChangesDetected", false);
            
            // Also load file tracking data from SessionState if exists
            string originalSizesJson = EditorPrefs.GetString(PrefsKeyPrefix + "OriginalFileSizes", "");
            string currentSizesJson = EditorPrefs.GetString(PrefsKeyPrefix + "CurrentFileSizes", "");
            
            if (!string.IsNullOrEmpty(originalSizesJson))
            {
                try
                {
                    // Deserialize file tracking data
                    originalFileSizes = JsonUtility.FromJson<SerializableDictionary>(originalSizesJson).ToDictionary();
                    initialScanPerformed = originalFileSizes.Count > 0;
                }
                catch (Exception ex)
                {
                    if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error loading original file sizes: {ex.Message}");
                    originalFileSizes.Clear();
                }
            }
            
            if (!string.IsNullOrEmpty(currentSizesJson))
            {
                try
                {
                    // Deserialize file tracking data
                    currentFileSizes = JsonUtility.FromJson<SerializableDictionary>(currentSizesJson).ToDictionary();
                }
                catch (Exception ex)
                {
                    if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error loading current file sizes: {ex.Message}");
                    currentFileSizes.Clear();
                }
            }
        }
        
        // Save tracking state to EditorPrefs
        private void SaveTrackingState()
        {
            try
            {
                // Serialize dictionaries to JSON
                string originalSizesJson = JsonUtility.ToJson(new SerializableDictionary(originalFileSizes));
                string currentSizesJson = JsonUtility.ToJson(new SerializableDictionary(currentFileSizes));
                
                // Save to EditorPrefs
                EditorPrefs.SetString(PrefsKeyPrefix + "OriginalFileSizes", originalSizesJson);
                EditorPrefs.SetString(PrefsKeyPrefix + "CurrentFileSizes", currentSizesJson);
                EditorPrefs.SetBool(PrefsKeyPrefix + "ServerChangesDetected", serverChangesDetected);
            }
            catch (Exception ex)
            {
                if (debugMode) Debug.LogError($"[ServerDetectionProcess] Error saving tracking state: {ex.Message}");
            }
        }
        
        // Helper class for serializing Dictionary<string, long>
        [Serializable]
        private class SerializableDictionary
        {
            [Serializable]
            public struct KeyValuePair
            {
                public string key;
                public long value;
            }
            
            public List<KeyValuePair> entries = new List<KeyValuePair>();
            
            public SerializableDictionary() { }
            
            public SerializableDictionary(Dictionary<string, long> dictionary)
            {
                foreach (var kvp in dictionary)
                {
                    entries.Add(new KeyValuePair { key = kvp.Key, value = kvp.Value });
                }
            }
            
            public Dictionary<string, long> ToDictionary()
            {
                Dictionary<string, long> result = new Dictionary<string, long>();
                foreach (var entry in entries)
                {
                    result[entry.key] = entry.value;
                }
                return result;
            }
        }
    }
} 
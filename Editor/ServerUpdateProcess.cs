using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

// Checks for new updates for this asset and related packages ///

namespace NorthernRogue.CCCP.Editor {

[InitializeOnLoad]
public class ServerUpdateProcess
{
    public static bool debugMode = false; // Set in ServerWindow

    private const string PrefsKeyPrefix = "CCCP_";

    /////////// Version Detection ///////////
    private static bool? isAssetStoreVersion = null;
    private static bool? isGithubVersion = null;
    private static string distributionType = "";

    /////////// CCCP Github Update Checker ///////////
    private const string owner = "strosz";
    private const string packageName = "com.northernrogue.cccp.spacetimedbserver";
    private const string branch = "main";
    private static ListRequest listRequest;
    private static string currentVersion = "";
    private static string latestCommitSha = "";
    private static AddRequest addRequest;
    private static ServerWindow window;

    /////////// CCCP Asset Store Version Update Checker //////////
    private static string cccpAssetStoreLatestVersion = "";
    private static SearchRequest searchRequest;

    /////////// SpacetimeDB Server Update Checker ///////////
    private static string spacetimeDBLatestVersion = "";

    /////////// SpacetimeDB SDK Update Checker ///////////
    private static string spacetimeSDKLatestVersion = "";

    static ServerUpdateProcess()
    {
        // Initialize distribution type on first load
        InitializeDistributionType();
        
        EditorApplication.delayCall += () => {
            if (debugMode) 
            {
                Debug.Log("ServerUpdateProcess: Starting update checks...");
                Debug.Log($"Distribution Type: {distributionType}");
            }
            CheckForGithubUpdate();
            CheckForAssetStoreUpdate();
            CheckForSpacetimeDBUpdate();
            CheckForSpacetimeSDKUpdate();
            //Debug.Log(GetVersionDebugInfo()); // Keep for debugging
        };
    }

    private static void InitializeDistributionType()
    {
        // First try to load from EditorPrefs
        distributionType = EditorPrefs.GetString(PrefsKeyPrefix + "DistributionType", "");
        
        // Only calculate if empty (first time or cache was cleared)
        if (string.IsNullOrEmpty(distributionType))
        {
            distributionType = GetDistributionType();
            EditorPrefs.SetString(PrefsKeyPrefix + "DistributionType", distributionType);
            
            if (debugMode) Debug.Log($"Distribution type calculated and cached: {distributionType}");
        }
        else if (debugMode)
        {
            Debug.Log($"Distribution type loaded from cache: {distributionType}");
        }
    }

    #region Github Update
    public static bool CheckForGithubUpdate()
    {
        // Don't check for GitHub updates if this is an Asset Store version
        if (!ShouldCheckGithubUpdates())
        {
            if (debugMode) Debug.Log("GitHub update check skipped - Asset Store version detected");
            return false;
        }

        string storedSha = EditorPrefs.GetString("CCCP_LastCommitSha", "");
        if (!string.IsNullOrEmpty(storedSha))
        {
            latestCommitSha = storedSha;
        }
        if (debugMode) Debug.Log($"Checked for GitHub update - Stored SHA: {latestCommitSha}");
        
        FetchLatestCommitAsync();
        return EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", false);
    }

    private static void FetchLatestCommitAsync()
    {
        string url = $"https://api.github.com/repos/{owner}/{packageName}/commits/{branch}";

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("User-Agent", "UnityGitHubChecker");  // GitHub requires this
        
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        operation.completed += _ => {

        if (request.result != UnityWebRequest.Result.Success)
        {
            if (debugMode) Debug.LogError("GitHub API request failed: " + request.error);
        }
        else
        {
            ProcessCommitResponse(request.downloadHandler.text);
            if (debugMode) Debug.Log("GitHub API request succeeded: " + request.downloadHandler.text);
        }
            request.Dispose();
        };
    }

    private static void ProcessCommitResponse(string json)
    {
        try
        {
            CommitData commitData = JsonUtility.FromJson<CommitData>(json);
            string fetchedSha = commitData.sha;

            if (string.IsNullOrEmpty(latestCommitSha))
            {
                latestCommitSha = fetchedSha;
                EditorPrefs.SetString("CCCP_LastCommitSha", latestCommitSha);
                if (debugMode) Debug.Log($"First fetch: latest commit SHA is {latestShaShort(latestCommitSha)}");
                EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", false);
            }
            else if (fetchedSha != latestCommitSha)
            {
                if (debugMode) Debug.Log($"New commit detected! Old: {latestShaShort(latestCommitSha)}  New: {latestShaShort(fetchedSha)}");
                latestCommitSha = fetchedSha;
                EditorPrefs.SetString("CCCP_LastCommitSha", latestCommitSha);
                EditorPrefs.SetBool(PrefsKeyPrefix + "GithubUpdateAvailable", true);
                DisplayGithubUpdateAvailable();
            }
            else
            {
                if (debugMode) Debug.Log("No new commit found.");
                EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", false);
            }
        }
        catch (Exception e)
        {
            if (debugMode) Debug.LogError("Failed to parse GitHub response: " + e.Message);
        }
    }    // Helper to shorten SHA
    private static string latestShaShort(string sha)
    {
        return sha.Length >= 7 ? sha.Substring(0, 7) : sha;
    }
    
    // Public static method for other scripts to check update status
    public static bool IsGithubUpdateAvailable()
    {
        // Don't show GitHub updates for Asset Store version
        if (!ShouldCheckGithubUpdates())
            return false;
            
        // EditorPrefs can also be called directly
        return EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", false);
    }

    // Display the update available message once in the ServerWindow
    private static void DisplayGithubUpdateAvailable()
    {
        if (window != null)
            window.LogMessage("Cosmos Cove Control Panel Update Available - Please update to the latest version in the Package Manager.", 1);
        // The EditorPref is set to false in ProcessCommitResponse() when no new commit is found
        // So this LogMessage will only be displayed once
    }

    public static void UpdateGithubPackage()
    {
        // Use the full GitHub URL format for package updates
        string gitUrl = $"https://github.com/{owner}/{packageName}.git";
        
        if (debugMode) Debug.Log($"Updating package from: {gitUrl}");
        
        addRequest = Client.Add(gitUrl);
        
        // Find the ServerWindow instance if it exists
        window = EditorWindow.GetWindow<ServerWindow>(false, "Server", false);
        
        EditorApplication.update += GithubUpdateProgress;
    }

    private static void GithubUpdateProgress()
    {
        if (addRequest.IsCompleted)
        {
            if (addRequest.Status == StatusCode.Success)
            {
                Debug.Log("Package updated successfully: " + addRequest.Result.packageId);
                if (window != null && debugMode)
                    window.LogMessage("Package updated successfully: " + addRequest.Result.packageId, 1);
                EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", false);
            }
            else if (addRequest.Status == StatusCode.Failure)
            {
                Debug.LogError("Package update failed: " + addRequest.Error.message);
                if (window != null && debugMode)
                    window.LogMessage("Package update failed: " + addRequest.Error.message, -1);
                EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", false); // To not occupy UI space
            }
                
            EditorApplication.update -= GithubUpdateProgress;
        }
    }

    // Data model for Github API response
    [Serializable]
    private class CommitData
    {
        public string sha;
    }
    #endregion

    #region Asset Store Update
    public static bool CheckForAssetStoreUpdate()
    {
        // Only check for Asset Store updates if this is an Asset Store version
        if (!IsAssetStoreVersion())
        {
            if (debugMode) Debug.Log("Asset Store update check skipped - not an Asset Store version");
            return false;
        }

        if (debugMode) Debug.Log("Checking for Asset Store package updates...");
        
        SearchAssetStorePackageAsync();
        return EditorPrefs.GetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
    }

    private static void SearchAssetStorePackageAsync()
    {
        // Search for the package in available registries
        searchRequest = Client.Search(packageName);
        
        EditorApplication.update += AssetStoreSearchProgress;
    }

    private static void AssetStoreSearchProgress()
    {
        if (searchRequest.IsCompleted)
        {
            if (searchRequest.Status == StatusCode.Success)
            {
                ProcessAssetStoreSearchResponse();
            }
            else if (searchRequest.Status == StatusCode.Failure)
            {
                if (debugMode) Debug.Log($"Asset Store search failed: {searchRequest.Error.message}");
                EditorPrefs.SetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
            }
            
            EditorApplication.update -= AssetStoreSearchProgress;
        }
    }

    private static void ProcessAssetStoreSearchResponse()
    {
        try
        {
            var packages = searchRequest.Result;
            string currentVersion = GetCurrentPackageVersion();
            
            if (packages != null && packages.Length > 0)
            {
                // Look for our package in the search results
                foreach (var package in packages)
                {
                    if (package.name == packageName)
                    {
                        string latestVersion = package.version;
                        cccpAssetStoreLatestVersion = latestVersion;
                        EditorPrefs.SetString(PrefsKeyPrefix + "AssetStoreLatestVersion", cccpAssetStoreLatestVersion);
                        
                        if (debugMode) Debug.Log($"Found package in registry - Current: {currentVersion}, Latest: {latestVersion}");
                        
                        // Compare versions
                        if (!string.IsNullOrEmpty(currentVersion) && CompareVersions(currentVersion, latestVersion) < 0)
                        {
                            if (debugMode) Debug.Log($"Asset Store update available! Current: {currentVersion}, Latest: {latestVersion}");
                            EditorPrefs.SetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", true);
                            DisplayAssetStoreUpdateAvailable();
                        }
                        else
                        {
                            if (debugMode) Debug.Log("No Asset Store update available - current version is up to date");
                            EditorPrefs.SetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
                        }
                        return;
                    }
                }
                
                if (debugMode) Debug.Log("Package not found in available registries");
                EditorPrefs.SetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
            }
            else
            {
                if (debugMode) Debug.Log("No packages found in search results");
                EditorPrefs.SetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
            }
        }
        catch (Exception e)
        {
            if (debugMode) Debug.LogError("Failed to process Asset Store search response: " + e.Message);
            EditorPrefs.SetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
        }
    }

    /// <summary>
    /// Compares two version strings. Returns -1 if version1 < version2, 0 if equal, 1 if version1 > version2
    /// </summary>
    private static int CompareVersions(string version1, string version2)
    {
        try
        {
            // Parse versions like "1.2.3" into comparable format
            string[] v1Parts = version1.Split('.');
            string[] v2Parts = version2.Split('.');
            
            int maxLength = Mathf.Max(v1Parts.Length, v2Parts.Length);
            
            for (int i = 0; i < maxLength; i++)
            {
                int v1Part = i < v1Parts.Length ? int.Parse(v1Parts[i]) : 0;
                int v2Part = i < v2Parts.Length ? int.Parse(v2Parts[i]) : 0;
                
                if (v1Part < v2Part) return -1;
                if (v1Part > v2Part) return 1;
            }
            
            return 0; // Versions are equal
        }
        catch (Exception)
        {
            // If parsing fails, fall back to string comparison
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Public static method for other scripts to check Asset Store update status
    public static bool IsAssetStoreUpdateAvailable()
    {
        // Only show Asset Store updates for Asset Store version
        if (!IsAssetStoreVersion())
            return false;
            
        return EditorPrefs.GetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", false);
    }

    // Display the Asset Store update available message once in the ServerWindow
    private static void DisplayAssetStoreUpdateAvailable()
    {
        if (window != null)
            window.LogMessage("Cosmos Cove Control Panel Asset Store Update Available - Please update in the Package Manager.", 1);
    }

    // Public method to get the latest Asset Store version
    public static string AssetStoreLatestVersion()
    {
        return EditorPrefs.GetString(PrefsKeyPrefix + "AssetStoreLatestVersion", "");
    }

    // Update Asset Store package (opens Package Manager)
    public static void UpdateAssetStorePackage()
    {
        if (debugMode) Debug.Log("Opening Package Manager for Asset Store update...");
        
        // Open Package Manager window and focus on the package
        UnityEditor.PackageManager.UI.Window.Open(packageName);
        
        if (window != null)
            window.LogMessage("Please update the package in the Package Manager window that just opened.", 1);
    }

    /// <summary>
    /// Gets debug information about the current asset version and update status
    /// </summary>
    public static string GetVersionDebugInfo()
    {
        string info = $"Distribution Type: {distributionType}\n";
        info += $"Current Version: {GetCurrentPackageVersion()}\n";
        info += $"Is GitHub Version: {IsGithubVersion()}\n";
        info += $"Is Asset Store Version: {IsAssetStoreVersion()}\n";
        info += $"GitHub Update Available: {IsGithubUpdateAvailable()}\n";
        info += $"Asset Store Update Available: {IsAssetStoreUpdateAvailable()}\n";
        info += $"Asset Store Latest Version: {AssetStoreLatestVersion()}\n";
        info += $"Should Check GitHub Updates: {ShouldCheckGithubUpdates()}";
        return info;
    }
    #endregion

    #region Version Detection
    /// <summary>
    /// Determines if this is the Asset Store version by checking for AssetStore.cs file
    /// </summary>
    public static bool IsAssetStoreVersion()
    {
        if (isAssetStoreVersion.HasValue)
            return isAssetStoreVersion.Value;

        // Check if AssetStore.cs exists in the same directory
        string currentScriptPath = GetCurrentScriptDirectory();
        string assetStoreFilePath = System.IO.Path.Combine(currentScriptPath, "AssetStore.cs");
        
        bool hasAssetStoreFile = System.IO.File.Exists(assetStoreFilePath);
        
        // Cache the result
        isAssetStoreVersion = hasAssetStoreFile;
        EditorPrefs.SetBool(PrefsKeyPrefix + "IsAssetStoreVersion", hasAssetStoreFile);
        
        if (debugMode) Debug.Log($"Asset Store version detected: {hasAssetStoreFile}");
        
        return hasAssetStoreFile;
    }

    /// <summary>
    /// Determines if this is the GitHub version by checking for Github.cs file
    /// </summary>
    public static bool IsGithubVersion()
    {
        if (isGithubVersion.HasValue)
            return isGithubVersion.Value;

        // Check if Github.cs exists in the same directory
        string currentScriptPath = GetCurrentScriptDirectory();
        string githubFilePath = System.IO.Path.Combine(currentScriptPath, "Github.cs");
        
        bool hasGithubFile = System.IO.File.Exists(githubFilePath);
        
        // Cache the result
        isGithubVersion = hasGithubFile;
        EditorPrefs.SetBool(PrefsKeyPrefix + "IsGithubVersion", hasGithubFile);
        
        if (debugMode) Debug.Log($"GitHub version detected: {hasGithubFile}");
        
        return hasGithubFile;
    }

    /// <summary>
    /// Gets the current distribution type
    /// Priority: If both files exist, it's considered GitHub (dev build)
    /// </summary>
    public static string GetDistributionType()
    {
        bool isGithub = IsGithubVersion();
        bool isAssetStore = IsAssetStoreVersion();
        
        if (isGithub && isAssetStore)
        {
            if (debugMode) Debug.Log("Both Github and AssetStore scripts detected - treating as GitHub (dev build)");
            return "GitHub (Dev Build)";
        }
        else if (isGithub)
        {
            return "GitHub Build";
        }
        else if (isAssetStore)
        {
            return "Asset Store Build";
        }
        else
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets the cached distribution type without recalculating
    /// </summary>
    public static string GetCachedDistributionType()
    {
        // If not initialized yet, initialize it
        if (string.IsNullOrEmpty(distributionType))
        {
            InitializeDistributionType();
        }
        return distributionType;
    }

    /// <summary>
    /// Gets the directory where this script is located
    /// </summary>
    private static string GetCurrentScriptDirectory()
    {
        // Search for the script by name
        string[] guids = AssetDatabase.FindAssets("ServerUpdateProcess t:MonoScript");
        if (guids.Length > 0)
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            return System.IO.Path.GetDirectoryName(scriptPath);
        }
        
        // Fallback: return empty string if script not found
        if (debugMode) Debug.LogWarning("Could not find ServerUpdateProcess script path");
        return "";
    }

    /// <summary>
    /// Checks if GitHub updates should be enabled for this version
    /// </summary>
    public static bool ShouldCheckGithubUpdates()
    {
        // Check GitHub updates if:
        // 1. This is a pure GitHub version (Github.cs exists, AssetStore.cs doesn't)
        // 2. This is a dev build (both files exist - treated as GitHub)
        // Don't check GitHub updates only if it's pure Asset Store version (AssetStore.cs exists, Github.cs doesn't)
        
        bool isGithub = IsGithubVersion();
        bool isAssetStore = IsAssetStoreVersion();
        
        if (isGithub && isAssetStore)
        {
            // Dev build - check GitHub updates
            return true;
        }
        else if (isGithub && !isAssetStore)
        {
            // Pure GitHub version - check GitHub updates
            return true;
        }
        else if (!isGithub && isAssetStore)
        {
            // Pure Asset Store version - don't check GitHub updates
            return false;
        }
        else
        {
            // Unknown version - default to not checking GitHub updates
            return false;
        }
    }
    #endregion

    public static string GetCurrentPackageVersion()
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            // Start a request to list all packages
            listRequest = Client.List();
            
            // Wait for the request to complete
            while (!listRequest.IsCompleted) { }

            if (listRequest.Status == StatusCode.Success)
            {
                foreach (var package in listRequest.Result)
                {
                    if (package.name == packageName)
                    {
                        currentVersion = package.version;
                        break;
                    }
                }
            }
            else if (listRequest.Status == StatusCode.Failure)
            {
                if (debugMode) Debug.LogError($"Failed to get package version: {listRequest.Error.message}");
                return "unknown";
            }
        }
        
        return currentVersion;
    }
    #region SpacetimeDB Update
    public static bool CheckForSpacetimeDBUpdate()
    {
        FetchSpacetimeDBVersionAsync();
        return EditorPrefs.GetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", false);
    }

    private static void FetchSpacetimeDBVersionAsync()
    {
        string url = "https://api.github.com/repos/clockworklabs/SpacetimeDB/releases/latest";

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("User-Agent", "UnitySpacetimeDBChecker");
        
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        operation.completed += _ => {
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (debugMode) Debug.LogError("SpacetimeDB API request failed: " + request.error);
            }
            else
            {
                ProcessSpacetimeDBReleaseResponse(request.downloadHandler.text);
            }
            request.Dispose();
        };
    }

    private static void ProcessSpacetimeDBReleaseResponse(string json)
    {
        try
        {
            // Parse the JSON response to extract the tag_name
            ReleaseData releaseData = JsonUtility.FromJson<ReleaseData>(json);
            string version = releaseData.tag_name;
            
            if (!string.IsNullOrEmpty(version) && version.StartsWith("v"))
            {
                // Remove 'v' prefix if present
                version = version.Substring(1);
            }
            
            spacetimeDBLatestVersion = version;
            EditorPrefs.SetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", spacetimeDBLatestVersion);

            if (debugMode) Debug.Log($"Latest SpacetimeDB version: {spacetimeDBLatestVersion}");
            
            // Compare with current installed version
            string currentSpacetimeDBVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersion", "");
            if (!string.IsNullOrEmpty(currentSpacetimeDBVersion) && currentSpacetimeDBVersion != spacetimeDBLatestVersion)
            {
                if (debugMode) Debug.Log($"SpacetimeDB update available! Current: {currentSpacetimeDBVersion}, Latest: {spacetimeDBLatestVersion}");
                EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", true);
            }
            else
            {
                EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", false);
            }
        }
        catch (Exception e)
        {
            if (debugMode) Debug.LogError("Failed to parse SpacetimeDB release data: " + e.Message);
        }
    }

    // Public method to check if a SpacetimeDB update is available
    public static bool IsSpacetimeDBUpdateAvailable()
    {
        return EditorPrefs.GetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", false);
    }
    
    // Public method to get the latest SpacetimeDB version
    public static string SpacetimeDBLatestVersion()
    {
        return EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
    }

    // Data model for SpacetimeDB release response
    [Serializable]
    private class ReleaseData
    {
        public string tag_name;
    }

    // Data models for SpacetimeDB SDK tags response
    [Serializable]
    private class TagData
    {
        public string name;
    }

    [Serializable]
    private class TagDataArray
    {
        public TagData[] tags;
    }
    #endregion

    #region SpacetimeSDK Update
    public static bool CheckForSpacetimeSDKUpdate()
    {
        FetchSpacetimeSDKVersionAsync();
        return EditorPrefs.GetBool(PrefsKeyPrefix + "SpacetimeSDKUpdateAvailable", false);
    }

    private static void FetchSpacetimeSDKVersionAsync()
    {
        string url = "https://api.github.com/repos/clockworklabs/com.clockworklabs.spacetimedbsdk/tags";

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("User-Agent", "UnitySpacetimeSDKChecker");
        
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        operation.completed += _ => {
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (debugMode) Debug.LogError("SpacetimeDB SDK API request failed: " + request.error);
            }
            else
            {
                ProcessSpacetimeSDKTagsResponse(request.downloadHandler.text);
            }
            request.Dispose();
        };
    }

    private static void ProcessSpacetimeSDKTagsResponse(string json)
    {
        try
        {
            // Parse the JSON response to extract the first (latest) tag
            TagData[] tagsData = JsonUtility.FromJson<TagDataArray>("{\"tags\":" + json + "}").tags;
            
            if (tagsData != null && tagsData.Length > 0)
            {
                string version = tagsData[0].name; // First tag is the latest
                
                if (!string.IsNullOrEmpty(version) && version.StartsWith("v"))
                {
                    // Remove 'v' prefix if present
                    version = version.Substring(1);
                }
                
                spacetimeSDKLatestVersion = version;
                EditorPrefs.SetString(PrefsKeyPrefix + "SpacetimeSDKLatestVersion", spacetimeSDKLatestVersion);

                if (debugMode) Debug.Log($"Latest SpacetimeDB SDK version: {spacetimeSDKLatestVersion}");
                
                // Compare with current installed version
                string currentSDKVersion = GetCurrentSDKVersion();
                if (!string.IsNullOrEmpty(currentSDKVersion) && currentSDKVersion != spacetimeSDKLatestVersion)
                {
                    if (debugMode) Debug.Log($"SpacetimeDB SDK update available! Current: {currentSDKVersion}, Latest: {spacetimeSDKLatestVersion}");
                    EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeSDKUpdateAvailable", true);
                }
                else
                {
                    EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeSDKUpdateAvailable", false);
                }
            }
            else
            {
                if (debugMode) Debug.LogWarning("No tags found for SpacetimeDB SDK");
                EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeSDKUpdateAvailable", false);
            }
        }
        catch (Exception e)
        {
            if (debugMode) Debug.LogError("Failed to parse SpacetimeDB SDK tags data: " + e.Message);
        }
    }

    // Get the current installed SDK version
    private static string GetCurrentSDKVersion()
    {
        const string sdkPackageName = "com.clockworklabs.spacetimedbsdk";
        
        ListRequest request = Client.List();
        while (!request.IsCompleted) { }

        if (request.Status == StatusCode.Success)
        {
            foreach (var package in request.Result)
            {
                if (package.name == sdkPackageName)
                {
                    return package.version;
                }
            }
        }
        else if (request.Status == StatusCode.Failure)
        {
            if (debugMode) Debug.LogError($"Failed to get SDK version: {request.Error.message}");
        }
        
        return "";
    }

    // Public method to check if a SpacetimeDB SDK update is available
    public static bool IsSpacetimeSDKUpdateAvailable()
    {
        return EditorPrefs.GetBool(PrefsKeyPrefix + "SpacetimeSDKUpdateAvailable", false);
    }
    
    // Public method to get the latest SpacetimeDB SDK version
    public static string SpacetimeSDKLatestVersion()
    {
        return EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeSDKLatestVersion", "");
    }

    // Public method to get the current installed SpacetimeDB SDK version
    public static string GetCurrentSpacetimeSDKVersion()
    {
        return GetCurrentSDKVersion();
    }
    #endregion

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
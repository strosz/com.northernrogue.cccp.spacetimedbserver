using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

// Handles new updates for the asset from Unity, Github and SpacetimeDB ///

namespace NorthernRogue.CCCP.Editor {

[InitializeOnLoad]
public class ServerUpdateProcess
{
    public static bool debugMode = false; // Set in ServerWindow

    /////////////////////////////// Cosmos Github Update Checker ///////////////////////////////
    private const string CosmosGithubUpdateAvailablePrefKey = "CCCP_GithubUpdateAvailable";
    private const string owner = "strosz";
    private const string packageName = "com.northernrogue.cccp.spacetimedbserver";
    private const string branch = "main";

    private static ListRequest listRequest;
    private static string currentVersion = "";
    private static string latestCommitSha = "";
    private static AddRequest addRequest;
    private static ServerWindow window;

    /////////////////////// Cosmos Unity Version Update Checker ///////////////////////////
    //private const string CosmosUnityUpdateAvailablePrefKey = "CCCP_UnityUpdateAvailable";    /////////////////////////////// SpacetimeDB Update Checker ///////////////////////////////

    /////////////////////////////// SpacetimeDB Update Installer ///////////////////////////////
    private const string SpacetimeDBUpdateAvailablePrefKey = "CCCP_SpacetimeDBUpdateAvailable";
    private const string SpacetimeDBVersionPrefKey = "CCCP_SpacetimeDBVersion";
    private const string SpacetimeDBLatestVersionPrefKey = "CCCP_SpacetimeDBLatestVersion";
    private static string spacetimeDBLatestVersion = "";
    
    static ServerUpdateProcess()
    {
        EditorApplication.delayCall += () => {
            //Debug.Log("ServerUpdateProcess: DelayCall executed!"); // Keep for debugging
            if (debugMode) Debug.Log("ServerUpdateProcess: Starting update checks...");
            CheckForGithubUpdate();
            CheckForSpacetimeDBUpdate();
        };
    }

    #region Github Update
    public static bool CheckForGithubUpdate()
    {
        string storedSha = EditorPrefs.GetString("CCCP_LastCommitSha", "");
        if (!string.IsNullOrEmpty(storedSha))
        {
            latestCommitSha = storedSha;
        }
        if (debugMode) Debug.Log($"Checked for GitHub update - Stored SHA: {latestCommitSha}");
        
        FetchLatestCommitAsync();
        return EditorPrefs.GetBool(CosmosGithubUpdateAvailablePrefKey, false);
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
                EditorPrefs.SetBool(CosmosGithubUpdateAvailablePrefKey, false);
            }
            else if (fetchedSha != latestCommitSha)
            {
                if (debugMode) Debug.Log($"New commit detected! Old: {latestShaShort(latestCommitSha)}  New: {latestShaShort(fetchedSha)}");
                latestCommitSha = fetchedSha;
                EditorPrefs.SetString("CCCP_LastCommitSha", latestCommitSha);
                EditorPrefs.SetBool(CosmosGithubUpdateAvailablePrefKey, true);
                DisplayGithubUpdateAvailable();
            }
            else
            {
                if (debugMode) Debug.Log("No new commit found.");
                EditorPrefs.SetBool(CosmosGithubUpdateAvailablePrefKey, false);
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
        // EditorPrefs can also be called directly
        return EditorPrefs.GetBool(CosmosGithubUpdateAvailablePrefKey, false);
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
                EditorPrefs.SetBool(CosmosGithubUpdateAvailablePrefKey, false);
            }
            else if (addRequest.Status == StatusCode.Failure)
            {
                Debug.LogError("Package update failed: " + addRequest.Error.message);
                if (window != null && debugMode)
                    window.LogMessage("Package update failed: " + addRequest.Error.message, -1);
                EditorPrefs.SetBool(CosmosGithubUpdateAvailablePrefKey, false); // To not occupy UI space
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
        return EditorPrefs.GetBool(SpacetimeDBUpdateAvailablePrefKey, false);
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
            EditorPrefs.SetString(SpacetimeDBLatestVersionPrefKey, spacetimeDBLatestVersion);
            
            if (debugMode) Debug.Log($"Latest SpacetimeDB version: {spacetimeDBLatestVersion}");
            
            // Compare with current installed version
            string currentSpacetimeDBVersion = EditorPrefs.GetString(SpacetimeDBVersionPrefKey, "");
            if (!string.IsNullOrEmpty(currentSpacetimeDBVersion) && currentSpacetimeDBVersion != spacetimeDBLatestVersion)
            {
                if (debugMode) Debug.Log($"SpacetimeDB update available! Current: {currentSpacetimeDBVersion}, Latest: {spacetimeDBLatestVersion}");
                EditorPrefs.SetBool(SpacetimeDBUpdateAvailablePrefKey, true);
            }
            else
            {
                EditorPrefs.SetBool(SpacetimeDBUpdateAvailablePrefKey, false);
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
        return EditorPrefs.GetBool(SpacetimeDBUpdateAvailablePrefKey, false);
    }
    
    // Public method to get the latest SpacetimeDB version
    public static string SpacetimeDBLatestVersion()
    {
        return EditorPrefs.GetString(SpacetimeDBLatestVersionPrefKey, "");
    }

    // Data model for SpacetimeDB release response
    [Serializable]
    private class ReleaseData
    {
        public string tag_name;
    }
    #endregion

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
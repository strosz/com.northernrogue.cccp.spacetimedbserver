using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

// Handles new updates for the asset from Unity, Github and SpacetimeDB ///
//////////////// made by Northern Rogue /// Mathias Toivonen //////////////

namespace NorthernRogue.CCCP.Editor {

[InitializeOnLoad]
public class ServerUpdateProcess : EditorWindow
{
    /////////////////////////////// Cosmos Github Update Checker ///////////////////////////////
    private const string CosmosGithubUpdateAvailablePrefKey = "CCCP_GithubUpdateAvailable";
    private const string owner = "strosz";
    private const string repo = "com.northernrogue.cccp.spacetimedbserver";
    private const string branch = "main";

    private static string latestCommitSha = "";
    private static AddRequest addRequest;
    private static ServerWindow window;
    public static bool debugMode = false;

    /////////////////////////////// Cosmos Unity Update Checker ///////////////////////////////
    //private const string CosmosUnityUpdateAvailablePrefKey = "CCCP_UnityUpdateAvailable";

    /////////////////////////////// SpacetimeDB Update Checker ///////////////////////////////
    //private const string SpacetimeDBUpdateAvailablePrefKey = "CCCP_SpacetimeDBUpdateAvailable";

    /////////////////////////////// SpacetimeDB Update Installer ///////////////////////////////
    // To be created

    static ServerUpdateProcess()
    {
        EditorApplication.delayCall += () => {
            var instance = CreateInstance<ServerUpdateProcess>();
            instance.CheckForNewCommit();
        };
    }

    #region Github Update
    public bool CheckForNewCommit()
    {
        string storedSha = EditorPrefs.GetString("CCCP_LastCommitSha", "");
        if (!string.IsNullOrEmpty(storedSha))
        {
            latestCommitSha = storedSha;
        }
        
        FetchLatestCommitAsync();
        return EditorPrefs.GetBool(CosmosGithubUpdateAvailablePrefKey, false);
    }

    private void FetchLatestCommitAsync()
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/commits/{branch}";

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
        }
            request.Dispose();
        };
    }

    private void ProcessCommitResponse(string json)
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
    }

    // Helper to shorten SHA
    private string latestShaShort(string sha)
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
    private void DisplayGithubUpdateAvailable()
    {
        //window.LogMessage("Cosmos Cove Control Panel Update Available - Please update to the latest version in the Package Manager.", 1);
        // The EditorPref is set to false in ProcessCommitResponse() when no new commit is found
        // So this LogMessage will only be displayed once
    }

    public static void UpdateGithubPackage()
    {
        // Use the full GitHub URL format for package updates
        string gitUrl = $"https://github.com/{owner}/{repo}.git";
        
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
                if (window != null)
                    window.LogMessage("Package updated successfully: " + addRequest.Result.packageId, 1);
                EditorPrefs.SetBool(CosmosGithubUpdateAvailablePrefKey, false);
            }
            else if (addRequest.Status == StatusCode.Failure)
            {
                Debug.LogError("Package update failed: " + addRequest.Error.message);
                if (window != null)
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

} // Class
} // Namespace

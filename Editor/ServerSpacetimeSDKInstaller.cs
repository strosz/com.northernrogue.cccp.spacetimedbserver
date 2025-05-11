using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System;

// Checking and installation of the official Spacetime SDK for Unity ///

namespace NorthernRogue.CCCP.Editor {
    
public static class ServerSpacetimeSDKInstaller
{
    private static ListRequest s_ListRequest;
    private static AddRequest s_AddRequest;
    private static bool s_IsCheckingInstalled = false;
    private static bool s_IsInstalling = false;
    private static float s_TimeoutStartTime = 0f;
    private static float s_TimeoutDuration = 60f; // 1 minute timeout
    private const string SDK_PACKAGE_NAME = "com.clockworklabs.spacetimedbsdk";
    private const string SDK_PACKAGE_URL = "https://github.com/clockworklabs/com.clockworklabs.spacetimedbsdk.git";

    // Callback delegates
    public delegate void CheckCompletedCallback(bool isInstalled);
    public delegate void InstallCompletedCallback(bool success, string errorMessage = null);

    /// <summary>
    /// Checks if the SpacetimeDB SDK is already installed
    /// </summary>
    /// <param name="callback">Callback that will be invoked with the result</param>
    public static void IsSDKInstalled(CheckCompletedCallback callback)
    {
        // First check if we can find the SDK assembly directly - this is faster than using Package Manager
        try
        {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.GetName().Name == "ClockworkLabs.SpacetimeDBSDK")
                {
                    callback?.Invoke(true);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SpacetimeDB] Error checking SDK assembly: {ex.Message}");
            // Continue with package manager check if assembly check fails
        }

        if (s_IsCheckingInstalled)
        {
            Debug.LogWarning("[SpacetimeDB] Already checking if SDK is installed");
            callback?.Invoke(false);
            return;
        }

        s_IsCheckingInstalled = true;
        s_TimeoutStartTime = Time.realtimeSinceStartup;
        s_ListRequest = Client.List(true); // Request offlineMode=true for faster response
        
        EditorApplication.update += CheckInstalledUpdate;

        void CheckInstalledUpdate()
        {
            // Check for timeout
            if (Time.realtimeSinceStartup - s_TimeoutStartTime > s_TimeoutDuration)
            {
                // Warns even though it works. Needs to be checked.
                //Debug.LogWarning("[SpacetimeDB] SDK check timed out after " + s_TimeoutDuration + " seconds");

                EditorApplication.update -= CheckInstalledUpdate;
                s_IsCheckingInstalled = false;
                s_ListRequest = null;
                callback?.Invoke(false);
                return;
            }

            if (s_ListRequest == null || !s_ListRequest.IsCompleted) return;
            
            EditorApplication.update -= CheckInstalledUpdate;
            s_IsCheckingInstalled = false;
            
            bool isInstalled = false;
            if (s_ListRequest.Status == StatusCode.Success)
            {
                foreach (var pkg in s_ListRequest.Result)
                {
                    if (pkg.name == SDK_PACKAGE_NAME)
                    {
                        isInstalled = true;
                        break;
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[SpacetimeDB] SDK check failed: {(s_ListRequest.Error != null ? s_ListRequest.Error.message : "Unknown error")}");
            }
            
            s_ListRequest = null;
            callback?.Invoke(isInstalled);
        }
    }

    /// <summary>
    /// Installs the SpacetimeDB SDK from GitHub
    /// </summary>
    /// <param name="callback">Callback that will be invoked when installation completes</param>
    public static void InstallSDK(InstallCompletedCallback callback)
    {
        if (s_IsInstalling)
        {
            Debug.LogWarning("[SpacetimeDB] SDK installation already in progress");
            callback?.Invoke(false, "Installation already in progress");
            return;
        }

        // First check if it's already installed
        IsSDKInstalled((isInstalled) =>
        {
            if (isInstalled)
            {
                Debug.Log("[SpacetimeDB] SDK is already installed");
                callback?.Invoke(true);
                return;
            }

            try
            {
                s_IsInstalling = true;
                s_TimeoutStartTime = Time.realtimeSinceStartup;
                Debug.Log("[SpacetimeDB] Installing SDK from Git: " + SDK_PACKAGE_URL);
                
                // Try to add with specific version
                s_AddRequest = Client.Add(SDK_PACKAGE_URL);
                EditorApplication.update += InstallUpdate;
                
                void InstallUpdate()
                {
                    // Check for timeout
                    if (Time.realtimeSinceStartup - s_TimeoutStartTime > s_TimeoutDuration)
                    {
                        Debug.LogWarning("[SpacetimeDB] SDK installation timed out after " + s_TimeoutDuration + " seconds");
                        EditorApplication.update -= InstallUpdate;
                        s_IsInstalling = false;
                        s_AddRequest = null;
                        callback?.Invoke(false, "Installation timed out");
                        return;
                    }
                    
                    if (s_AddRequest == null || !s_AddRequest.IsCompleted) return;

                    EditorApplication.update -= InstallUpdate;
                    s_IsInstalling = false;

                    if (s_AddRequest.Status == StatusCode.Success)
                    {
                        Debug.Log("[SpacetimeDB] SDK installed successfully!");
                        
                        // Force a domain reload to prevent editor freeze
                        EditorUtility.RequestScriptReload();
                        
                        callback?.Invoke(true);
                    }
                    else if (s_AddRequest.Status >= StatusCode.Failure)
                    {
                        string errorMessage = s_AddRequest.Error != null ? s_AddRequest.Error.message : "Unknown error";
                        Debug.LogError($"[SpacetimeDB] Failed to install SDK: {errorMessage}");
                        callback?.Invoke(false, errorMessage);
                    }
                    
                    s_AddRequest = null;
                }
            }
            catch (Exception ex)
            {
                s_IsInstalling = false;
                Debug.LogException(ex);
                callback?.Invoke(false, ex.Message);
            }
        });
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace NorthernRogue.CCCP.Editor
{
    public static class ServerSpacetimeSDKInstaller
    {
        private static ListRequest s_ListRequest;
        private static AddRequest s_AddRequest;
        private static bool s_IsCheckingInstalled = false;
        private static bool s_IsInstalling = false;

        // Callback delegates
        public delegate void CheckCompletedCallback(bool isInstalled);
        public delegate void InstallCompletedCallback(bool success, string errorMessage = null);

        /// <summary>
        /// Checks if the SpacetimeDB SDK is already installed
        /// </summary>
        /// <param name="callback">Callback that will be invoked with the result</param>
        public static void IsSDKInstalled(CheckCompletedCallback callback)
        {
            if (s_IsCheckingInstalled)
            {
                Debug.LogWarning("[SpacetimeDB] Already checking if SDK is installed");
                return;
            }

            s_IsCheckingInstalled = true;
            s_ListRequest = Client.List();
            
            EditorApplication.update += () =>
            {
                if (!s_ListRequest.IsCompleted) return;
                
                EditorApplication.update -= CheckInstalledUpdate;
                s_IsCheckingInstalled = false;
                
                bool isInstalled = false;
                if (s_ListRequest.Status == StatusCode.Success)
                {
                    foreach (var pkg in s_ListRequest.Result)
                    {
                        if (pkg.name == "com.clockworklabs.spacetimedbsdk")
                        {
                            isInstalled = true;
                            break;
                        }
                    }
                }
                
                callback?.Invoke(isInstalled);
            };
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
                return;
            }

            // First check if it's already installed
            IsSDKInstalled((isInstalled) =>
            {
                if (isInstalled)
                {
                    callback?.Invoke(true);
                    return;
                }

                s_IsInstalling = true;
                Debug.Log("[SpacetimeDB] Installing SDK from Git...");
                s_AddRequest = Client.Add("https://github.com/clockworklabs/com.clockworklabs.spacetimedbsdk.git");

                EditorApplication.update += () =>
                {
                    if (s_AddRequest == null || !s_AddRequest.IsCompleted) return;

                    EditorApplication.update -= InstallUpdate;
                    s_IsInstalling = false;

                    if (s_AddRequest.Status == StatusCode.Success)
                    {
                        Debug.Log("[SpacetimeDB] SDK installed successfully!");
                        callback?.Invoke(true);
                    }
                    else if (s_AddRequest.Status >= StatusCode.Failure)
                    {
                        string errorMessage = s_AddRequest.Error.message;
                        Debug.LogError($"[SpacetimeDB] Failed to install SDK: {errorMessage}");
                        callback?.Invoke(false, errorMessage);
                    }
                };
            });
        }

        private static void CheckInstalledUpdate()
        {
            if (!s_ListRequest.IsCompleted) return;
            
            EditorApplication.update -= CheckInstalledUpdate;
            s_IsCheckingInstalled = false;
        }

        private static void InstallUpdate()
        {
            if (s_AddRequest == null || !s_AddRequest.IsCompleted) return;

            EditorApplication.update -= InstallUpdate;
            s_IsInstalling = false;
        }
    }
}
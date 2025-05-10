using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;
using System.Threading;

// Provides a compability report for WSL and allows server installer window to continue ///
///////////////////// made by Northern Rogue /// Mathias Toivonen /////////////////////////

namespace NorthernRogue.CCCP.Editor {

public class ServerCompabilityReport : EditorWindow
{
    private string resultDetails = "Click the button below to check WSL2 support.";
    private bool isWSL2Supported = false;
    private bool isChecking = false;
    
    // Callbacks for WSL installation
    private Action installWSL1Action;
    private Action installWSL2Action;
    
    // Constants for timeouts
    private const int PROCESS_TIMEOUT_MS = 5000;

    /// <summary>
    /// Checks if the system supports WSL2 asynchronously and returns the result.
    /// Optionally shows a dialog with the results.
    /// </summary>
    /// <param name="showDialog">Whether to show a dialog with the results</param>
    /// <param name="onInstallWSL1">Action to call when user chooses to install WSL1</param>
    /// <param name="onInstallWSL2">Action to call when user chooses to install WSL2</param>
    /// <returns>True if the system supports WSL2, false otherwise</returns>
    public static async Task<bool> CheckWSL2Support(bool showDialog = true, Action onInstallWSL1 = null, Action onInstallWSL2 = null)
    {
        try
        {
            // Perform the check asynchronously with a timeout
            var timeoutTask = Task.Delay(30000); // 30 second overall timeout
            var checkTask = PerformWSL2SupportCheckAsync();
            
            // Wait for either the check to complete or timeout
            var completedTask = await Task.WhenAny(checkTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                // Timeout occurred
                UnityEngine.Debug.LogWarning("WSL2 support check timed out after 30 seconds.");
                // Show dialog with timeout message if requested
                if (showDialog)
                {
                    EditorApplication.delayCall += () => ShowWindowWithResults(
                        false, 
                        "The WSL2 support check timed out.\n\nThis may indicate an issue with system commands or resource constraints. " +
                        "Try running the check again or manually checking WSL2 compatibility via Command Prompt.",
                        onInstallWSL1,
                        onInstallWSL2);
                }
                return false;
            }
            
            // Check completed successfully, get the result
            (bool isSupported, string details) = await checkTask;

            // If dialog is requested, show it on the main thread
            if (showDialog)
            {
                // Schedule ShowWindowWithResults to run on the main thread
                EditorApplication.delayCall += () => ShowWindowWithResults(
                    isSupported, 
                    details,
                    onInstallWSL1,
                    onInstallWSL2);
            }

            return isSupported;
        }
        catch (Exception ex)
        {
            // Log the exception and return false
            UnityEngine.Debug.LogError($"Exception in CheckWSL2Support: {ex}");
            
            if (showDialog)
            {
                EditorApplication.delayCall += () => ShowWindowWithResults(
                    false, 
                    $"An error occurred while checking WSL2 compatibility: {ex.Message}\n\n" +
                    "Please try again or check WSL2 compatibility manually.",
                    onInstallWSL1,
                    onInstallWSL2);
            }
            
            return false;
        }
    }

    /// <summary>
    /// Helper method to display the results in the window. Must be called on the main thread.
    /// </summary>
    private static void ShowWindowWithResults(bool isSupported, string details, Action onInstallWSL1, Action onInstallWSL2)
    {
        try
        {
            var window = GetWindow<ServerCompabilityReport>("WSL2 Support Check");
            window.minSize = new Vector2(450, 350);
            window.resultDetails = details;
            window.isWSL2Supported = isSupported;
            window.isChecking = false; // Ensure checking state is reset
            window.installWSL1Action = onInstallWSL1;
            window.installWSL2Action = onInstallWSL2;
            window.Repaint();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error showing WSL2 support window: {ex.Message}");
            // At least show a dialog with the results
            EditorUtility.DisplayDialog("WSL2 Support Check", 
                $"Result: {(isSupported ? "Supported" : "Not Supported")}\n\n{details}", "OK");
        }
    }
    
    /// <summary>
    /// Performs the actual WSL2 support checks asynchronously.
    /// </summary>
    /// <returns>A tuple containing the support status (bool) and detailed results (string).</returns>
    private static async Task<(bool isSupported, string details)> PerformWSL2SupportCheckAsync()
    {
        string currentResultDetails = "";
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            currentResultDetails = "This check is only valid on Windows systems.";
            return (false, currentResultDetails);
        }

        currentResultDetails = "Windows Subsystem for Linux 2 Requirements Check:\n\n";

        // Check Windows version (synchronous, generally fast)
        bool validWindowsVersion = CheckWindowsVersion();
        currentResultDetails += $"✓ Windows Version: {(validWindowsVersion ? "Compatible" : "Not Compatible - Requires Windows 10 version 1903+ or Windows 11")}\n";

        // Check if system is 64-bit (synchronous, very fast)
        bool is64Bit = Environment.Is64BitOperatingSystem;
        currentResultDetails += $"✓ 64-bit OS: {(is64Bit ? "Yes" : "No - WSL2 requires 64-bit Windows")}\n";

        // Variables to track check results with defaults
        bool virtualizationEnabled = false;
        bool hyperVAvailable = false;
        //bool hyperVInstalled = false;

        // Check if virtualization is enabled in BIOS (asynchronous with timeout)
        try
        {
            
            virtualizationEnabled = await CheckVirtualizationEnabledAsync();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error checking virtualization: {ex.Message}");
            currentResultDetails += $"✓ Virtualization Enabled: Error checking - {ex.Message}\n";
        }
        
        if (!currentResultDetails.Contains("Virtualization Enabled: Error"))
        {
            currentResultDetails += $"✓ Virtualization Enabled: {(virtualizationEnabled ? "Yes" : "No - Please enable in BIOS settings")}\n";
        }

        // Check if Hyper-V is available/installable (asynchronous with timeout)
        try
        {
            
            hyperVAvailable = await CheckCPUVirtualizationSupportAsync();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error checking Hyper-V availability: {ex.Message}");
            currentResultDetails += $"✓ Hyper-V Supported: Error checking - {ex.Message}\n";
        }
        
        if (!currentResultDetails.Contains("Hyper-V Available: Error"))
        {
            currentResultDetails += $"✓ Hyper-V Supported: {(hyperVAvailable ? "Yes" : "No - Your CPU may not support required virtualization features")}\n";
        }

        // Determine overall compatibility
        bool canInstallWSL2 = validWindowsVersion && is64Bit && hyperVAvailable && virtualizationEnabled;
        currentResultDetails += $"\nResult: {(canInstallWSL2 ? "Your system can support WSL2!\n\n" : "Your system does not meet all requirements for WSL2.\n\n")}";

        // Provide installation instructions if needed
        if (canInstallWSL2)
        {
            currentResultDetails += 
            "It's recommended to install WSL2 for your system for best performance.\n" +
            "Regardless of chosen WSL, remember to always backup anything important\n" +
            "on your PC before continuing.\n\n" +
            "WSL2 enables a Virtual Machine Platform hypervisor, which can be\n" +
            "incompatible with WMWare and VirtualBox. Some older Windows PCs may\n" +
            "also not support WSL2. While this compability tool should be accurate\n" +
            "I leave no responsibility for any issues that may occur from installing WSL2.\n" +
            "Refer to the CCCP documentation if the automatic installation fails.";
        } else {
            currentResultDetails += 
            "It's recommended to install WSL1 for your system for better compatibility.\n" +
            "Regardless of chosen WSL, remember to always backup anything important\n" +
            "on your PC before continuing.\n\n" +
            "WSL1 may require a restart and will probably show errors on the other\n" +
            "Debian installers like Curl and Rust, but will succesfully install.\n" +
            "It may display installing version 2, but it is actually installing WSL1.\n" +
            "Refer to the CCCP documentation if the automatic installation fails.";
        }

        return (canInstallWSL2, currentResultDetails);
    }

    #region System Checks
    private static bool CheckWindowsVersion()
    {
        try
        {
            // Get Windows version
            var osVersion = Environment.OSVersion.Version;
            
            // Windows 11 (Windows 10 version 10.0.22000 or higher)
            if (osVersion.Major == 10 && osVersion.Build >= 22000)
                return true;
                
            // Windows 10 version 1903 (build 18362) or higher
            if (osVersion.Major == 10 && osVersion.Build >= 18362)
                return true;
                
            return false;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error checking Windows version: {ex.Message}");
            return false;
        }
    }

    // Made static and async with timeout
    private static async Task<bool> CheckVirtualizationEnabledAsync()
    {
        string detailedOutput = "No output received"; // For logging
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "powershell";
                process.StartInfo.Arguments = @"-Command ""
                    try {
                        # Check for virtualization firmware enabled
                        $result = (Get-WmiObject Win32_Processor).VirtualizationFirmwareEnabled;
                        Write-Output $result.ToString();
                    } 
                    catch {
                        # If anything goes wrong, output false
                        Write-Output 'False';
                    }
                """;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                // Use a shorter timeout for this simpler command
                using (var cts = new CancellationTokenSource(3000)) // 3 second timeout
                {
                    try
                    {
                        process.Start();
                        // Read both output and error streams with smaller timeout
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        // Wait for the process to exit or timeout
                        bool exited = process.WaitForExit(3000); // 3 second timeout

                        detailedOutput = await outputTask;
                        string errorOutput = await errorTask;

                        if (!exited)
                        {
                            try { process.Kill(); } catch { }
                            UnityEngine.Debug.LogWarning($"CheckVirtualizationEnabledAsync timed out.");
                            return false;
                        }

                        // Log the actual output received for debugging
                        //UnityEngine.Debug.Log($"CheckVirtualizationEnabledAsync PowerShell Output: '{detailedOutput.Trim()}', Error Stream: '{errorOutput.Trim()}'");
                        
                        return detailedOutput.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                    }
                    catch (Exception innerEx)
                    {
                        // Log exception and return false
                        UnityEngine.Debug.LogError($"Error in PowerShell process for virtualization check: {innerEx.Message}");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log outer exception and return false
            UnityEngine.Debug.LogError($"Error preparing virtualization check: {ex.Message}");
            return false;
        }
    }

    // Made static and async with timeout
    private static async Task<bool> CheckCPUVirtualizationSupportAsync()
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "powershell";
                process.StartInfo.Arguments = "-Command \"systeminfo | Select-String 'Hyper-V Requirements' -Context 0,10\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                
                // Create a cancellation token for timeout
                using (var cts = new CancellationTokenSource(PROCESS_TIMEOUT_MS))
                {
                    try
                    {
                        process.Start();
                        
                        // Read output asynchronously with timeout
                        var readTask = process.StandardOutput.ReadToEndAsync();
                        
                        // Wait for completion or timeout
                        if (await Task.WhenAny(readTask, Task.Delay(PROCESS_TIMEOUT_MS, cts.Token)) != readTask)
                        {
                            // Timeout occurred
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("Process timed out after " + PROCESS_TIMEOUT_MS + "ms");
                        }
                        
                        string output = (await readTask).Trim();
                        
                        // Short timeout for process exit
                        if (!process.WaitForExit(1000))
                        {
                            try { process.Kill(); } catch { }
                        }

                        // Check if a hypervisor is already running
                        if (output.Contains("A hypervisor has been detected"))
                        {
                            return true;
                        }

                        // Check for required virtualization features
                        bool hasVMMonitorModeExtensions = output.Contains("VM Monitor Mode Extensions: Yes");
                        bool hasSecondLevelAddressTranslation = output.Contains("Second Level Address Translation: Yes");

                        return hasVMMonitorModeExtensions && hasSecondLevelAddressTranslation;
                    }
                    catch (TaskCanceledException)
                    {
                        // Handle cancellation
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("Process timed out after " + PROCESS_TIMEOUT_MS + "ms");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error checking CPU virtualization: {ex.Message}");
            return false;
        }
    }
    #endregion

    #region GUI
    private void OnGUI()
    {      
        EditorGUILayout.Space(10);
        
        // Display current status or results
        if (isChecking)
        {
            EditorGUILayout.HelpBox("Checking system compatibility...", MessageType.Info);
        }
        else if (string.IsNullOrEmpty(resultDetails) || resultDetails.StartsWith("Click the button"))
        {
            EditorGUILayout.HelpBox("Click the button below to check WSL2 support on this system.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(isWSL2Supported ? "WSL2 should be supported on this system!" : "WSL2 may not be supported on this system.",
                isWSL2Supported ? MessageType.Info : MessageType.Warning);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detailed Results:", EditorStyles.boldLabel);
            
            // Use a scroll view for potentially long results
            EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(resultDetails, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            
            // Installation buttons
            if (installWSL1Action != null || installWSL2Action != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.BeginHorizontal();
                
                // WSL1 button
                if (installWSL1Action != null && GUILayout.Button("Continue Installing WSL1", GUILayout.Height(30)))
                {
                    installWSL1Action.Invoke();
                    Close(); // Close the window after clicking
                }
                
                // WSL2 button - disabled if not supported
                if (installWSL2Action != null)
                {
                    EditorGUI.BeginDisabledGroup(!isWSL2Supported);
                    if (GUILayout.Button("Continue Installing WSL2", GUILayout.Height(30)))
                    {
                        installWSL2Action.Invoke();
                        Close(); // Close the window after clicking
                    }
                    EditorGUI.EndDisabledGroup();
                }
                
                EditorGUILayout.EndHorizontal();
            }
        }
        
        EditorGUILayout.Space();
        
        // Disable button while checking
        EditorGUI.BeginDisabledGroup(isChecking);
        if (GUILayout.Button(isChecking ? "Checking..." : "Check WSL2 Support"))
        {
            // Start asynchronous check
            CheckSupportAndUpdateUI();
        }
        EditorGUI.EndDisabledGroup();
        
        // Documentation 
        if (GUILayout.Button("Documentation"))
        {
            Application.OpenURL("https://docs.google.com/document/d/1HpGrdNicubKD8ut9UN4AzIOwdlTh1eO4ampZuEk5fM0/edit?usp=sharing");
        }
    }

    /// <summary>
    /// Async void method to handle the button click, perform checks, and update UI.
    /// </summary>
    private async void CheckSupportAndUpdateUI()
    {
        if (isChecking) return; // Prevent concurrent checks

        isChecking = true;
        resultDetails = "Checking system compatibility..."; // Update status immediately
        Repaint(); // Show "Checking..." message

        try
        {
            // Create a cancellation token for the overall operation
            using (var cts = new CancellationTokenSource(30000)) // 30-second timeout
            {
                try
                {
                    // Await the asynchronous check with timeout
                    var checkTask = PerformWSL2SupportCheckAsync();
                    var timeoutTask = Task.Delay(30000, cts.Token);
                    
                    // Wait for either completion or timeout
                    var completedTask = await Task.WhenAny(checkTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        // Timeout occurred
                        resultDetails = "The WSL2 support check timed out after 30 seconds. Please try again.";
                        isWSL2Supported = false;
                    }
                    else
                    {
                        // Check completed successfully
                        (bool supported, string details) = await checkTask;
                        isWSL2Supported = supported;
                        resultDetails = details;
                    }
                }
                catch (TaskCanceledException)
                {
                    // Handle cancellation
                    resultDetails = "The WSL2 support check was cancelled.";
                    isWSL2Supported = false;
                }
            }
        }
        catch (Exception ex)
        {
            resultDetails = $"An error occurred during the check: {ex.Message}";
            isWSL2Supported = false; // Assume not supported on error
            UnityEngine.Debug.LogError($"WSL2 Check Error: {ex}");
        }
        finally
        {
            isChecking = false; // Reset checking flag
            Repaint(); // Update the UI with results or error
        }
    }
    #endregion
} // Class
} // Namespace
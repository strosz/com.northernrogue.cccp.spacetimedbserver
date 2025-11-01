using UnityEngine;
using System.Diagnostics;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using UnityEditor;
using NorthernRogue.CCCP.Editor.Settings;

// Runs the local Docker server processes and methods ///

namespace NorthernRogue.CCCP.Editor {

public class ServerDockerProcess
{
    public static bool debugMode = false;
    
    // Docker container name for SpacetimeDB
    public const string ContainerName = "spacetimedb-server";
    // Official SpacetimeDB Docker image name (as per documentation)
    public const string ImageName = "clockworklabs/spacetime";
    
    // Async Port Check State
    private readonly object statusUpdateLock = new object();
    
    // Server status caching
    private bool cachedServerRunningStatus = false;
    private double lastStatusCacheTime = 0;
    private const double statusCacheTimeout = 10; // Status valid for 10 seconds
    
    // Reference to the server process
    private Process serverProcess;
    
    // Debug logging delegate for verbose output
    private Action<string, int> logCallback;
    
    // Public property to access cached server status
    public bool cachedServerRunningStatus_Public => cachedServerRunningStatus;
    
    public ServerDockerProcess(Action<string, int> logCallback, bool debugMode = false)
    {
        this.logCallback = logCallback;
        ServerDockerProcess.debugMode = debugMode;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Initialized");
    }
    
    #region Installation

    public async Task<bool> RunPowerShellInstallCommand(string command, Action<string, int> statusCallback = null, bool visibleProcess = true, bool keepWindowOpenForDebug = false, bool requiresElevation = false)
    {
        Process process = null;
        try
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Running shell command: {command} | Visible: {visibleProcess} | KeepOpen: {keepWindowOpenForDebug} | RequiresElevation: {requiresElevation}");
            
            process = new Process();
            
            if (visibleProcess)
            {
                // For visible window, use platform-specific shell
                process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.CreateNoWindow = false;
                
                if (ServerUtilityProvider.IsWindows())
                {
                    // Use a temporary batch file on Windows
                    string tempBatchFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cosmoscovecontrolpanel_docker_{DateTime.Now.Ticks}.bat");
                    
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tempBatchFile))
                    {
                        sw.WriteLine("@echo off");
                        sw.WriteLine("echo Running command...");
                        sw.WriteLine(command);
                        sw.WriteLine($"echo Command finished. Exit code is %ERRORLEVEL%.");
                        sw.WriteLine("if %ERRORLEVEL% neq 0 (");
                        sw.WriteLine("    echo ERROR: Command failed with exit code %ERRORLEVEL%");
                        sw.WriteLine(")");
                        
                        if (keepWindowOpenForDebug)
                        {
                            sw.WriteLine("echo Press any key to close this window...");
                            sw.WriteLine("pause > nul");
                        }
                        else
                        {
                            sw.WriteLine("echo Window will close in 5 seconds...");
                            sw.WriteLine("timeout /t 5 /nobreak > nul");
                        }
                        sw.WriteLine("exit /b %ERRORLEVEL%");
                    }
                    
                    process.StartInfo.Arguments = $"/C \"{tempBatchFile}\"";
                    
                    if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Created batch file: {tempBatchFile} with command: {command}");
                }
                else
                {
                    // Unix-like systems (macOS, Linux)
                    process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(command);
                }
            }
            else // Hidden execution
            {
                process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                string commandToExecute = command;
                if (requiresElevation && ServerUtilityProvider.IsWindows())
                {
                    // Elevation only supported on Windows via PowerShell
                    process.StartInfo.FileName = "powershell.exe";
                    var (exe, args) = SplitCommandForProcess(commandToExecute);
                    
                    string escapedExe = exe.Replace("'", "''");
                    string escapedArgs = args.Replace("'", "''");

                    commandToExecute = $"$ProgressPreference = 'SilentlyContinue'; try {{ $process = Start-Process -FilePath '{escapedExe}' -ArgumentList '{escapedArgs}' -Verb RunAs -Wait -PassThru; exit $process.ExitCode; }} catch {{ Write-Error $_; exit 1; }}";
                    if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Elevated command for hidden execution: {commandToExecute}");
                    string escapedFinalCommand = commandToExecute.Replace("\"", "`\"");
                    process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{escapedFinalCommand}\"";
                }
                else
                {
                    // Standard hidden execution for all platforms
                    process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(commandToExecute);
                }
            }
            
            process.Start();
            statusCallback?.Invoke($"Executing: {command.Split(' ')[0]}...", 0);
            
            string outputLog = "";
            string errorLog = "";

            if (!visibleProcess)
            {
                outputLog = await process.StandardOutput.ReadToEndAsync();
                errorLog = await process.StandardError.ReadToEndAsync();
            }

            await Task.Run(() => process.WaitForExit()); 
            int exitCode = process.ExitCode;

            if (debugMode && !visibleProcess) 
            {
                if (!string.IsNullOrEmpty(outputLog)) UnityEngine.Debug.Log($"[ServerDockerProcess] Output: {outputLog}");
                if (!string.IsNullOrEmpty(errorLog)) UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Error: {errorLog}");
            }
            
            if (exitCode == 0)
            {
                statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' completed successfully.", 1);
                return true;
            }
            else
            {
                if (visibleProcess)
                {
                    if (debugMode) statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' window was closed. Check installation status to verify success.", 0);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Visible process exited with code {exitCode} - likely manual window closure");
                    return true;
                }
                else
                {
                    if (debugMode) statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' failed with exit code {exitCode}. Check console/window for details.", -1);
                    if (!string.IsNullOrEmpty(errorLog)) 
                    {
                         UnityEngine.Debug.LogError($"[ServerDockerProcess] Hidden command failed. Error stream: {errorLog}");
                    } 
                    else if (!string.IsNullOrEmpty(outputLog)) 
                    {
                         UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Hidden command failed. Output stream: {outputLog}");
                    }
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            string commandExcerpt = command.Length > 50 ? command.Substring(0, 50) + "..." : command;
            statusCallback?.Invoke($"Error executing command '{commandExcerpt}': {ex.Message}", -1);
            UnityEngine.Debug.LogError($"[ServerDockerProcess] Exception: {ex}");
            return false;
        }
        finally
        {
            if (process != null && !visibleProcess)
            {
                process?.Dispose();
            }
        }
    }
    
    #endregion
    
    #region Process Execution Methods

    public Process StartVisibleServerProcess(string serverDirectory, string unityAssetsDirectory = null)
    {
        try 
        {
            // Get the configured port from settings
            int hostPort = CCCPSettings.Instance.serverPortDocker;
            
            // Check if container already exists and is running
            var (exists, isRunning) = CheckContainerStatus(ContainerName);
            
            if (exists && isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' is already running. Reusing existing container.", 0);
                
                // Just open a window to the existing container
                Process logProcess = new Process();
                logProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                logProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments($"docker logs -f {ContainerName}");
                logProcess.StartInfo.UseShellExecute = true;
                logProcess.Start();
                return logProcess;
            }
            
            if (exists && !isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' exists but is stopped. Starting it...", 0);
                
                // Start existing stopped container
                Process startProcess = new Process();
                startProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                startProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments($"docker start {ContainerName}");
                startProcess.StartInfo.UseShellExecute = false;
                startProcess.StartInfo.CreateNoWindow = true;
                startProcess.Start();
                startProcess.WaitForExit();
                
                // Now show logs
                Process logProcess = new Process();
                logProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                logProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments($"docker logs -f {ContainerName}");
                logProcess.StartInfo.UseShellExecute = true;
                logProcess.Start();
                return logProcess;
            }
            
            // Container doesn't exist, create new one
            Process process = new Process();
            process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            
            // Build volume mounts
            string volumeMounts = $"-v \"{serverDirectory}:/app\"";
            
            // Add persistent volume for SpacetimeDB data
            volumeMounts += " -v spacetimedb-data:/home/spacetime/.local/share/spacetime/data";
            if (debugMode) logCallback("Mounting persistent volume for SpacetimeDB data", 0);
            
            // Add persistent volume for SpacetimeDB authentication config
            volumeMounts += " -v spacetimedb-auth:/home/spacetime/.config/spacetime";
            if (debugMode) logCallback("Mounting persistent volume for SpacetimeDB authentication config", 0);
            
            // Add Unity Assets directory as a volume mount if provided
            if (!string.IsNullOrEmpty(unityAssetsDirectory))
            {
                // Get the Assets root directory (Unity project root contains Assets)
                string assetsRoot = GetUnityProjectRoot(unityAssetsDirectory);
                if (!string.IsNullOrEmpty(assetsRoot))
                {
                    volumeMounts += $" -v \"{assetsRoot}:/unity\"";
                    if (debugMode) logCallback($"Mounting Unity project at /unity: {assetsRoot}", 0);
                }
                else
                {
                    logCallback($"WARNING: Could not determine Unity project root from: {unityAssetsDirectory}. File generation may fail!", -1);
                    logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                    string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                    volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                    if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
                }
            }
            else
            {
                logCallback($"WARNING: Unity Assets directory not provided. File generation will likely fail!", -1);
                logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
            }
            
            // Start Docker container with interactive mode
            // Note: NOT using --rm so container persists and can be stopped/restarted
            // Override entrypoint and use --user root to ensure permissions on the volumes
            string dockerCommand = $"docker run -it --name {ContainerName} -p {hostPort}:3000 --user root --entrypoint /bin/sh {volumeMounts} {ImageName} -c \"chown -R spacetime:spacetime /home/spacetime/.local/share/spacetime/data && chown -R spacetime:spacetime /home/spacetime/.config/spacetime && su spacetime -c 'spacetime start'\"";
            
            process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(dockerCommand);
            process.StartInfo.UseShellExecute = true;
            
            if (debugMode) logCallback($"Starting SpacetimeDB Server on port {hostPort} (Docker Visible Shell)...", 0);
            process.Start();
            serverProcess = process;
            return process;
        }
        catch (Exception ex) 
        {
            logCallback($"Error starting visible Docker server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public Process StartSilentServerProcess(string serverDirectory, string unityAssetsDirectory = null)
    {
        try 
        {
            // Get the configured port from settings
            int hostPort = CCCPSettings.Instance.serverPortDocker;
            
            // Check if container already exists and is running
            var (exists, isRunning) = CheckContainerStatus(ContainerName);
            
            if (exists && isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' is already running. No need to start.", 1);
                return null; // Return null but this is not an error - container is already running
            }
            
            if (exists && !isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' exists but is stopped. Starting it...", 0);
                
                // Start existing stopped container
                Process startProcess = new Process();
                startProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                startProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments($"docker start {ContainerName}");
                startProcess.StartInfo.UseShellExecute = false;
                startProcess.StartInfo.CreateNoWindow = true;
                startProcess.StartInfo.RedirectStandardOutput = true;
                startProcess.StartInfo.RedirectStandardError = true;
                startProcess.Start();
                
                string startOutput = startProcess.StandardOutput.ReadToEnd();
                string startError = startProcess.StandardError.ReadToEnd();
                startProcess.WaitForExit();
                
                if (startProcess.ExitCode == 0)
                {
                    if (debugMode) logCallback($"Existing container started successfully.", 1);
                    return startProcess;
                }
                else
                {
                    logCallback($"Failed to start existing container. Error: {startError}", -1);
                    return null;
                }
            }
            
            // Build volume mounts
            string volumeMounts = $"-v \"{serverDirectory}:/app\"";
            
            // Add persistent volume for SpacetimeDB data
            volumeMounts += " -v spacetimedb-data:/home/spacetime/.local/share/spacetime/data";
            if (debugMode) logCallback("Mounting persistent volume for SpacetimeDB data", 0);
            
            // Add persistent volume for SpacetimeDB authentication config
            volumeMounts += " -v spacetimedb-auth:/home/spacetime/.config/spacetime";
            if (debugMode) logCallback("Mounting persistent volume for SpacetimeDB authentication config", 0);
            
            // Add Unity Assets directory as a volume mount if provided
            if (!string.IsNullOrEmpty(unityAssetsDirectory))
            {
                // Get the Assets root directory (Unity project root contains Assets)
                string assetsRoot = GetUnityProjectRoot(unityAssetsDirectory);
                if (!string.IsNullOrEmpty(assetsRoot))
                {
                    volumeMounts += $" -v \"{assetsRoot}:/unity\"";
                    if (debugMode) logCallback($"Mounting Unity project at /unity: {assetsRoot}", 0);
                }
                else
                {
                    logCallback($"WARNING: Could not determine Unity project root from: {unityAssetsDirectory}. File generation may fail!", -1);
                    logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                    string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                    volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                    if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
                }
            }
            else
            {
                logCallback($"WARNING: Unity Assets directory not provided. File generation will likely fail!", -1);
                logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
            }
            
            // Container doesn't exist, create new one
            // Start Docker container in detached mode
            // Override entrypoint and use --user root to ensure permissions on the volumes
            string dockerCommand = $"docker run -d --name {ContainerName} -p {hostPort}:3000 --user root --entrypoint /bin/sh {volumeMounts} {ImageName} -c \"chown -R spacetime:spacetime /home/spacetime/.local/share/spacetime/data && chown -R spacetime:spacetime /home/spacetime/.config/spacetime && su spacetime -c 'spacetime start'\"";
            
            if (debugMode) logCallback($"Docker command: {dockerCommand}", 0);
            
            Process process = new Process();
            process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable(); 
            process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(dockerCommand);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            if (debugMode) logCallback($"Creating new Docker container on port {hostPort}...", 0);
            process.Start();
            
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode == 0)
            {
                if (debugMode) logCallback($"Docker container started successfully. Container ID: {output.Trim()}", 1);
            }
            else
            {
                logCallback($"Failed to start Docker container. Error: {error}", -1);
                return null;
            }
            
            serverProcess = process;
            return process;
        }
        catch (Exception ex) 
        {
            logCallback($"Error launching silent Docker server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public void OpenDockerWindow()
    {
        try
        {
            // Open Docker Desktop or attach to running container
            Process process = new Process();
            process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments($"docker exec -it {ContainerName} /bin/bash");
            process.StartInfo.UseShellExecute = true;
            process.Start();
        }
        catch (Exception ex)
        {
            logCallback($"Error opening Docker window: {ex.Message}", -1);
        }
    }
    #endregion

    #region Docker Control
    
    public void ShutdownDocker()
    {
        if (debugMode) logCallback("Attempting to shut down Docker...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments("docker-compose down");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            if (debugMode) logCallback("Docker shutdown command issued.", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to shut down Docker: {ex.Message}", -1);
        }
    }

    public void StartDocker()
    {
        if (debugMode) logCallback("Attempting to start Docker...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            process.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments("docker-compose up -d");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            if (debugMode) logCallback("Docker startup command issued.", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to start Docker: {ex.Message}", -1);
        }    
    }

    public void ShutdownDockerDesktop()
    {
        if (debugMode) logCallback("Attempting to shut down Docker Desktop...", 0);
        try
        {
            // Use Docker Desktop's official shutdown command (works on all platforms on Docker Desktop 4.11.0+)
            string quitCommand = "docker desktop stop";

            Process quitProcess = new Process();
            quitProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            quitProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(quitCommand);
            quitProcess.StartInfo.UseShellExecute = false;
            quitProcess.StartInfo.CreateNoWindow = true;
            quitProcess.StartInfo.RedirectStandardOutput = true;
            quitProcess.StartInfo.RedirectStandardError = true;
            quitProcess.Start();
            
            // Don't wait long - just trigger the shutdown and let Docker Desktop handle the rest
            // Docker Desktop will continue its shutdown process (including stopping containers) after Unity quits
            if (!quitProcess.WaitForExit(100)) // Wait 100ms to initiate successfully
            {
                if (debugMode) logCallback("Docker Desktop shutdown initiated (continuing in background).", 1);
            }
            else
            {
                if (debugMode) logCallback("Docker Desktop shutdown command completed.", 1);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to shut down Docker Desktop: {ex.Message}", -1);
        }
    }
    #endregion

    #region CheckServerProcess
    public async Task<bool> CheckDockerProcessAsync(bool isDockerRunning)
    {
        if (!isDockerRunning)
        {
            if (debugMode) logCallback("[ServerDockerProcess] Docker is not running, cannot check server process.", 0);
            return false;
        }

        bool processResult = false;
        string errorMessage = null;
        string debugMessage = null;
        bool timedOut = false;
        int exitCode = 0;
            
        try
        {
            var result = await Task.Run<(bool, string, string, bool, int)>(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"ps --filter name={ContainerName} --format \"{{{{.Names}}}}\"";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        bool finished = process.WaitForExit(5000);
                        if (!finished)
                        {
                            try { process.Kill(); } catch { }
                            return (false, "Docker ps command timed out", null, true, -1);
                        }
                        
                        int code = process.ExitCode;
                        
                        if (code == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            bool found = output.Trim().Contains(ContainerName);
                            return (found, null, found ? $"Container {ContainerName} found" : "Container not found", false, code);
                        }
                        else if (code == 0)
                        {
                            return (false, null, "Container not found (empty output)", false, code);
                        }
                        else
                        {
                            return (false, $"Docker ps failed: {error}", null, false, code);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Exception during Docker ps: {ex.Message}", null, false, -1);
                }
            });

            processResult = result.Item1;
            errorMessage = result.Item2;
            debugMessage = result.Item3;
            timedOut = result.Item4;
            exitCode = result.Item5;
        }
        catch (Exception ex)
        {
            errorMessage = $"Outer exception: {ex.Message}";
        }

        lock (statusUpdateLock)
        {
            cachedServerRunningStatus = processResult;
            lastStatusCacheTime = EditorApplication.timeSinceStartup;
        }

        if (debugMode)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                logCallback($"[ServerDockerProcess] Error checking Docker process: {errorMessage}", -1);
            }
            else if (!string.IsNullOrEmpty(debugMessage))
            {
                logCallback($"[ServerDockerProcess] {debugMessage}", processResult ? 1 : 0);
            }
        }

        return processResult;
    }
    #endregion
    
    #region CheckPrereq

    public void CheckPrerequisites(Action<bool, bool, bool, bool> callback)
    {
        Task.Run(() =>
        {
            bool hasDocker = false;
            bool hasDockerCompose = false;
            bool hasDockerImage = false;
            bool hasDockerContainerMounts = false;

            try
            {
                // Step 1: Check if Docker CLI is installed (platform-agnostic)
                // Use 'docker --version' which works even if daemon isn't running
                using (Process dockerVersionCheck = new Process())
                {
                    dockerVersionCheck.StartInfo.FileName = "docker";
                    dockerVersionCheck.StartInfo.Arguments = "--version";
                    dockerVersionCheck.StartInfo.RedirectStandardOutput = true;
                    dockerVersionCheck.StartInfo.RedirectStandardError = true;
                    dockerVersionCheck.StartInfo.UseShellExecute = false;
                    dockerVersionCheck.StartInfo.CreateNoWindow = true;
                    
                    dockerVersionCheck.Start();
                    string output = dockerVersionCheck.StandardOutput.ReadToEnd();
                    string error = dockerVersionCheck.StandardError.ReadToEnd();
                    bool exited = dockerVersionCheck.WaitForExit(5000);
                    
                    if (exited && dockerVersionCheck.ExitCode == 0 && output.Contains("Docker version"))
                    {
                        hasDocker = true;
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerDockerProcess] Docker is installed: {output.Trim()}");
                        }
                    }
                    else
                    {
                        if (debugMode)
                        {
                            if (!exited)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker version check timed out - Docker may not be installed");
                            }
                            else if (dockerVersionCheck.ExitCode != 0)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker version check failed (exit code {dockerVersionCheck.ExitCode})");
                                if (!string.IsNullOrEmpty(error))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Error: {error.Trim()}");
                                }
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker version check succeeded but output doesn't contain version info: {output}");
                            }
                        }
                    }
                    
                    if (!exited)
                    {
                        try { dockerVersionCheck.Kill(); } catch { }
                    }
                }

                // Step 2: If Docker CLI exists, check if daemon is running
                if (hasDocker)
                {
                    using (Process dockerPingCheck = new Process())
                    {
                        dockerPingCheck.StartInfo.FileName = "docker";
                        dockerPingCheck.StartInfo.Arguments = "info";
                        dockerPingCheck.StartInfo.RedirectStandardOutput = true;
                        dockerPingCheck.StartInfo.RedirectStandardError = true;
                        dockerPingCheck.StartInfo.UseShellExecute = false;
                        dockerPingCheck.StartInfo.CreateNoWindow = true;
                        
                        dockerPingCheck.Start();
                        string output = dockerPingCheck.StandardOutput.ReadToEnd();
                        string error = dockerPingCheck.StandardError.ReadToEnd();
                        bool exited = dockerPingCheck.WaitForExit(5000);
                        
                        if (exited && dockerPingCheck.ExitCode == 0)
                        {
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerDockerProcess] Docker daemon is running");
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                string reason = !exited ? "timeout" : $"exit code {dockerPingCheck.ExitCode}";
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker daemon is not running ({reason})");
                                if (!string.IsNullOrEmpty(error) && error.Contains("Cannot connect"))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker Desktop may not be started. Please start Docker Desktop.");
                                }
                            }
                        }
                        
                        if (!exited)
                        {
                            try { dockerPingCheck.Kill(); } catch { }
                        }
                    }
                }

                // Step 3: Check if Docker Compose is available (only if Docker CLI exists)
                if (hasDocker)
                {
                    // Check if Docker Compose is available
                    using (Process composeCheck = new Process())
                    {
                        composeCheck.StartInfo.FileName = "docker";
                        composeCheck.StartInfo.Arguments = "compose version";
                        composeCheck.StartInfo.RedirectStandardOutput = true;
                        composeCheck.StartInfo.RedirectStandardError = true;
                        composeCheck.StartInfo.UseShellExecute = false;
                        composeCheck.StartInfo.CreateNoWindow = true;
                        
                        composeCheck.Start();
                        string output = composeCheck.StandardOutput.ReadToEnd();
                        string error = composeCheck.StandardError.ReadToEnd();
                        bool exited = composeCheck.WaitForExit(5000);
                        
                        if (exited && composeCheck.ExitCode == 0)
                        {
                            hasDockerCompose = true;
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerDockerProcess] Docker Compose is available: {output.Trim()}");
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker Compose check failed");
                                if (!string.IsNullOrEmpty(error))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Error: {error.Trim()}");
                                }
                            }
                        }
                        
                        if (!exited)
                        {
                            try { composeCheck.Kill(); } catch { }
                        }
                    }

                    // Step 4: Check if SpacetimeDB image exists locally (only if Docker daemon is running)
                    using (Process imageCheck = new Process())
                    {
                        imageCheck.StartInfo.FileName = "docker";
                        imageCheck.StartInfo.Arguments = $"images -q {ImageName}";
                        imageCheck.StartInfo.RedirectStandardOutput = true;
                        imageCheck.StartInfo.RedirectStandardError = true;
                        imageCheck.StartInfo.UseShellExecute = false;
                        imageCheck.StartInfo.CreateNoWindow = true;
                        
                        imageCheck.Start();
                        string output = imageCheck.StandardOutput.ReadToEnd();
                        string error = imageCheck.StandardError.ReadToEnd();
                        bool exited = imageCheck.WaitForExit(5000);
                        
                        if (exited && imageCheck.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            hasDockerImage = true;
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerDockerProcess] SpacetimeDB image found locally");
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                if (string.IsNullOrWhiteSpace(output))
                                {
                                    UnityEngine.Debug.Log($"[ServerDockerProcess] SpacetimeDB image not found locally (can be pulled when needed)");
                                }
                                if (!string.IsNullOrEmpty(error) && error.Contains("Cannot connect"))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Cannot check for images - Docker daemon not running");
                                }
                            }
                        }
                        
                        if (!exited)
                        {
                            try { imageCheck.Kill(); } catch { }
                        }
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // This exception occurs when the executable is not found (Docker not in PATH)
                if (debugMode) 
                {
                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker executable not found. Docker may not be installed or not in system PATH: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                if (debugMode) 
                {
                    UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking prerequisites: {ex.Message}\n{ex.StackTrace}");
                }
            }
            
            // Step 5: Check container mounts if Docker and image are available
            if (hasDocker && hasDockerImage)
            {
                hasDockerContainerMounts = CheckDockerContainerMounts();
            }
            
            if (debugMode) 
            {
                UnityEngine.Debug.Log($"[ServerDockerProcess] Prerequisites check complete - Docker: {hasDocker}, Compose: {hasDockerCompose}, Image: {hasDockerImage}, Container Mounts: {hasDockerContainerMounts}");
            }

            EditorApplication.delayCall += () => callback(hasDocker, hasDockerCompose, hasDockerImage, hasDockerContainerMounts);
        });
    }
    
    /// <summary>
    /// Checks if the Docker container has correct volume mounts for Unity file generation
    /// </summary>
    private bool CheckDockerContainerMounts()
    {
        try
        {
            // Check if container exists
            var (exists, isRunning) = CheckContainerExistsAndRunning();
            
            if (!exists)
            {
                // Container doesn't exist yet - that's okay, it will be created with correct mounts
                return true;
            }
            
            // Inspect the container to check mounts
            using (Process inspectProcess = new Process())
            {
                inspectProcess.StartInfo.FileName = "docker";
                inspectProcess.StartInfo.Arguments = $"inspect {ContainerName}";
                inspectProcess.StartInfo.UseShellExecute = false;
                inspectProcess.StartInfo.RedirectStandardOutput = true;
                inspectProcess.StartInfo.RedirectStandardError = true;
                inspectProcess.StartInfo.CreateNoWindow = true;
                
                inspectProcess.Start();
                string output = inspectProcess.StandardOutput.ReadToEnd();
                inspectProcess.WaitForExit();
                
                if (inspectProcess.ExitCode != 0)
                {
                    return false;
                }
                
                // Check if /unity and /app mounts exist in the output
                bool hasUnityMount = output.Contains("/unity");
                bool hasAppMount = output.Contains("/app");
                
                // For proper configuration, we need at least the Unity mount
                // The /app mount is also desirable but not strictly required for all operations
                bool hasProperMounts = hasUnityMount;
                
                if (debugMode)
                {
                    UnityEngine.Debug.Log($"[ServerDockerProcess] Container mount check - Unity: {hasUnityMount}, App: {hasAppMount}, Proper: {hasProperMounts}");
                }
                
                return hasProperMounts;
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking container mounts: {ex.Message}");
            }
            return false;
        }
    }
    
    /// <summary>
    /// Public wrapper to check if a Docker container exists and if it's running
    /// </summary>
    /// <param name="containerName">Optional container name, defaults to ContainerName constant</param>
    /// <returns>Tuple of (exists, isRunning)</returns>
    public (bool exists, bool isRunning) CheckContainerExistsAndRunning(string containerName = null)
    {
        if (string.IsNullOrEmpty(containerName))
        {
            containerName = ContainerName;
        }
        
        return CheckContainerStatus(containerName);
    }
    #endregion

    #region StopServer
    public async Task<bool> StopServer(string containerName = null)
    {
        string target = containerName ?? ContainerName;
        
        try
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Attempting to stop Docker container: {target}", 0);

            var result = await StopDockerContainer(target);
            
            if (result)
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Successfully stopped container: {target}", 1);
                lock (statusUpdateLock)
                {
                    cachedServerRunningStatus = false;
                }
            }
            else
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Failed to stop container: {target}", -1);
            }

            return result;
        }
        catch (Exception ex)
        {
            logCallback($"[ServerDockerProcess] Exception while stopping server: {ex.Message}", -1);
            return false;
        }
    }
    
    private async Task<bool> StopDockerContainer(string containerName)
    {
        try
        {
            // Invalidate cache immediately
            lock (statusUpdateLock)
            {
                lastStatusCacheTime = 0; // Force fresh check next time
            }
            
            var stopResult = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"stop {containerName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerDockerProcess] Executing: docker stop {containerName}");
                        }
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        bool finished = process.WaitForExit(30000); // 30 second timeout for graceful stop
                        if (!finished)
                        {
                            if (debugMode)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Stop command timed out, forcing kill...");
                            }
                            try { process.Kill(); } catch { }
                            return (false, "Stop command timed out");
                        }
                        
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerDockerProcess] Stop command exit code: {process.ExitCode}");
                            if (!string.IsNullOrEmpty(output)) UnityEngine.Debug.Log($"[ServerDockerProcess] Stop output: {output}");
                            if (!string.IsNullOrEmpty(error)) UnityEngine.Debug.Log($"[ServerDockerProcess] Stop error: {error}");
                        }
                        
                        if (process.ExitCode == 0)
                        {
                            return (true, null);
                        }
                        else
                        {
                            return (false, $"Stop failed with exit code {process.ExitCode}: {error}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (!stopResult.Item1 && debugMode)
            {
                logCallback($"[ServerDockerProcess] Stop container error: {stopResult.Item2}", -1);
            }

            // Update cache to reflect stopped state
            if (stopResult.Item1)
            {
                lock (statusUpdateLock)
                {
                    cachedServerRunningStatus = false;
                    lastStatusCacheTime = EditorApplication.timeSinceStartup;
                }
                
                // Note: We're NOT removing the container so it can be restarted
                // await RemoveDockerContainer(containerName);
            }

            return stopResult.Item1;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception stopping container: {ex.Message}", -1);
            return false;
        }
    }

    private async Task<bool> RemoveDockerContainer(string containerName)
    {
        try
        {
            var removeResult = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"rm {containerName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        process.WaitForExit(5000);
                        
                        return process.ExitCode == 0;
                    }
                }
                catch
                {
                    return false;
                }
            });

            if (debugMode) logCallback($"[ServerDockerProcess] Remove container result: {removeResult}", removeResult ? 1 : 0);
            return removeResult;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception removing container: {ex.Message}", -1);
            return false;
        }
    }
    
    public bool CheckIfServerRunningDocker()
    {
        // Use cached status if available and not expired
        double currentTime = EditorApplication.timeSinceStartup;
        lock (statusUpdateLock)
        {
            if (currentTime - lastStatusCacheTime < statusCacheTimeout)
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Using cached status: {cachedServerRunningStatus}", 0);
                return cachedServerRunningStatus;
            }
        }
        
        // Otherwise perform fresh check using CheckContainerStatus
        try
        {
            var (exists, isRunning) = CheckContainerStatus(ContainerName);
            
            lock (statusUpdateLock)
            {
                cachedServerRunningStatus = exists && isRunning;
                lastStatusCacheTime = currentTime;
            }
            
            if (debugMode)
            {
                logCallback($"[ServerDockerProcess] Container status check - Exists: {exists}, Running: {isRunning}", 0);
            }
            
            return exists && isRunning;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error checking if server running: {ex.Message}", -1);
            
            // Invalidate cache on error
            lock (statusUpdateLock)
            {
                lastStatusCacheTime = 0;
            }
            
            return false;
        }
    }
    
    #endregion

    #region RunDockerCommand

    public int RunDockerCommandSilent(string dockerCommand)
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = dockerCommand;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.Start();
                process.WaitForExit();

                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error running Docker command: {ex.Message}", -1);
            return -1;
        }
    }
    #endregion
    
    #region RunServerCommand

    public async Task<(string output, string error, bool success)> RunServerCommandAsync(string command, string serverDirectory = null, bool isStatusCheck = false)
    {
        try
        {
            // Execute command inside the running container
            string dockerExecCommand = $"exec {ContainerName} {command}";
            
            if (debugMode && !isStatusCheck) 
            {
                logCallback($"[ServerDockerProcess] Running command in container: {command}", 0);
            }

            // Check if this is a login command that needs real-time output
            bool isLoginCommand = command.Contains("spacetime login") && !command.Contains("show --token");

            var result = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = dockerExecCommand;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;

                        var outputBuilder = new StringBuilder();
                        var errorBuilder = new StringBuilder();

                        process.OutputDataReceived += (sender, args) =>
                        {
                            if (args.Data != null)
                            {
                                outputBuilder.AppendLine(args.Data);
                                // For login commands, process output to auto-open URLs and log in real-time
                                if (isLoginCommand && logCallback != null)
                                {
                                    string processedOutput = ServerUtilityProvider.ProcessOutputAndOpenUrls(args.Data);
                                    // Use success log level (1) for successful login messages
                                    int logLevel = processedOutput.Contains("Login successful!") ? 1 : 0;
                                    logCallback(processedOutput, logLevel);
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, args) =>
                        {
                            if (args.Data != null)
                            {
                                errorBuilder.AppendLine(args.Data);
                                // For login commands, also log errors in real-time
                                if (isLoginCommand && logCallback != null)
                                {
                                    logCallback(args.Data, -2);
                                }
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        // Use longer timeout for login commands (3 minutes) since they require user interaction
                        int timeoutMs = isLoginCommand ? 180000 : 30000; // 3 minutes for login, 30 seconds for others
                        bool finished = process.WaitForExit(timeoutMs);
                        if (!finished)
                        {
                            try { process.Kill(); } catch { }
                            if (isLoginCommand)
                                return ("", "Login command timed out. Please try again.", false);
                            else
                                return ("", "Command timed out. Please try again.", false);
                        }

                        string output = outputBuilder.ToString();
                        string error = errorBuilder.ToString();
                        bool success = process.ExitCode == 0;

                        return (output, error, success);
                    }
                }
                catch (Exception ex)
                {
                    return ("", ex.Message, false);
                }
            });

            if (debugMode && !isStatusCheck)
            {
                if (!result.Item3)
                {
                    logCallback($"[ServerDockerProcess] Command failed: {result.Item2}", -1);
                }
                else if (!string.IsNullOrEmpty(result.Item1))
                {
                    logCallback($"[ServerDockerProcess] Command output: {result.Item1}", 1);
                }
            }

            // Analyze the result with error message handling (same as WSL version)
            bool commandSuccess = false;
            bool isPublishCommand = command.Contains("spacetime publish");
            bool isGenerateCommand = command.Contains("spacetime generate");
            bool isLogSizeCommand = command.Contains("du -s") || command.Contains("journalctl") && command.Contains("wc -c");

            if (!string.IsNullOrEmpty(result.Item2) && result.Item2.Contains("Finished"))
            {
                if (isPublishCommand)
                {
                    //logCallback("Successfully published module!", 1);
                    commandSuccess = true;
                }
                else if (isGenerateCommand)
                {
                    //logCallback("Successfully generated files!", 1);
                    commandSuccess = true;
                }
                else
                {
                    logCallback("Command finished successfully!", 1);
                    commandSuccess = true;
                }
            }
            else if (result.Item2.Contains("tar: Removing leading `/' from member names"))
            {
                logCallback("Successfully backed up SpacetimeDB data!", 1);
                commandSuccess = true;
            }
            else if (result.Item2.Contains("command not found") || result.Item2.Contains("not found"))
            {
                logCallback($"Error: The command (likely 'spacetime') was not found in the Docker container. Ensure SpacetimeDB is correctly installed and in the PATH.", -1);
                commandSuccess = false;
            }
            else if (result.Item2.Contains("not detect the language of the module"))
            {
                logCallback("Please ensure Init New Module has run successfully on your selected module before publishing.", 0);
                commandSuccess = false;
                EditorUtility.DisplayDialog("Publish Error", "Please ensure Init New Module has run successfully on your selected module before publishing.", "OK");
            }
            else if (result.Item2.Contains("invalid characters in database name"))
            {
                logCallback("Please ensure your module name is written in lowercase characters.", -1);
                commandSuccess = false;
            }
            else if (isLogSizeCommand && !string.IsNullOrEmpty(result.Item1) && result.Item1.Trim().All(char.IsDigit))
            {
                // Log size commands are successful if they return numeric output
                commandSuccess = true;
                if (debugMode) logCallback($"Log size command successful with output: {result.Item1.Trim()}", 1);
            }
            else if (result.Item3)
            {
                // Command succeeded based on exit code
                commandSuccess = true;
            }
            else if (string.IsNullOrEmpty(result.Item1) && string.IsNullOrEmpty(result.Item2))
            {
                commandSuccess = true;
            }

            return (result.Item1, result.Item2, commandSuccess);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception running server command: {ex.Message}", -1);
            return ("", ex.Message, false);
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Check if a Docker container exists and if it's running
    /// </summary>
    /// <param name="containerName">Name of the container to check</param>
    /// <returns>Tuple of (exists, isRunning)</returns>
    private (bool exists, bool isRunning) CheckContainerStatus(string containerName)
    {
        try
        {
            // Check if container exists (running or stopped)
            using (Process checkProcess = new Process())
            {
                checkProcess.StartInfo.FileName = "docker";
                checkProcess.StartInfo.Arguments = $"ps -a --filter name={containerName} --format {{{{.Names}}}}";
                checkProcess.StartInfo.UseShellExecute = false;
                checkProcess.StartInfo.RedirectStandardOutput = true;
                checkProcess.StartInfo.CreateNoWindow = true;
                
                checkProcess.Start();
                string output = checkProcess.StandardOutput.ReadToEnd().Trim();
                checkProcess.WaitForExit();
                
                bool exists = !string.IsNullOrWhiteSpace(output) && output == containerName;
                
                if (!exists)
                {
                    return (false, false);
                }
                
                // Container exists, now check if it's running
                using (Process runningCheck = new Process())
                {
                    runningCheck.StartInfo.FileName = "docker";
                    runningCheck.StartInfo.Arguments = $"ps --filter name={containerName} --format {{{{.Names}}}}";
                    runningCheck.StartInfo.UseShellExecute = false;
                    runningCheck.StartInfo.RedirectStandardOutput = true;
                    runningCheck.StartInfo.CreateNoWindow = true;
                    
                    runningCheck.Start();
                    string runningOutput = runningCheck.StandardOutput.ReadToEnd().Trim();
                    runningCheck.WaitForExit();
                    
                    bool isRunning = !string.IsNullOrWhiteSpace(runningOutput) && runningOutput == containerName;
                    
                    return (true, isRunning);
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking container status: {ex.Message}");
            }
            return (false, false);
        }
    }
    
    private (string executable, string arguments) SplitCommandForProcess(string command)
    {
        // Split command into executable and arguments for Process.Start

        if (command.StartsWith("\""))
        {
            int closingQuoteIndex = command.IndexOf("\"", 1);
            if (closingQuoteIndex > 0)
            {
                string executable = command.Substring(1, closingQuoteIndex - 1);
                if (command.Length > closingQuoteIndex + 1)
                {
                    return (executable, command.Substring(closingQuoteIndex + 2).Trim());
                }
            }
            else
            {
                return (command.Substring(1).Trim(), "");
            }
        }
        else
        {
            int firstSpaceIndex = command.IndexOf(' ');
            if (firstSpaceIndex > 0)
            {
                return (command.Substring(0, firstSpaceIndex), command.Substring(firstSpaceIndex + 1).Trim());
            }
            else
            {
                return (command.Trim(), "");
            }
        }
        
        return (command, "");
    }
    
    /// <summary>
    /// Gets the Unity project root directory from an Assets subdirectory path
    /// </summary>
    /// <param name="assetsPath">Path to a directory within Assets</param>
    /// <returns>Unity project root directory containing Assets folder, or null if not found</returns>
    private string GetUnityProjectRoot(string assetsPath)
    {
        if (string.IsNullOrEmpty(assetsPath))
            return null;
            
        try
        {
            // Normalize the path
            string normalizedPath = assetsPath.Replace('\\', '/');
            
            if (debugMode) logCallback($"[GetUnityProjectRoot] Input path: {normalizedPath}", 0);
            
            // Find the Assets folder in the path
            int assetsIndex = normalizedPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex < 0)
            {
                // Check if path ends with /Assets
                assetsIndex = normalizedPath.LastIndexOf("/Assets", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0 && assetsIndex + 7 == normalizedPath.Length)
                {
                    // Path ends with /Assets, extract everything before it
                    string projectRoot = normalizedPath.Substring(0, assetsIndex).Replace('/', '\\');
                    if (debugMode) logCallback($"[GetUnityProjectRoot] Found /Assets at end. Project root: {projectRoot}", 0);
                    return projectRoot;
                }
                
                // Try without leading slash
                assetsIndex = normalizedPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex < 0)
                {
                    if (debugMode) logCallback($"[GetUnityProjectRoot] Could not find 'Assets' in path", -1);
                    return null;
                }
                
                if (assetsIndex == 0)
                {
                    // Path starts with Assets/ (relative path)
                    // Get current Unity project directory
                    string projectRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                    if (debugMode) logCallback($"[GetUnityProjectRoot] Path starts with Assets/. Using Unity's dataPath: {projectRoot}", 0);
                    return projectRoot;
                }
            }
            
            // Extract everything before /Assets/ or Assets/
            string result = normalizedPath.Substring(0, assetsIndex).Replace('/', '\\');
            
            // Handle case where path starts with Assets (relative path)
            if (string.IsNullOrEmpty(result))
            {
                // Get current Unity project directory
                result = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
            }
            
            if (debugMode) logCallback($"[GetUnityProjectRoot] Final project root: {result}", 0);
            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error getting Unity project root: {ex.Message}", -1);
            return null;
        }
    }

    public void PingServer(string serverUrl, Action<bool, string> callback)
    {
        try
        {
            // Run the ping command asynchronously inside the Docker container
            Task.Run(async () =>
            {
                try
                {
                    var result = await RunServerCommandAsync($"spacetime server ping {serverUrl}", null, true);
                    
                    // Combine output and error for parsing
                    string fullOutput = result.output + result.error;
                    
                    // Check if server is online by looking for "Server is online" in the output
                    bool isOnline = fullOutput.Contains("Server is online");
                    
                    // Prepare a clean message for display
                    string message = fullOutput;
                    
                    // Remove the "WARNING: This command is UNSTABLE" line if present
                    if (message.Contains("WARNING: This command is UNSTABLE"))
                    {
                        int warningIndex = message.IndexOf("WARNING:");
                        int newlineIndex = message.IndexOf('\n', warningIndex);
                        if (newlineIndex > warningIndex)
                        {
                            message = message.Remove(warningIndex, newlineIndex + 1 - warningIndex);
                        }
                    }
                    
                    // Trim and clean up the message
                    message = message.Trim();
                    
                    // Use EditorApplication.delayCall to ensure callback is called on main thread
                    EditorApplication.delayCall += () => callback(isOnline, message);
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerDockerProcess] Error pinging server: {ex.Message}");
                    EditorApplication.delayCall += () => callback(false, $"Error pinging server: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerDockerProcess] Error starting ping task: {ex.Message}");
            callback(false, $"Error pinging server: {ex.Message}");
        }
    }
    
    #endregion

    #region Service Management
    
    public async Task<bool> IsDockerServiceRunning()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = "version";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(5000);
                        
                        // Docker is running if we can get version info and exit code is 0
                        // Also check for "Server:" section which confirms Docker daemon is running
                        bool hasServerInfo = output.Contains("Server:") || output.Contains("Server Version:");
                        return process.ExitCode == 0 && hasServerInfo;
                    }
                }
                catch
                {
                    return false;
                }
            });

            if (debugMode && result) logCallback("[ServerDockerProcess] Docker service is running", 0);
            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error checking Docker service: {ex.Message}", -1);
            return false;
        }
    }

    public async Task<bool> StartDockerService()
    {
        try
        {
            // On Windows, try to start Docker Desktop
            if (debugMode) logCallback("[ServerDockerProcess] Attempting to start Docker Desktop...", 0);
            
            var result = await Task.Run(() =>
            {
                try
                {
                    // Try to start Docker Desktop executable
                    string dockerDesktopPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Docker", "Docker", "Docker Desktop.exe"
                    );

                    if (System.IO.File.Exists(dockerDesktopPath))
                    {
                        Process.Start(dockerDesktopPath);
                        return true;
                    }
                    else
                    {
                        if (debugMode) 
                        {
                            logCallback($"[ServerDockerProcess] Docker Desktop not found at: {dockerDesktopPath}", -1);
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (debugMode) 
                    {
                        logCallback($"[ServerDockerProcess] Error starting Docker Desktop: {ex.Message}", -1);
                    }
                    return false;
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception starting Docker service: {ex.Message}", -1);
            return false;
        }
    }

    /// <summary>
    /// Waits for Docker service to become ready after starting Docker Desktop
    /// </summary>
    /// <param name="maxWaitSeconds">Maximum time to wait in seconds</param>
    /// <returns>True if Docker became ready, false if timeout</returns>
    public async Task<bool> WaitForDockerServiceReady(int maxWaitSeconds = 60)
    {
        if (debugMode) logCallback($"[ServerDockerProcess] Waiting for Docker Desktop to become ready (max {maxWaitSeconds}s)...", 0);
        
        int checkIntervalMs = 2000; // Check every 2 seconds
        int checksPerformed = 0;
        int maxChecks = (maxWaitSeconds * 1000) / checkIntervalMs;
        
        while (checksPerformed < maxChecks)
        {
            bool isReady = await IsDockerServiceRunning();
            
            if (isReady)
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Docker Desktop is ready after {checksPerformed * 2} seconds", 1);
                return true;
            }
            
            checksPerformed++;
            
            // Log progress every 10 seconds
            if (checksPerformed % 5 == 0)
            {
                int secondsWaited = checksPerformed * 2;
                logCallback($"Waiting for Docker Desktop to start... ({secondsWaited}s elapsed)", 0);
            }
            
            await Task.Delay(checkIntervalMs);
        }

        logCallback($"[ServerDockerProcess] Docker Desktop did not become ready within {maxWaitSeconds} seconds", -1);
        return false;
    }

    #endregion

    #region Docker Log Management

    public async Task<string> GetDockerLogs(int tailLines = 100)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"logs --tail {tailLines} {ContainerName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                        process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit(5000);
                        
                        if (process.ExitCode == 0)
                        {
                            return output + error; // Docker logs can output to both streams
                        }
                        else
                        {
                            return $"Error getting logs: {error}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    return $"Exception getting logs: {ex.Message}";
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error getting Docker logs: {ex.Message}", -1);
            return "";
        }
    }

    public async Task<bool> ClearDockerLogs()
    {
        // Docker doesn't have a built-in clear logs command
        // We can restart the container to clear logs
        if (debugMode) logCallback("[ServerDockerProcess] Clearing logs by restarting container...", 0);
        
        bool stopped = await StopServer();
        if (!stopped)
        {
            return false;
        }

        // Container will be recreated on next start with fresh logs
        return true;
    }

    #endregion

    #region Version Checking
    
    public async Task<(string version, string toolVersion, string latestVersion, bool updateAvailable)> CheckSpacetimeDBVersionDocker(bool hasDocker, bool hasDockerImage, string serverDirectory)
    {
        if (debugMode) logCallback("Checking SpacetimeDB version in Docker...", 0);
        
        // Only proceed if enough prerequisites are met
        if (!hasDocker || !hasDockerImage)
        {
            if (debugMode) logCallback("Skipping SpacetimeDB version check - Docker prerequisites not met", 0);
            return ("", "", "", false);
        }
        
        // Use RunServerCommandAsync to run the spacetime --version command inside the container (mark as status check for silent mode)
        var result = await RunServerCommandAsync("spacetime --version", serverDirectory, true);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) logCallback("Failed to get SpacetimeDB version from Docker", -1);
            return ("", "", "", false);
        }
        
        // Parse the version from output that looks like:
        // "spacetime Path: /root/.local/share/spacetime/bin/1.3.1/spacetimedb-cli
        // Commit: 
        // spacetimedb tool version 1.3.0; spacetimedb-lib version 1.3.0;"
        // Prefer the version from the path (1.3.1) over the tool version (1.3.0)
        string version = "";
        string toolversion = "";

        // First try to extract version from the path (preferred method)
        System.Text.RegularExpressions.Match pathMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"Path:\s+[^\r\n]*?/bin/([0-9]+\.[0-9]+\.[0-9]+)/");

        if (pathMatch.Success && pathMatch.Groups.Count > 1)
        {
            version = pathMatch.Groups[1].Value;
            if (debugMode) logCallback($"Detected SpacetimeDB version from path: {version}", 1);
        }
        else
        {
            // Fallback to tool version if path version not found
            System.Text.RegularExpressions.Match fallbackToolMatch = 
                System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

            if (fallbackToolMatch.Success && fallbackToolMatch.Groups.Count > 1)
            {
                version = fallbackToolMatch.Groups[1].Value;
                if (debugMode) logCallback($"Detected SpacetimeDB version from tool output: {version}", 1);
            }
        }

        // Also save the tool version for cargo.toml version update in Setup Window
        System.Text.RegularExpressions.Match toolMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

        if (toolMatch.Success && toolMatch.Groups.Count > 1)
        {
            toolversion = toolMatch.Groups[1].Value;
            if (debugMode) logCallback($"Detected SpacetimeDB tool version from output: {toolversion}", 1);
        }

        if (!string.IsNullOrEmpty(version))
        {
            // Check if update is available by comparing with the latest version
            string latestVersion = CCCPSettingsAdapter.GetSpacetimeDBLatestVersion();
            bool updateAvailable = !string.IsNullOrEmpty(latestVersion) && version != latestVersion;
            
            return (version, toolversion, latestVersion, updateAvailable);
        }
        else
        {
            if (debugMode) logCallback("Could not parse SpacetimeDB version from Docker output", -1);
            return ("", "", "", false);
        }
    }
    
    public async Task<(string rustVersion, string rustLatestVersion, bool rustUpdateAvailable, string rustupVersion, bool rustupUpdateAvailable)> CheckRustVersionDocker(bool hasDocker, bool hasDockerImage, string serverDirectory)
    {
        if (debugMode) logCallback("Checking Rust version in Docker...", 0);
        
        // Only proceed if enough prerequisites are met
        if (!hasDocker || !hasDockerImage)
        {
            if (debugMode) logCallback("Skipping Rust version check - Docker prerequisites not met", 0);
            return ("", "", false, "", false);
        }
        
        // Use RunServerCommandAsync to run the rustup check command inside the container (mark as status check for silent mode)
        var result = await RunServerCommandAsync("rustup check", serverDirectory, true);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) logCallback("Failed to get Rust version information from Docker", -1);
            return ("", "", false, "", false);
        }
        
        if (debugMode) logCallback($"Rust check output: {result.output}", 0);
        
        // Parse the version from output that looks like:
        // "stable-x86_64-unknown-linux-gnu - Up to date : 1.89.0 (29483883e 2025-08-04)
        // rustup - Up to date : 1.28.2"
        // Or when updates are available:
        // "stable-x86_64-unknown-linux-gnu - Update available : 1.89.0 (29483883e 2025-08-04) -> 1.90.0 (5bc8c42bb 2025-09-04)
        // rustup - Update available : 1.28.2 -> 1.29.0"
        
        string rustStableVersion = "";
        string rustLatestVersion = "";
        string rustupCurrentVersion = "";
        bool rustUpdateAvailable = false;
        bool rustupUpdateAvailable = false;
        
        // Parse Rust stable version
        System.Text.RegularExpressions.Match rustMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"stable-x86_64-unknown-linux-gnu.*?:\s*([0-9]+\.[0-9]+\.[0-9]+)");
        
        if (rustMatch.Success && rustMatch.Groups.Count > 1)
        {
            rustStableVersion = rustMatch.Groups[1].Value;
            
            // Check if update is available for Rust and extract latest version
            if (result.output.Contains("stable-x86_64-unknown-linux-gnu - Update available"))
            {
                rustUpdateAvailable = true;
                
                // Try to extract the latest version from "1.89.0 -> 1.90.0" format
                System.Text.RegularExpressions.Match latestMatch = 
                    System.Text.RegularExpressions.Regex.Match(result.output, @"stable-x86_64-unknown-linux-gnu.*?->\s*([0-9]+\.[0-9]+\.[0-9]+)");
                
                if (latestMatch.Success && latestMatch.Groups.Count > 1)
                {
                    rustLatestVersion = latestMatch.Groups[1].Value;
                    if (debugMode) logCallback($"Rust update available from version: {rustStableVersion} to {rustLatestVersion}", 1);
                }
                else
                {
                    if (debugMode) logCallback($"Rust update available from version: {rustStableVersion}", 1);
                }
            }
            else if (result.output.Contains("stable-x86_64-unknown-linux-gnu - Up to date"))
            {
                // When up to date, latest version is the same as current version
                rustLatestVersion = rustStableVersion;
                if (debugMode) logCallback($"Rust is up to date at version: {rustStableVersion}", 1);
            }
        }
        
        // Parse rustup version
        System.Text.RegularExpressions.Match rustupMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"rustup.*?:\s*([0-9]+\.[0-9]+\.[0-9]+)");
        
        if (rustupMatch.Success && rustupMatch.Groups.Count > 1)
        {
            rustupCurrentVersion = rustupMatch.Groups[1].Value;
            
            // Check if update is available for rustup
            if (result.output.Contains("rustup - Update available"))
            {
                rustupUpdateAvailable = true;
                if (debugMode) logCallback($"Rustup update available from version: {rustupCurrentVersion}", 1);
            }
            else if (result.output.Contains("rustup - Up to date"))
            {
                if (debugMode) logCallback($"Rustup is up to date at version: {rustupCurrentVersion}", 1);
            }
        }
        
        if (string.IsNullOrEmpty(rustStableVersion) && string.IsNullOrEmpty(rustupCurrentVersion))
        {
            if (debugMode) logCallback("Could not parse Rust version information from Docker output", -1);
        }
        
        return (rustStableVersion, rustLatestVersion, rustUpdateAvailable, rustupCurrentVersion, rustupUpdateAvailable);
    }
    
    /// <summary>
    /// Fetches the latest Docker image tag for SpacetimeDB from the registry
    /// This compares the current running image tag with the latest available tag
    /// </summary>
    public async Task<(string currentTag, string latestTag, bool updateAvailable)> GetLatestImageTag()
    {
        if (debugMode) logCallback("Checking for latest SpacetimeDB Docker image tag...", 0);
        
        try
        {
            // Get the current image tag by querying docker images on the host
            string currentTag = "";
            
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c docker images {ImageName}";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                string output = await Task.Run(() => process.StandardOutput.ReadToEnd());
                bool exited = await Task.Run(() => process.WaitForExit(10000)); // 10 second timeout
                
                if (exited && process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    // Parse the output to find the tag
                    // Expected format:
                    // REPOSITORY                TAG       IMAGE ID       CREATED        SIZE
                    // clockworklabs/spacetime   v1.6.0    236587e83648   10 hours ago   2.49GB
                    
                    string[] lines = output.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
                    
                    // Skip header line and find the first row with our image
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        
                        // Split by whitespace to get columns
                        string[] columns = System.Text.RegularExpressions.Regex.Split(line, @"\s+");
                        
                        // Format: REPOSITORY TAG IMAGE_ID CREATED SIZE
                        // We want the second column (index 1) which is the TAG
                        if (columns.Length >= 2)
                        {
                            currentTag = columns[1]; // Get the TAG column
                            if (debugMode) logCallback($"Current SpacetimeDB Docker image tag: {currentTag}", 0);
                            break;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(currentTag))
                    {
                        if (debugMode) logCallback("Could not parse Docker image tag from 'docker images' output", -1);
                    }
                }
                else if (!exited)
                {
                    process.Kill();
                    if (debugMode) logCallback("docker images query timed out", -1);
                }
                else if (process.ExitCode != 0)
                {
                    if (debugMode) logCallback($"docker images query failed with exit code {process.ExitCode}", -1);
                }
            }
            
            // Get the latest image tag from Docker Hub
            string latestTag = await GetLatestImageTagFromRegistry();
            
            if (!string.IsNullOrEmpty(latestTag))
            {
                // If current tag is "latest", resolve it to the actual version number
                // This prevents false update notifications on first-time pulls with "latest" tag
                if (currentTag == "latest")
                {
                    if (debugMode) logCallback("Current tag is 'latest', resolving to actual version number...", 0);
                    string resolvedTag = await ResolveLatestTagToVersion(latestTag);
                    if (!string.IsNullOrEmpty(resolvedTag))
                    {
                        currentTag = resolvedTag;
                        if (debugMode) logCallback($"Resolved 'latest' tag to: {resolvedTag}", 0);
                    }
                }
                
                bool updateAvailable = currentTag != latestTag && !string.IsNullOrEmpty(currentTag);
                if (debugMode) logCallback($"Latest SpacetimeDB Docker image tag: {latestTag}, update available: {updateAvailable}", 0);
                return (currentTag, latestTag, updateAvailable);
            }
            else
            {
                if (debugMode) logCallback("Could not determine latest SpacetimeDB Docker image tag", -1);
                return (currentTag, "", false);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error checking for latest Docker image tag: {ex.Message}", -1);
            return ("", "", false);
        }
    }
    
    /// <summary>
    /// Resolves the "latest" tag to its actual version number by comparing image digests
    /// or by using the provided latest version if they match
    /// </summary>
    private async Task<string> ResolveLatestTagToVersion(string latestVersionTag)
    {
        try
        {
            if (debugMode) logCallback($"Resolving 'latest' tag to version {latestVersionTag}...", 0);
            
            // Get the image ID of the currently running image with "latest" tag
            string latestImageId = await GetDockerImageId($"{ImageName}:latest");
            if (string.IsNullOrEmpty(latestImageId))
            {
                if (debugMode) logCallback("Could not get image ID for 'latest' tag", -1);
                return "";
            }
            
            // Get the image ID of the latest version tag from registry
            string latestVersionImageId = await GetDockerImageId($"{ImageName}:{latestVersionTag}");
            if (string.IsNullOrEmpty(latestVersionImageId))
            {
                // If we can't get the image ID for the version tag, assume they're the same
                // This happens when the version tag image hasn't been pulled yet
                if (debugMode) logCallback($"Assuming 'latest' matches version {latestVersionTag}", 0);
                return latestVersionTag;
            }
            
            // If the image IDs match, they're the same image
            if (latestImageId == latestVersionImageId)
            {
                if (debugMode) logCallback($"'latest' tag matches version {latestVersionTag}", 0);
                return latestVersionTag;
            }
            else
            {
                if (debugMode) logCallback($"'latest' tag does not match version {latestVersionTag} (different images)", -1);
                return "";
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error resolving latest tag: {ex.Message}", -1);
            return "";
        }
    }
    
    /// <summary>
    /// Gets the image ID of a Docker image by tag
    /// Returns empty string if image not found or error occurs
    /// Multiplatform compatible (Windows, Mac, Linux)
    /// </summary>
    private async Task<string> GetDockerImageId(string imageTag)
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = $"inspect -f \"{{{{.ID}}}}\" {imageTag}";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                string output = await Task.Run(() => process.StandardOutput.ReadToEnd());
                bool exited = await Task.Run(() => process.WaitForExit(5000)); // 5 second timeout
                
                if (exited && process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    return output.Trim();
                }
                
                if (!exited)
                    process.Kill();
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error getting Docker image ID for {imageTag}: {ex.Message}", -1);
        }
        
        return "";
    }
    
    /// <summary>
    /// Fetches the latest version tag from Docker registry by querying metadata
    /// Gets the first tag matching a version number pattern (v1.x.x) since Docker Hub's "latest" is just a label
    /// </summary>
    private async Task<string> GetLatestImageTagFromRegistry()
    {
        try
        {
            // Use PowerShell to query Docker Hub API for tags
            // We get multiple tags and filter for the first one that looks like a version number (contains dot)
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = "-Command \"" +
                    "$response = Invoke-RestMethod -Uri 'https://registry.hub.docker.com/v2/repositories/clockworklabs/spacetime/tags?page_size=100' -ErrorAction SilentlyContinue; " +
                    "if ($response -and $response.results) { " +
                        // Find the first tag that looks like a version number (vX.Y.Z or X.Y.Z)
                        "$versionTag = $response.results | Where-Object { $_.name -match '^v?[0-9]+\\.[0-9]+' -and $_.name -ne 'latest' } | Select-Object -First 1; " +
                        "if ($versionTag) { " +
                            "Write-Host $versionTag.name; " +
                        "} else { " +
                            "Write-Host 'Error'; " +
                        "} " +
                    "} else { " +
                        "Write-Host 'Error'; " +
                    "} " +
                    "\"";
                
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                string output = await Task.Run(() => process.StandardOutput.ReadToEnd());
                bool exited = await Task.Run(() => process.WaitForExit(10000));
                
                if (exited && process.ExitCode == 0)
                {
                    string latestTag = output.Trim();
                    if (!latestTag.Contains("Error") && !string.IsNullOrEmpty(latestTag))
                    {
                        if (debugMode) logCallback($"Retrieved latest version tag from registry: {latestTag}", 0);
                        return latestTag;
                    }
                }
                else if (!exited)
                {
                    process.Kill();
                    if (debugMode) logCallback("Docker Hub registry query timed out", -1);
                }
                else if (process.ExitCode != 0)
                {
                    if (debugMode) logCallback($"Docker Hub registry query failed with exit code {process.ExitCode}", -1);
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error querying Docker Hub registry: {ex.Message}", -1);
        }
        
        return "";
    }
    
    /// <summary>
    /// Updates the SpacetimeDB Docker image by pulling the latest version
    /// Stops the container, pulls the new image with the specified tag, removes the old container,
    /// and prepares for restart with the new image
    /// </summary>
    /// <param name="latestTag">The version tag to pull (e.g., "v1.6.0")</param>
    public async Task<bool> UpdateDockerImage(string latestTag = null)
    {
        if (debugMode) logCallback("Starting Docker image update process...", 0);
        
        try
        {
            // Step 1: Stop the current server container (if running)
            if (debugMode) logCallback("Step 1: Stopping the server container...", 0);
            bool stopped = await StopServer(ContainerName);
            if (!stopped && debugMode)
            {
                logCallback("Note: Server may already be stopped", 0);
            }
            
            // Step 2: Remove the old container so it will be recreated with the new image
            // This ensures the new image is used, not the old one
            if (debugMode) logCallback("Step 2: Removing old container to force recreation...", 0);
            bool removed = await RemoveDockerContainer(ContainerName);
            if (!removed && debugMode)
            {
                logCallback("Note: Container may not exist or couldn't be removed", 0);
            }
            
            // Step 3: Pull the latest image from Docker Hub using the actual version tag
            // The docker pull command must be run on the host OS, not inside the container
            if (debugMode) logCallback("Step 3: Pulling latest Docker image...", 0);
            
            // Use the provided tag, or just the image name if no tag provided
            string pullCommand = string.IsNullOrEmpty(latestTag) 
                ? ImageName 
                : $"{ImageName}:{latestTag}";
            
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c docker pull {pullCommand}";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                string output = await Task.Run(() => process.StandardOutput.ReadToEnd());
                string error = await Task.Run(() => process.StandardError.ReadToEnd());
                bool exited = await Task.Run(() => process.WaitForExit(60000)); // 60 second timeout
                
                if (!exited)
                {
                    process.Kill();
                    logCallback("Failed to pull Docker image: Operation timed out", -1);
                    if (debugMode) logCallback($"Timeout error: {error}", -1);
                    return false;
                }
                
                if (process.ExitCode != 0)
                {
                    logCallback($"Failed to pull Docker image: {error}", -1);
                    if (debugMode) logCallback($"Pull output: {output}", -1);
                    return false;
                }
                
                if (debugMode) logCallback($"Docker image pull output: {output}", 0);
            }
            
            if (debugMode) logCallback($"Docker image updated successfully to {pullCommand}", 1);
            return true;
        }
        catch (Exception ex)
        {
            logCallback($"Error updating Docker image: {ex.Message}", -1);
            if (debugMode) logCallback($"Stack trace: {ex.StackTrace}", -1);
            return false;
        }
    }
    
    #endregion

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games

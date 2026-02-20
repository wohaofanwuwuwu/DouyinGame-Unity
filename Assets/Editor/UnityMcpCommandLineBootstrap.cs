#if UNITY_EDITOR
using System;
using System.IO;
using System.Diagnostics;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

public static class UnityMcpCommandLineBootstrap
{
    const int HttpPort = 9080;
    const string BaseUrl = "http://localhost:9080";
    /// <summary>登记后等待多少秒再启动 MCP，避免卡在 "Finish Loading Project" 或启动界面</summary>
    const double WaitSecondsBeforeStart = 10.0;
    /// <summary>HTTP 进程启动后再等多少秒建 Session</summary>
    const double WaitSecondsBeforeBridge = 3.0;

    // 不再用 [InitializeOnLoadMethod]，避免加载阶段执行任何代码导致卡在启动界面。
    // 需要自动启动时请用命令行带 -executeMethod；或打开项目后菜单 Window > MCP for Unity 里点 Start Server。

    /// <summary>
    /// 供命令行 -executeMethod 调用。只登记延迟启动并立即返回，约 10 秒后再启动 MCP，不阻塞主线程。
    /// </summary>
    public static void StartUnityMcpServerListening()
    {
        EditorApplication.delayCall += ScheduleMCPStart;
    }

    /// <summary>
    /// 只做一件事：登记 update 等待，不在此处调用任何 MCP/Server/Bridge，避免卡住加载。
    /// </summary>
    static void ScheduleMCPStart()
    {
        EditorApplication.delayCall -= ScheduleMCPStart;
        double startTime = EditorApplication.timeSinceStartup;
        void OnUpdate()
        {
            if (EditorApplication.timeSinceStartup - startTime < WaitSecondsBeforeStart)
                return;
            EditorApplication.update -= OnUpdate;
            DoStartMCP();
        }
        EditorApplication.update += OnUpdate;
    }

    static void DoStartMCP()
    {
        var config = EditorConfigurationCache.Instance;
        config.SetUseHttpTransport(true);
        config.SetHttpTransportScope("local");
        HttpEndpointUtility.SaveLocalBaseUrl(BaseUrl);

        bool serverStarted = StartLocalHttpServerHeadless();
        if (!serverStarted && !MCPServiceLocator.Server.IsLocalHttpServerReachable())
        {
            UnityEngine.Debug.LogError("Failed to start Unity MCP server at " + BaseUrl);
            return;
        }

        double bridgeStartTime = EditorApplication.timeSinceStartup;
        void OnUpdateBridge()
        {
            if (EditorApplication.timeSinceStartup - bridgeStartTime < WaitSecondsBeforeBridge)
                return;
            EditorApplication.update -= OnUpdateBridge;

            bool bridgeStarted = MCPServiceLocator.Bridge.StartAsync().GetAwaiter().GetResult();
            if (!bridgeStarted)
                UnityEngine.Debug.LogWarning("MCP HTTP server is up but Bridge (session) failed to start. Cursor may show 'Unity session not available'.");
            else
                UnityEngine.Debug.Log("Unity MCP server and session started at " + BaseUrl + "/mcp (no need to click Start Session).");
        }
        EditorApplication.update += OnUpdateBridge;
    }

    static bool StartLocalHttpServerHeadless()
    {
        if (!MCPServiceLocator.Server.TryGetLocalHttpServerCommand(out string displayCommand, out string error))
        {
            UnityEngine.Debug.LogError("MCP server command not available: " + (error ?? "unknown"));
            return false;
        }

        string projectDir = Path.GetDirectoryName(Application.dataPath);
        string pidDir = Path.Combine(projectDir, "Library", "MCPForUnity", "RunState");
        string pidFile = Path.Combine(pidDir, "mcp_http_" + HttpPort + ".pid");
        string instanceToken = Guid.NewGuid().ToString("N");
        string quotedPid = "\"" + pidFile.Replace("\"", "\\\"") + "\"";
        string fullCommand = displayCommand + " --pidfile " + quotedPid + " --unity-instance-token " + instanceToken;

        try
        {
            if (!Directory.Exists(pidDir))
                Directory.CreateDirectory(pidDir);

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = projectDir
            };

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/c \"" + fullCommand.Replace("\"", "\\\"") + "\"";
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.Arguments = "-c " + "\"" + fullCommand.Replace("\"", "\\\"") + "\"";
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            return false;
        }
    }
}
#endif


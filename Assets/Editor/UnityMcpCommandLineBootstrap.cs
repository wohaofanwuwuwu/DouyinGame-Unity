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

    public static void StartUnityMcpServerListening()
    {
        var config = EditorConfigurationCache.Instance;
        config.SetUseHttpTransport(true);
        config.SetHttpTransportScope("local");
        HttpEndpointUtility.SaveLocalBaseUrl(BaseUrl);

        bool serverStarted = false;

        if (Application.isBatchMode)
        {
            // 命令行/无界面模式：不弹窗，直接启动 HTTP 进程并建立 Bridge Session
            serverStarted = StartLocalHttpServerHeadless();
        }
        else
        {
            serverStarted = MCPServiceLocator.Server.StartLocalHttpServer();
        }

        // 若本次未成功启动进程，仍检查端口是否已被占用（例如之前已启动），是则继续做 Start Session
        if (!serverStarted && !MCPServiceLocator.Server.IsLocalHttpServerReachable())
        {
            UnityEngine.Debug.LogError("Failed to start Unity MCP server at " + BaseUrl);
            throw new Exception("Unity MCP server start failed.");
        }

        // 等待 HTTP 服务就绪后再建立 Session，避免 Bridge 连不上
        if (serverStarted)
            System.Threading.Thread.Sleep(2000);

        // 必须启动 Bridge（= 点击 Start Session），否则 MCP 客户端连上 9080 也看不到 Unity 实例
        bool bridgeStarted = MCPServiceLocator.Bridge.StartAsync().GetAwaiter().GetResult();
        if (!bridgeStarted)
        {
            UnityEngine.Debug.LogWarning("MCP HTTP server is up but Bridge (session) failed to start. Cursor may show 'Unity session not available'.");
        }
        else
        {
            UnityEngine.Debug.Log("Unity MCP server and session started at " + BaseUrl + "/mcp (no need to click Start Session).");
        }
    }

    /// <summary>
    /// 无界面模式下启动本地 HTTP 服务（不弹 DisplayDialog，直接起进程）
    /// </summary>
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
            System.Threading.Thread.Sleep(1500);
            return MCPServiceLocator.Server.IsLocalHttpServerReachable();
        }
        catch (Exception ex)
        {
            UnityEngine.UnityEngine.Debug.LogException(ex);
            return false;
        }
    }
}
#endif


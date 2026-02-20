#if UNITY_EDITOR
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

public static class UnityMcpCommandLineBootstrap
{
    public static void StartUnityMcpServerListening()
    {
        var config = EditorConfigurationCache.Instance;

        config.SetUseHttpTransport(true);
        config.SetHttpTransportScope("local");
        HttpEndpointUtility.SaveLocalBaseUrl("http://localhost:9080");

        bool started = MCPServiceLocator.Server.StartLocalHttpServer();
        if (!started)
        {
            Debug.LogError("Failed to start Unity MCP server at http://localhost:9080.");
            throw new System.Exception("Unity MCP server start failed.");
        }

        Debug.Log("Unity MCP server started at http://localhost:9080/mcp");
    }
}
#endif


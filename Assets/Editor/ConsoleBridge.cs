#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

[InitializeOnLoad]
public static class ConsoleBridge
{
    private const int MaxQueueSize = 300;
    private const int MaxBatchSize = 40;
    private const int MaxLocalBytes = 1024 * 1024;
    private static readonly Queue<ConsoleEntry> Pending = new Queue<ConsoleEntry>();
    private static readonly object FileLock = new object();
    private static double _nextFlushAt;
    private static bool _sending;

    private static string EndpointUrl
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("UNITY_CONSOLE_BRIDGE_URL");
            if (!string.IsNullOrEmpty(fromEnv))
            {
                return fromEnv.Trim();
            }
            return "http://127.0.0.1:9000/unity-console-report";
        }
    }

    private static string LocalLogFilePath
    {
        get
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    return string.Empty;
                }
                return Path.Combine(projectRoot, "ProjectSettings", "AutoCraftConsoleErrors.jsonl");
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    static ConsoleBridge()
    {
        Application.logMessageReceivedThreaded += OnLogReceived;
        EditorApplication.update += OnEditorUpdate;
        _nextFlushAt = EditorApplication.timeSinceStartup + 1.0d;
    }

    private static bool IsErrorType(LogType type)
    {
        return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
    }

    private static void OnLogReceived(string message, string stackTrace, LogType type)
    {
        if (!IsErrorType(type))
        {
            return;
        }
        var entry = new ConsoleEntry
        {
            message = message ?? string.Empty,
            stackTrace = stackTrace ?? string.Empty,
            type = type.ToString(),
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        lock (Pending)
        {
            Pending.Enqueue(entry);

            while (Pending.Count > MaxQueueSize)
            {
                Pending.Dequeue();
            }
        }
        AppendToLocalFile(entry);
    }

    private static void OnEditorUpdate()
    {
        if (_sending)
        {
            return;
        }
        if (EditorApplication.timeSinceStartup < _nextFlushAt)
        {
            return;
        }

        List<ConsoleEntry> batch = null;
        lock (Pending)
        {
            if (Pending.Count == 0)
            {
                _nextFlushAt = EditorApplication.timeSinceStartup + 1.2d;
                return;
            }

            batch = new List<ConsoleEntry>(Math.Min(Pending.Count, MaxBatchSize));
            while (Pending.Count > 0 && batch.Count < MaxBatchSize)
            {
                batch.Add(Pending.Dequeue());
            }
        }

        if (batch == null || batch.Count == 0)
        {
            _nextFlushAt = EditorApplication.timeSinceStartup + 1.2d;
            return;
        }

        SendBatch(batch);
    }

    private static void SendBatch(List<ConsoleEntry> entries)
    {
        var payload = new ConsoleReport { entries = entries };
        var json = JsonUtility.ToJson(payload);
        var body = Encoding.UTF8.GetBytes(json);

        var req = new UnityWebRequest(EndpointUrl, UnityWebRequest.kHttpVerbPOST);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        _sending = true;
        var op = req.SendWebRequest();
        op.completed += _ =>
        {
            _sending = false;
            _nextFlushAt = EditorApplication.timeSinceStartup + 1.2d;
            req.Dispose();
        };
    }

    private static void AppendToLocalFile(ConsoleEntry entry)
    {
        var filePath = LocalLogFilePath;
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        lock (FileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var line = JsonUtility.ToJson(entry) + "\n";
                File.AppendAllText(filePath, line, Encoding.UTF8);
                var info = new FileInfo(filePath);
                if (info.Exists && info.Length > MaxLocalBytes)
                {
                    var all = File.ReadAllLines(filePath, Encoding.UTF8);
                    var start = Math.Max(0, all.Length - 400);
                    File.WriteAllLines(filePath, all[start..], Encoding.UTF8);
                }
            }
            catch
            {
                // Keep console reporting non-intrusive for editor runtime.
            }
        }
    }

    [Serializable]
    private class ConsoleEntry
    {
        public string message;
        public string stackTrace;
        public string type;
        public string time;
    }

    [Serializable]
    private class ConsoleReport
    {
        public List<ConsoleEntry> entries;
    }
}
#endif

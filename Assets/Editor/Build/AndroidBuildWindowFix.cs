using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Workarounds for Unity Android Build Settings IMGUI errors such as:
/// ArgumentException: Getting control 1's position in a group with only 1 controls when doing repaint
/// (AndroidBuildWindowExtension.DoTargetListUnsafe)
///
/// Common triggers: script recompile while Build Settings is open, ADB device-list
/// changing between Layout/Repaint, or stale/invalid Android signing paths.
/// </summary>
public static class AndroidBuildWindowFix
{
    [MenuItem("Build/Reset Android Build Window")]
    public static void ResetAndroidBuildWindow()
    {
        // Close any open Build Settings windows so IMGUI layout state is discarded.
        var buildWindows = Resources.FindObjectsOfTypeAll<BuildPlayerWindow>();
        foreach (var window in buildWindows)
        {
            window.Close();
        }

        RestartAdb();

        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem("File/Build Settings...");
            Debug.Log(
                "Android Build Window reset. If the IMGUI error returns, leave Build Settings closed during script compile, then reopen.");
        };
    }

    [MenuItem("Build/Restart ADB Server")]
    public static void RestartAdbMenu()
    {
        RestartAdb();
    }

    private static void RestartAdb()
    {
        try
        {
            RunAdb("kill-server");
            RunAdb("start-server");
            RunAdb("devices");
            Debug.Log("ADB server restarted.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restart ADB (optional): {e.Message}");
        }
    }

    private static void RunAdb(string args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "adb",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            if (process == null)
            {
                throw new System.InvalidOperationException("Failed to start adb process.");
            }

            process.WaitForExit(10000);
        }
    }
}

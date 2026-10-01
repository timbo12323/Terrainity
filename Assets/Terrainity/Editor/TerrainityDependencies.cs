using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Terrainity.Editor
{
    [InitializeOnLoad]
    internal static class TerrainityDependencies
    {
        internal const string ProBuilderId = "com.unity.probuilder";
        internal static event Action Changed;
        internal static bool Busy => list != null || add != null;
        internal static bool Installed { get; private set; }
        internal static string Message { get; private set; } = "Checking installed packages…";
        static ListRequest list;
        static AddRequest add;

        static TerrainityDependencies() { EditorApplication.delayCall += Refresh; }

        internal static void Refresh()
        {
            if (Busy) return;
            try
            {
                Message = "Checking installed packages…";
                list = Client.List(true, true);
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
            }
            catch (Exception e) { Message = e.Message; }
            Changed?.Invoke();
        }

        internal static void Install()
        {
            if (Busy || Installed) return;
            try
            {
                Message = "Installing ProBuilder… Unity may reload scripts.";
                add = Client.Add(ProBuilderId);
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
            }
            catch (Exception e) { Message = e.Message; }
            Changed?.Invoke();
        }

        static void Poll()
        {
            if (add != null && add.IsCompleted)
            {
                var success = add.Status == StatusCode.Success;
                Message = success ? "ProBuilder installed." : "Installation failed: " + add.Error?.message;
                add = null;
                EditorApplication.update -= Poll;
                if (success) Refresh();
                else Changed?.Invoke();
                return;
            }
            if (list == null || !list.IsCompleted) return;
            if (list.Status == StatusCode.Success)
            {
                var package = list.Result.FirstOrDefault(p => p.name == ProBuilderId);
                Installed = package != null;
                Message = Installed ? "Installed • ProBuilder " + package.version : "Not installed • Optional for tree generation";
            }
            else Message = "Package check failed: " + list.Error?.message;
            list = null;
            EditorApplication.update -= Poll;
            Changed?.Invoke();
        }
    }
}

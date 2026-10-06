using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    public sealed partial class TerrainityWindow
    {
        [Serializable]
        sealed class GitHubRelease
        {
            public string name = "";
            public string tag_name = "";
            public string body = "";
        }

        Label latestReleaseLabel;
        UnityWebRequest releaseRequest;

        void CheckLatestRelease()
        {
            StopReleaseCheck();
            if (latestReleaseLabel == null) return;
            latestReleaseLabel.text = "Checking latest GitHub release…";
            try
            {
                releaseRequest = UnityWebRequest.Get("https://api.github.com/repos/timbo12323/Terrainity/releases/latest");
                releaseRequest.timeout = 10; // Avoid retaining a stalled request until the window closes.
                releaseRequest.SetRequestHeader("Accept", "application/vnd.github+json");
                releaseRequest.SetRequestHeader("User-Agent", "Terrainity-Unity-Editor");
                releaseRequest.SendWebRequest();
                EditorApplication.update += PollReleaseCheck;
            }
            catch (Exception)
            {
                latestReleaseLabel.text = "Latest GitHub release unavailable.";
                StopReleaseCheck();
            }
        }

        void PollReleaseCheck()
        {
            if (releaseRequest == null || !releaseRequest.isDone) return;
            if (latestReleaseLabel != null)
            {
                if (releaseRequest.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var release = JsonUtility.FromJson<GitHubRelease>(releaseRequest.downloadHandler.text);
                        string title = string.IsNullOrWhiteSpace(release?.name) ? release?.tag_name : release.name;
                        string tag = release?.tag_name;
                        string summary = FirstReleaseLine(release?.body);
                        latestReleaseLabel.text = string.IsNullOrWhiteSpace(title)
                            ? "Latest GitHub release unavailable."
                            : "Latest release: " + title
                                + (string.IsNullOrWhiteSpace(tag) || string.Equals(title, tag, StringComparison.OrdinalIgnoreCase) ? "" : " (" + tag + ")")
                                + (string.IsNullOrEmpty(summary) ? "" : " — " + summary);
                    }
                    catch (Exception) { latestReleaseLabel.text = "Latest GitHub release unavailable."; }
                }
                else
                    latestReleaseLabel.text = releaseRequest.responseCode == 404
                        ? "No published GitHub release yet."
                        : "Latest GitHub release unavailable.";
            }
            StopReleaseCheck();
        }

        static string FirstReleaseLine(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            foreach (string line in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = line.Trim().TrimStart('#', '-', '*', ' ');
                if (text.Length == 0) continue;
                return text.Length > 160 ? text.Substring(0, 157) + "…" : text;
            }
            return null;
        }

        void StopReleaseCheck()
        {
            EditorApplication.update -= PollReleaseCheck;
            releaseRequest?.Dispose();
            releaseRequest = null;
        }
    }
}

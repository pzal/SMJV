#if UNITY_ANDROID
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor.Android;
using UnityEngine;

// Workarounds for Meta XR SDK 201.0.0 + AGP 9 / Gradle 9.1 incompatibilities:
//
// 1. InteractionSdk.aar and OVRPlugin.aar both declare package="com.oculus.Integration"
//    in their AndroidManifest.xml. AGP 9 rejects duplicate namespaces across modules.
//    We rewrite each staged AAR to give it a unique namespace.
//
// 2. The generated gradle.properties enables Jetifier. Jetifier on Gradle 9.1 trips
//    https://issuetracker.google.com/issues/184622491 ("invalid entry size") on these
//    AARs. Jetifier is deprecated and only translates legacy android.support.* refs
//    that Meta SDK 201 doesn't use, so we disable it.
//
// Remove this file once Meta ships AARs with distinct namespaces (and Unity stops
// defaulting Jetifier to enabled).
public class MetaXrAarNamespaceFixer : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    const string OldPackage = "com.oculus.Integration";

    static readonly (string AarName, string NewPackage)[] Targets =
    {
        ("InteractionSdk.aar", "com.oculus.integration.interactionsdk"),
        ("OVRPlugin.aar",      "com.oculus.integration.ovrplugin"),
    };

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        // `path` is .../Gradle/unityLibrary
        var libsDir = Path.Combine(path, "libs");
        if (Directory.Exists(libsDir))
        {
            foreach (var (aarName, newPackage) in Targets)
            {
                var aarPath = Path.Combine(libsDir, aarName);
                if (!File.Exists(aarPath))
                {
                    Debug.Log($"[MetaXrAarNamespaceFixer] {aarName} not in libs, skipping.");
                    continue;
                }
                try { RewriteAarManifestPackage(aarPath, newPackage); }
                catch (Exception e)
                {
                    Debug.LogError($"[MetaXrAarNamespaceFixer] Failed to rewrite {aarPath}: {e}");
                    throw;
                }
            }
        }
        else
        {
            Debug.LogWarning($"[MetaXrAarNamespaceFixer] libs dir not found: {libsDir}");
        }

        var gradleRoot = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(gradleRoot))
            DisableJetifier(Path.Combine(gradleRoot, "gradle.properties"));
    }

    static void RewriteAarManifestPackage(string aarPath, string newPackage)
    {
        // Repack the AAR from scratch instead of editing in place. ZipArchive's Update
        // mode emits non-zero local sizes for directory entries, which Gradle 9.1's
        // Jetifier transform rejects (issuetracker 184622491). Rebuilding a fresh zip
        // avoids that pitfall regardless of whether Jetifier is enabled.

        // Probe first so we don't repack when already patched (idempotent on re-builds).
        using (var probe = ZipFile.OpenRead(aarPath))
        {
            var manifestEntry = probe.GetEntry("AndroidManifest.xml");
            if (manifestEntry == null)
            {
                Debug.LogWarning($"[MetaXrAarNamespaceFixer] {Path.GetFileName(aarPath)}: no AndroidManifest.xml, skipping.");
                return;
            }
            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
            var existing = reader.ReadToEnd();
            if (!existing.Contains(OldPackage))
            {
                Debug.Log($"[MetaXrAarNamespaceFixer] {Path.GetFileName(aarPath)} already patched, skipping.");
                return;
            }
        }

        var tmpPath = aarPath + ".tmp";
        if (File.Exists(tmpPath)) File.Delete(tmpPath);

        using (var src = ZipFile.OpenRead(aarPath))
        using (var dstStream = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write))
        using (var dst = new ZipArchive(dstStream, ZipArchiveMode.Create))
        {
            foreach (var entry in src.Entries)
            {
                // Skip directory entries entirely — they are what trip Jetifier, and
                // AAR consumers don't need them.
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) && entry.Length == 0)
                    continue;

                if (entry.FullName == "AndroidManifest.xml")
                {
                    string manifest;
                    using (var r = new StreamReader(entry.Open(), Encoding.UTF8))
                        manifest = r.ReadToEnd();
                    var rewritten = manifest.Replace(
                        $"package=\"{OldPackage}\"",
                        $"package=\"{newPackage}\"");
                    var fresh = dst.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                    using var w = new StreamWriter(fresh.Open(), new UTF8Encoding(false));
                    w.Write(rewritten);
                }
                else
                {
                    var fresh = dst.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                    using var inStream = entry.Open();
                    using var outStream = fresh.Open();
                    inStream.CopyTo(outStream);
                }
            }
        }

        File.Delete(aarPath);
        File.Move(tmpPath, aarPath);
        Debug.Log($"[MetaXrAarNamespaceFixer] Patched {Path.GetFileName(aarPath)}: {OldPackage} -> {newPackage}");
    }

    static void DisableJetifier(string gradlePropertiesPath)
    {
        if (!File.Exists(gradlePropertiesPath))
        {
            Debug.LogWarning($"[MetaXrAarNamespaceFixer] gradle.properties not found at {gradlePropertiesPath}");
            return;
        }
        var text = File.ReadAllText(gradlePropertiesPath);
        if (text.Contains("android.enableJetifier=false"))
        {
            Debug.Log("[MetaXrAarNamespaceFixer] Jetifier already disabled.");
            return;
        }
        if (!text.Contains("android.enableJetifier=true"))
        {
            Debug.Log("[MetaXrAarNamespaceFixer] No enableJetifier=true line; nothing to flip.");
            return;
        }
        var rewritten = text.Replace("android.enableJetifier=true", "android.enableJetifier=false");
        File.WriteAllText(gradlePropertiesPath, rewritten);
        Debug.Log("[MetaXrAarNamespaceFixer] Set android.enableJetifier=false to avoid Gradle 9.1 Jetifier zip bug.");
    }
}
#endif

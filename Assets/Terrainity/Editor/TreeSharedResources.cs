using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    // Match resource contents across exports; track only this transaction's new assets for rollback.
    internal sealed class TreeSharedResources
    {
        internal const string Root = "Assets/TerrainityGenerated/Shared";
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly List<string> createdGuids = new List<string>();

        internal TreeSharedResources()
        {
            TreeExporter.EnsureFolder(Root + "/Textures");
            TreeExporter.EnsureFolder(Root + "/Materials");
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture != null && texture.isReadable) textures[TextureKey(texture)] = texture;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && material.shader != null) materials[MaterialKey(material)] = material;
            }
        }

        internal Texture2D Texture(Texture2D candidate, string label)
        {
            try
            {
                string key = TextureKey(candidate);
                if (textures.TryGetValue(key, out var existing))
                {
                    UnityEngine.Object.DestroyImmediate(candidate);
                    return existing;
                }
                candidate.name = label;
                candidate.hideFlags = HideFlags.None;
                Save(candidate, Root + "/Textures/" + TreeExporter.SafeName(label) + "_" + key + ".asset");
                textures[key] = candidate;
                return candidate;
            }
            catch
            {
                if (candidate != null && !EditorUtility.IsPersistent(candidate)) UnityEngine.Object.DestroyImmediate(candidate);
                throw;
            }
        }

        internal Material Material(Material candidate, string label)
        {
            try
            {
                string key = MaterialKey(candidate);
                if (materials.TryGetValue(key, out var existing))
                {
                    UnityEngine.Object.DestroyImmediate(candidate);
                    return existing;
                }
                candidate.name = label;
                Save(candidate, Root + "/Materials/" + TreeExporter.SafeName(label) + "_" + key + ".mat");
                materials[key] = candidate;
                return candidate;
            }
            catch
            {
                if (candidate != null && !EditorUtility.IsPersistent(candidate)) UnityEngine.Object.DestroyImmediate(candidate);
                throw;
            }
        }

        void Save(UnityEngine.Object asset, string suggested)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(suggested);
            try { AssetDatabase.CreateAsset(asset, path); }
            catch { UnityEngine.Object.DestroyImmediate(asset); throw; }
            createdGuids.Add(AssetDatabase.AssetPathToGUID(path));
        }

        internal void Rollback()
        {
            for (int i = createdGuids.Count - 1; i >= 0; i--)
            {
                string path = AssetDatabase.GUIDToAssetPath(createdGuids[i]);
                if (path.StartsWith(Root + "/", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(path);
            }
        }

        static string Hash(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var sha = SHA256.Create())
            {
                write(writer); writer.Flush();
                // Hash the backing stream directly; textures can make this buffer several MB.
                stream.Position = 0;
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        static string TextureKey(Texture2D texture) => Hash(w =>
        {
            w.Write(texture.width); w.Write(texture.height); w.Write((int)texture.graphicsFormat);
            w.Write(texture.mipmapCount); w.Write((int)texture.wrapModeU); w.Write((int)texture.wrapModeV);
            w.Write((int)texture.wrapModeW); w.Write((int)texture.filterMode);
            w.Write(texture.anisoLevel); w.Write(texture.mipMapBias);
            w.Write(texture.GetRawTextureData<byte>().ToArray());
        });

        static void Vector(BinaryWriter w, Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        static string MaterialKey(Material material) => Hash(w =>
        {
            w.Write(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.shader)));
            w.Write(material.shader.name); w.Write(material.renderQueue); w.Write(material.enableInstancing);
            w.Write(material.doubleSidedGI); w.Write((int)material.globalIlluminationFlags);
            foreach (string keyword in material.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)) w.Write(keyword);
            w.Write("properties");
            for (int i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                string name = material.shader.GetPropertyName(i);
                w.Write(name);
                switch (material.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: Vector(w, material.GetColor(name)); break;
                    case ShaderPropertyType.Vector: Vector(w, material.GetVector(name)); break;
                    case ShaderPropertyType.Int: w.Write(material.GetInteger(name)); break;
                    case ShaderPropertyType.Texture:
                        var texture = material.GetTexture(name);
                        if (texture != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long id))
                        { w.Write(guid); w.Write(id); }
                        else { w.Write(""); w.Write(0L); }
                        Vector(w, material.GetTextureScale(name)); Vector(w, material.GetTextureOffset(name));
                        break;
                    default: w.Write(material.GetFloat(name)); break;
                }
            }
            for (int i = 0; i < material.passCount; i++)
            {
                string name = material.GetPassName(i); w.Write(name); w.Write(material.GetShaderPassEnabled(name));
            }
            w.Write(material.GetTag("RenderType", false, ""));
        });
    }
}

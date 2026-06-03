using System;
using System.Collections.Generic;
using UnityEngine;

namespace SMJV
{
    public class SimAssetCache
    {
        private readonly Dictionary<string, SimMesh> _meshDefs = new();
        private readonly Dictionary<string, Mesh> _meshes = new();
        private readonly Dictionary<string, SimTexture> _textureDefs = new();
        private readonly Dictionary<string, Texture2D> _textures = new();
        private readonly Dictionary<string, SimMaterial> _materialDefs = new();
        private readonly Dictionary<string, Material> _materials = new();
        private readonly Dictionary<string, int> _meshRefCounts = new();
        private readonly Dictionary<string, int> _textureRefCounts = new();
        private readonly Dictionary<string, int> _materialRefCounts = new();
        private AssetRefs _incomingSceneRefs;
        private bool _deferEviction;

        public class AssetRefs
        {
            public readonly HashSet<string> Meshes = new();
            public readonly HashSet<string> Textures = new();
            public readonly HashSet<string> Materials = new();
        }

        public void RegisterAssets(SimAssetBundle assets)
        {
            if (assets == null) return;

            if (assets.meshes != null)
            {
                foreach (var entry in assets.meshes)
                {
                    string hash = entry.Key;
                    SimMesh mesh = entry.Value;
                    if (mesh == null || string.IsNullOrEmpty(hash)) continue;
                    mesh.hash = string.IsNullOrEmpty(mesh.hash) ? hash : mesh.hash;
                    _meshDefs[mesh.hash] = mesh;
                }
            }

            if (assets.textures != null)
            {
                foreach (var entry in assets.textures)
                {
                    string hash = entry.Key;
                    SimTexture texture = entry.Value;
                    if (texture == null || string.IsNullOrEmpty(hash)) continue;
                    texture.hash = string.IsNullOrEmpty(texture.hash) ? hash : texture.hash;
                    _textureDefs[texture.hash] = texture;
                }
            }

            if (assets.materials != null)
            {
                foreach (var entry in assets.materials)
                {
                    string hash = entry.Key;
                    SimMaterial material = entry.Value;
                    if (material == null || string.IsNullOrEmpty(hash)) continue;
                    material.hash = string.IsNullOrEmpty(material.hash) ? hash : material.hash;
                    _materialDefs[material.hash] = material;
                }
            }
        }

        public AssetRefs FindMissingAssets(SimObject[] objects)
        {
            AssetRefs required = CollectRefs(objects);
            AssetRefs missing = new AssetRefs();

            foreach (string hash in required.Meshes)
            {
                if (!_meshDefs.ContainsKey(hash))
                {
                    missing.Meshes.Add(hash);
                }
            }
            foreach (string hash in required.Textures)
            {
                if (!_textureDefs.ContainsKey(hash))
                {
                    missing.Textures.Add(hash);
                }
            }
            foreach (string hash in required.Materials)
            {
                if (!_materialDefs.ContainsKey(hash))
                {
                    missing.Materials.Add(hash);
                }
            }

            return missing;
        }

        public bool HasMissingAssets(AssetRefs missing)
        {
            return missing != null &&
                   (missing.Meshes.Count > 0 ||
                    missing.Textures.Count > 0 ||
                    missing.Materials.Count > 0);
        }

        public Mesh GetOrCreateMesh(SimMesh simMesh, string name)
        {
            SimMesh meshDef = ResolveMeshDefinition(simMesh);
            if (meshDef == null) return null;

            string hash = FirstHash(simMesh?.hash, meshDef.hash);
            if (!string.IsNullOrEmpty(hash) && _meshes.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            if (meshDef.vertices == null || meshDef.normals == null || meshDef.indices == null)
            {
                Debug.LogWarning($"Mesh '{name}' is missing bytes and is not present in the asset cache.");
                return null;
            }

            Mesh mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                name = string.IsNullOrEmpty(name) ? "SimMesh" : $"{name}_Mesh"
            };
            mesh.vertices = SimSceneLoader.DecodeArray<Vector3>(meshDef.vertices, 0, meshDef.vertices.Length);
            mesh.normals = SimSceneLoader.DecodeArray<Vector3>(meshDef.normals, 0, meshDef.normals.Length);
            mesh.triangles = SimSceneLoader.DecodeArray<int>(meshDef.indices, 0, meshDef.indices.Length);
            if (meshDef.uv != null)
            {
                mesh.uv = SimSceneLoader.DecodeArray<Vector2>(meshDef.uv, 0, meshDef.uv.Length);
            }
            mesh.hideFlags = HideFlags.DontSave;

            if (!string.IsNullOrEmpty(hash))
            {
                _meshes[hash] = mesh;
                _meshDefs[hash] = meshDef;
            }

            return mesh;
        }

        public Texture2D GetOrCreateTexture(SimTexture simTexture)
        {
            SimTexture textureDef = ResolveTextureDefinition(simTexture);
            if (textureDef == null) return null;

            string hash = FirstHash(simTexture?.hash, textureDef.hash);
            if (!string.IsNullOrEmpty(hash) && _textures.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            if (textureDef.textureData == null)
            {
                Debug.LogWarning($"Texture '{hash}' is missing bytes and is not present in the asset cache.");
                return null;
            }

            Texture2D texture = new Texture2D(2, 2);
            if (!texture.LoadImage(textureDef.textureData))
            {
                Debug.LogError("Failed to decode the JPEG/PNG texture data.");
                DestroyUnityObject(texture);
                return null;
            }
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;

            if (!string.IsNullOrEmpty(hash))
            {
                _textures[hash] = texture;
                _textureDefs[hash] = textureDef;
            }

            return texture;
        }

        public SimMaterial ResolveMaterialDefinition(SimMaterial simMaterial)
        {
            if (simMaterial == null) return null;

            string hash = simMaterial.hash;
            if (HasMaterialData(simMaterial))
            {
                if (!string.IsNullOrEmpty(hash))
                {
                    _materialDefs[hash] = simMaterial;
                }
                return simMaterial;
            }

            if (!string.IsNullOrEmpty(hash) && _materialDefs.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            Debug.LogWarning($"Material '{hash}' is missing data and is not present in the asset cache.");
            return null;
        }

        public Material GetOrCreateMaterial(SimMaterial simMaterial, Func<SimMaterial, Material> factory)
        {
            SimMaterial materialDef = ResolveMaterialDefinition(simMaterial);
            if (materialDef == null) return null;

            string hash = FirstHash(simMaterial?.hash, materialDef.hash);
            if (!string.IsNullOrEmpty(hash) && _materials.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            Material material = factory(materialDef);
            if (material == null) return null;
            material.hideFlags = HideFlags.DontSave;

            if (!string.IsNullOrEmpty(hash))
            {
                _materials[hash] = material;
                _materialDefs[hash] = materialDef;
            }

            return material;
        }

        public void BeginSceneUpdate(SimObject[] objects)
        {
            _incomingSceneRefs = CollectRefs(objects);
            _deferEviction = true;
        }

        public void EndSceneUpdate()
        {
            _deferEviction = false;
            _incomingSceneRefs = null;
            EvictZeroRefAssets();
        }

        public AssetRefs RetainObjectAssets(SimObject simObject)
        {
            AssetRefs refs = CollectRefs(simObject);
            foreach (string hash in refs.Meshes)
            {
                IncrementRef(_meshRefCounts, hash);
            }
            foreach (string hash in refs.Textures)
            {
                IncrementRef(_textureRefCounts, hash);
            }
            foreach (string hash in refs.Materials)
            {
                IncrementRef(_materialRefCounts, hash);
            }

            return refs;
        }

        public void ReleaseObjectAssets(AssetRefs refs)
        {
            if (refs == null) return;

            foreach (string hash in refs.Materials)
            {
                if (DecrementRef(_materialRefCounts, hash) && !ShouldDeferMaterialEviction(hash))
                {
                    EvictMaterial(hash);
                }
            }
            foreach (string hash in refs.Meshes)
            {
                if (DecrementRef(_meshRefCounts, hash) && !ShouldDeferMeshEviction(hash))
                {
                    EvictMesh(hash);
                }
            }
            foreach (string hash in refs.Textures)
            {
                if (DecrementRef(_textureRefCounts, hash) && !ShouldDeferTextureEviction(hash))
                {
                    EvictTexture(hash);
                }
            }
        }

        public SimTexture ResolveTextureDefinition(SimTexture simTexture)
        {
            if (simTexture == null) return null;

            string hash = simTexture.hash;
            if (simTexture.textureData != null)
            {
                if (!string.IsNullOrEmpty(hash))
                {
                    _textureDefs[hash] = simTexture;
                }
                return simTexture;
            }

            if (!string.IsNullOrEmpty(hash) && _textureDefs.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            Debug.LogWarning($"Texture '{hash}' is missing data and is not present in the asset cache.");
            return null;
        }

        public void Clear()
        {
            foreach (var mesh in _meshes.Values)
            {
                DestroyUnityObject(mesh);
            }
            foreach (var texture in _textures.Values)
            {
                DestroyUnityObject(texture);
            }
            foreach (var material in _materials.Values)
            {
                DestroyUnityObject(material);
            }

            _meshDefs.Clear();
            _meshes.Clear();
            _textureDefs.Clear();
            _textures.Clear();
            _materialDefs.Clear();
            _materials.Clear();
            _meshRefCounts.Clear();
            _textureRefCounts.Clear();
            _materialRefCounts.Clear();
            _incomingSceneRefs = null;
            _deferEviction = false;
        }

        private AssetRefs CollectRefs(SimObject[] objects)
        {
            AssetRefs refs = new AssetRefs();
            if (objects == null) return refs;

            foreach (SimObject obj in objects)
            {
                AddRefs(refs, obj);
            }

            return refs;
        }

        private AssetRefs CollectRefs(SimObject obj)
        {
            AssetRefs refs = new AssetRefs();
            AddRefs(refs, obj);
            return refs;
        }

        private void AddRefs(AssetRefs refs, SimObject obj)
        {
            if (obj?.visuals == null) return;

            foreach (SimVisual visual in obj.visuals)
            {
                if (visual == null) continue;

                string meshHash = ResolveMeshHash(visual.mesh);
                if (!string.IsNullOrEmpty(meshHash))
                {
                    refs.Meshes.Add(meshHash);
                }

                SimMaterial materialDef = TryResolveMaterialDefinition(visual.material);
                string materialHash = ResolveMaterialHash(visual.material, materialDef);
                if (!string.IsNullOrEmpty(materialHash))
                {
                    refs.Materials.Add(materialHash);
                }

                SimTexture texture = materialDef?.texture ?? visual.material?.texture;
                SimTexture textureDef = TryResolveTextureDefinition(texture);
                string textureHash = ResolveTextureHash(texture, textureDef);
                if (!string.IsNullOrEmpty(textureHash))
                {
                    refs.Textures.Add(textureHash);
                }
            }
        }

        private string ResolveMeshHash(SimMesh simMesh)
        {
            SimMesh meshDef = TryResolveMeshDefinition(simMesh);
            return FirstHash(simMesh?.hash, meshDef?.hash);
        }

        private static string ResolveMaterialHash(SimMaterial materialRef, SimMaterial materialDef)
        {
            return FirstHash(materialRef?.hash, materialDef?.hash);
        }

        private static string ResolveTextureHash(SimTexture textureRef, SimTexture textureDef)
        {
            return FirstHash(textureRef?.hash, textureDef?.hash);
        }

        private static void IncrementRef(Dictionary<string, int> counts, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return;
            counts.TryGetValue(hash, out int count);
            counts[hash] = count + 1;
        }

        private static bool DecrementRef(Dictionary<string, int> counts, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            if (!counts.TryGetValue(hash, out int count)) return true;

            count -= 1;
            if (count > 0)
            {
                counts[hash] = count;
                return false;
            }

            counts.Remove(hash);
            return true;
        }

        private void EvictZeroRefAssets()
        {
            foreach (string hash in ZeroRefHashes(_materialDefs.Keys, _materialRefCounts))
            {
                EvictMaterial(hash);
            }
            foreach (string hash in ZeroRefHashes(_meshDefs.Keys, _meshRefCounts))
            {
                EvictMesh(hash);
            }
            foreach (string hash in ZeroRefHashes(_textureDefs.Keys, _textureRefCounts))
            {
                EvictTexture(hash);
            }
        }

        private static List<string> ZeroRefHashes(IEnumerable<string> hashes, Dictionary<string, int> counts)
        {
            List<string> result = new();
            foreach (string hash in hashes)
            {
                if (!counts.ContainsKey(hash))
                {
                    result.Add(hash);
                }
            }
            return result;
        }

        private bool ShouldDeferMeshEviction(string hash)
        {
            return _deferEviction && _incomingSceneRefs != null && _incomingSceneRefs.Meshes.Contains(hash);
        }

        private bool ShouldDeferTextureEviction(string hash)
        {
            return _deferEviction && _incomingSceneRefs != null && _incomingSceneRefs.Textures.Contains(hash);
        }

        private bool ShouldDeferMaterialEviction(string hash)
        {
            return _deferEviction && _incomingSceneRefs != null && _incomingSceneRefs.Materials.Contains(hash);
        }

        private void EvictMesh(string hash)
        {
            if (_meshes.TryGetValue(hash, out Mesh mesh))
            {
                DestroyUnityObject(mesh);
            }
            _meshes.Remove(hash);
            _meshDefs.Remove(hash);
        }

        private void EvictTexture(string hash)
        {
            if (_textures.TryGetValue(hash, out Texture2D texture))
            {
                DestroyUnityObject(texture);
            }
            _textures.Remove(hash);
            _textureDefs.Remove(hash);
        }

        private void EvictMaterial(string hash)
        {
            if (_materials.TryGetValue(hash, out Material material))
            {
                DestroyUnityObject(material);
            }
            _materials.Remove(hash);
            _materialDefs.Remove(hash);
        }

        private SimMesh ResolveMeshDefinition(SimMesh simMesh)
        {
            if (simMesh == null) return null;

            string hash = simMesh.hash;
            if (simMesh.vertices != null && simMesh.normals != null && simMesh.indices != null)
            {
                if (!string.IsNullOrEmpty(hash))
                {
                    _meshDefs[hash] = simMesh;
                }
                return simMesh;
            }

            if (!string.IsNullOrEmpty(hash) && _meshDefs.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            return null;
        }

        private static bool HasMaterialData(SimMaterial material)
        {
            return material.color != null ||
                   material.emissionColor != null ||
                   material.specular != 0f ||
                   material.shininess != 0f ||
                   material.reflectance != 0f;
        }

        private SimMesh TryResolveMeshDefinition(SimMesh simMesh)
        {
            if (simMesh == null) return null;

            string hash = simMesh.hash;
            if (simMesh.vertices != null && simMesh.normals != null && simMesh.indices != null)
            {
                return simMesh;
            }

            if (!string.IsNullOrEmpty(hash) && _meshDefs.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            return null;
        }

        private SimMaterial TryResolveMaterialDefinition(SimMaterial simMaterial)
        {
            if (simMaterial == null) return null;

            string hash = simMaterial.hash;
            if (HasMaterialData(simMaterial))
            {
                return simMaterial;
            }

            if (!string.IsNullOrEmpty(hash) && _materialDefs.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            return null;
        }

        private SimTexture TryResolveTextureDefinition(SimTexture simTexture)
        {
            if (simTexture == null) return null;

            string hash = simTexture.hash;
            if (simTexture.textureData != null)
            {
                return simTexture;
            }

            if (!string.IsNullOrEmpty(hash) && _textureDefs.TryGetValue(hash, out var cached))
            {
                return cached;
            }

            return null;
        }

        private static string FirstHash(string preferred, string fallback)
        {
            return !string.IsNullOrEmpty(preferred) ? preferred : fallback;
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}

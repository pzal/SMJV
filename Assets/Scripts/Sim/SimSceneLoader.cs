using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SMJV
{
    public class SimSceneLoader : MonoBehaviour
    {
        private const float AlphaEpsilon = 0.0001f;

        [SerializeField] private Material opaqueBaseMaterial;
        [SerializeField] private Material transparentBaseMaterial;

        private SimAssetCache assetCache = new();
        private readonly Dictionary<string, GameObject> _simObjectDict = new();
        private readonly Dictionary<string, Transform> _simObjTransDict = new();
        private readonly Dictionary<string, string> _simObjectContentHashes = new();
        private readonly Dictionary<string, SimAssetCache.AssetRefs> _simObjectAssetRefs = new();

        public void Configure(Material opaque, Material transparent)
        {
            opaqueBaseMaterial = opaque;
            transparentBaseMaterial = transparent;
        }

        public void SetAssetCache(SimAssetCache cache)
        {
            assetCache = cache ?? new SimAssetCache();
        }

        public Dictionary<string, Transform> GetObjectsTrans() => _simObjTransDict;

        public void ReconcileScene(SimSceneConfig config, SimObject[] objects)
        {
            if (config != null && !string.IsNullOrEmpty(config.name))
            {
                gameObject.name = config.name;
            }

            assetCache ??= new SimAssetCache();
            objects ??= Array.Empty<SimObject>();
            assetCache.BeginSceneUpdate(objects);
            try
            {
                HashSet<string> incomingNames = new();
                foreach (SimObject obj in objects)
                {
                    if (obj != null && !string.IsNullOrEmpty(obj.name))
                    {
                        incomingNames.Add(obj.name);
                    }
                }

                RemoveMissingObjects(incomingNames);

                int preserved = 0, rebuilt = 0, created = 0;
                foreach (SimObject obj in objects)
                {
                    if (obj == null || string.IsNullOrEmpty(obj.name)) continue;

                    if (_simObjectDict.TryGetValue(obj.name, out GameObject existing))
                    {
                        _simObjectContentHashes.TryGetValue(obj.name, out string existingHash);
                        if (!string.IsNullOrEmpty(obj.contentHash) &&
                            string.Equals(existingHash, obj.contentHash, StringComparison.Ordinal))
                        {
                            ApplyTransform(existing.transform, obj.trans);
                            preserved++;
                            continue;
                        }

                        DestroySimObjectTree(obj.name);
                        rebuilt++;
                    }
                    else
                    {
                        created++;
                    }

                    CreateSimObject(obj);
                    _simObjectContentHashes[obj.name] = obj.contentHash;
                }

                Debug.Log(
                    $"ReconcileScene objects={objects.Length} preserved={preserved} created={created} rebuilt={rebuilt} live={_simObjectDict.Count}"
                );
            }
            finally
            {
                assetCache.EndSceneUpdate();
            }
        }

        public void CreateSimObject(SimObject simObject)
        {
            if (_simObjectDict.ContainsKey(simObject.name))
            {
                Debug.LogWarning($"SimObject with name {simObject.name} already exists, skipping creation.");
                return;
            }

            GameObject newSimGameObject = new GameObject(simObject.name);
            if (!_simObjTransDict.ContainsKey(simObject.parent))
            {
                newSimGameObject.transform.SetParent(transform, false);
            }
            else
            {
                newSimGameObject.transform.SetParent(_simObjTransDict[simObject.parent], false);
            }
            RegisterGameObject(simObject, newSimGameObject);
            if (!string.IsNullOrEmpty(simObject.contentHash))
            {
                _simObjectContentHashes[simObject.name] = simObject.contentHash;
            }
            ApplyTransform(newSimGameObject.transform, simObject.trans);

            if (simObject.visuals != null)
            {
                foreach (var visual in simObject.visuals)
                {
                    GameObject visualObj = CreateSimVisual(visual);
                    if (visualObj != null)
                    {
                        visualObj.transform.SetParent(newSimGameObject.transform, false);
                    }
                }
            }

            assetCache ??= new SimAssetCache();
            _simObjectAssetRefs[simObject.name] = assetCache.RetainObjectAssets(simObject);
        }

        public GameObject CreateSimVisual(SimVisual simVisual)
        {
            if (simVisual == null || string.IsNullOrEmpty(simVisual.name))
            {
                return null;
            }
            if (IsFullyTransparent(simVisual))
            {
                Debug.Log($"Skipping fully transparent SimVisual {simVisual.name}.");
                return null;
            }
            if (_simObjTransDict.ContainsKey(simVisual.name))
            {
                Debug.LogWarning($"SimVisualObject with name {simVisual.name} already exists, skipping creation.");
                return null;
            }

            GameObject visualObj;
            switch (simVisual.type)
            {
                case "CUBE":     visualObj = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
                case "PLANE":    visualObj = GameObject.CreatePrimitive(PrimitiveType.Plane); break;
                case "CYLINDER": visualObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder); break;
                case "CAPSULE":  visualObj = GameObject.CreatePrimitive(PrimitiveType.Capsule); break;
                case "SPHERE":   visualObj = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
                case "MESH":
                    if (simVisual.mesh == null)
                    {
                        Debug.LogWarning($"SimVisual {simVisual.name} has no mesh data; skipping visual.");
                        return null;
                    }
                    Mesh mesh = ResolveMesh(simVisual.mesh, simVisual.name);
                    if (mesh == null)
                    {
                        Debug.LogWarning($"SimVisual {simVisual.name} mesh could not be resolved.");
                        return null;
                    }
                    visualObj = new GameObject(simVisual.name, typeof(MeshFilter), typeof(MeshRenderer));
                    visualObj.GetComponent<MeshFilter>().sharedMesh = mesh;
                    break;
                default:
                    Debug.LogWarning($"Unknown SimVisual type {simVisual.type}; skipping visual.");
                    return null;
            }

            Renderer visualRenderer = visualObj.GetComponent<Renderer>();
            if (visualRenderer != null)
            {
                ApplyMaterial(simVisual, visualRenderer);
            }
            ApplyTransform(visualObj.transform, simVisual.trans);
            return visualObj;
        }

        public static T[] DecodeArray<T>(byte[] data, int start, int length) where T : struct
        {
            return MemoryMarshal.Cast<byte, T>(new ReadOnlySpan<byte>(data, start, length)).ToArray();
        }

        private static void ApplyTransform(Transform uTransform, SimTransform simTrans)
        {
            uTransform.localPosition = simTrans.GetPos();
            uTransform.localRotation = simTrans.GetRot();
            uTransform.localScale = simTrans.GetScale();
        }

        private void RegisterGameObject(SimObject simObject, GameObject simGameObject)
        {
            if (_simObjectDict.ContainsKey(simObject.name))
            {
                Debug.LogWarning($"SimObject with name {simObject.name} already exists, skipping registration.");
                return;
            }
            if (_simObjTransDict.ContainsKey(simObject.name))
            {
                Debug.LogWarning($"SimObject with id {simObject.name} already exists, skipping registration.");
                return;
            }
            _simObjectDict.Add(simObject.name, simGameObject);
            _simObjTransDict.Add(simObject.name, simGameObject.transform);
        }

        private void RemoveMissingObjects(HashSet<string> incomingNames)
        {
            List<string> oldNames = new(_simObjectDict.Keys);
            oldNames.Sort((a, b) => ObjectDepth(b).CompareTo(ObjectDepth(a)));
            foreach (string oldName in oldNames)
            {
                if (!incomingNames.Contains(oldName))
                {
                    DestroySimObjectTree(oldName);
                }
            }
        }

        private int ObjectDepth(string name)
        {
            if (!_simObjTransDict.TryGetValue(name, out Transform target) || target == null)
            {
                return 0;
            }

            int depth = 0;
            Transform current = target;
            while (current != null && current != transform)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private void DestroySimObjectTree(string name)
        {
            if (!_simObjectDict.TryGetValue(name, out GameObject root) || root == null)
            {
                _simObjectDict.Remove(name);
                _simObjTransDict.Remove(name);
                _simObjectContentHashes.Remove(name);
                ReleaseSimObjectAssetRefs(name);
                return;
            }

            Transform rootTransform = root.transform;
            List<string> removedNames = new();
            foreach (var entry in _simObjTransDict)
            {
                Transform candidate = entry.Value;
                if (candidate == null ||
                    candidate == rootTransform ||
                    candidate.IsChildOf(rootTransform))
                {
                    removedNames.Add(entry.Key);
                }
            }

            foreach (string removedName in removedNames)
            {
                _simObjectDict.Remove(removedName);
                _simObjTransDict.Remove(removedName);
                _simObjectContentHashes.Remove(removedName);
            }

            DestroyImmediate(root);

            foreach (string removedName in removedNames)
            {
                ReleaseSimObjectAssetRefs(removedName);
            }
        }

        private void ReleaseSimObjectAssetRefs(string name)
        {
            if (_simObjectAssetRefs.TryGetValue(name, out SimAssetCache.AssetRefs refs))
            {
                assetCache?.ReleaseObjectAssets(refs);
                _simObjectAssetRefs.Remove(name);
            }
        }

        private void ReleaseAllObjectAssetRefs()
        {
            List<string> names = new(_simObjectAssetRefs.Keys);
            foreach (string name in names)
            {
                ReleaseSimObjectAssetRefs(name);
            }
        }

        private Mesh ResolveMesh(SimMesh simMesh, string name)
        {
            assetCache ??= new SimAssetCache();
            return assetCache.GetOrCreateMesh(simMesh, name);
        }

        private void ApplyMaterial(SimVisual simVisual, Renderer renderer)
        {
            if (simVisual.material == null) return;

            assetCache ??= new SimAssetCache();
            Material material = assetCache.GetOrCreateMaterial(
                simVisual.material,
                materialDef =>
                {
                    Material created = CreateMaterialInstance(materialDef);
                    ApplyTexture(materialDef, created);
                    return created;
                }
            );

            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        // Inlined from RGBDMaterialSetProfile.CreateMaterialInstance.
        private Material CreateMaterialInstance(SimMaterial simMat)
        {
            bool transparent = IsTransparent(simMat);
            Material source = transparent ? transparentBaseMaterial : opaqueBaseMaterial;
            Material mat = source != null
                ? new Material(source)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            Color baseColor = BaseColor(simMat);
            ConfigureMaterialSurface(mat, baseColor.a);

            if (simMat == null) return mat;

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", baseColor);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", baseColor);
            }

            if (simMat.emissionColor != null && simMat.emissionColor.Length == 4)
            {
                Color emission = new Color(simMat.emissionColor[0], simMat.emissionColor[1], simMat.emissionColor[2], simMat.emissionColor[3]);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", emission);
                }
                else if (mat.HasProperty("_emissionColor"))
                {
                    mat.SetColor("_emissionColor", emission);
                }
            }
            SetFloatIfPresent(mat, "_SpecularHighlights", simMat.specular);
            SetFloatIfPresent(mat, "_specularHighlights", simMat.specular);
            SetFloatIfPresent(mat, "_Smoothness", simMat.shininess);
            SetFloatIfPresent(mat, "_GlossyReflections", simMat.reflectance);
            return mat;
        }

        private void ApplyTexture(SimMaterial materialDef, Material material)
        {
            if (materialDef?.texture == null || material == null) return;

            SimTexture textureDef = assetCache.ResolveTextureDefinition(materialDef.texture);
            Texture2D texture = assetCache.GetOrCreateTexture(textureDef ?? materialDef.texture);
            if (texture == null) return;

            material.mainTexture = texture;
            float[] textureScale = textureDef?.textureScale ?? materialDef.texture.textureScale;
            material.mainTextureScale = (textureScale != null && textureScale.Length >= 2)
                ? new Vector2(textureScale[0], textureScale[1])
                : Vector2.one;
        }

        private static bool IsTransparent(SimMaterial simMaterial)
        {
            return MaterialAlpha(simMaterial) < 1f - AlphaEpsilon;
        }

        private bool IsFullyTransparent(SimVisual simVisual)
        {
            if (simVisual?.material == null) return false;

            assetCache ??= new SimAssetCache();
            SimMaterial materialDef = assetCache.ResolveMaterialDefinition(simVisual.material) ?? simVisual.material;
            return MaterialAlpha(materialDef) <= AlphaEpsilon;
        }

        private static float MaterialAlpha(SimMaterial simMaterial)
        {
            if (simMaterial?.color == null || simMaterial.color.Length < 4)
            {
                return 1f;
            }
            return simMaterial.color[3];
        }

        private static Color BaseColor(SimMaterial simMaterial)
        {
            if (simMaterial?.color == null)
            {
                return Color.white;
            }
            if (simMaterial.color.Length >= 4)
            {
                return new Color(simMaterial.color[0], simMaterial.color[1], simMaterial.color[2], simMaterial.color[3]);
            }
            if (simMaterial.color.Length >= 3)
            {
                return new Color(simMaterial.color[0], simMaterial.color[1], simMaterial.color[2], 1f);
            }
            return Color.white;
        }

        private static void ConfigureMaterialSurface(Material mat, float alpha)
        {
            if (mat == null) return;

            bool transparent = alpha < 1f - AlphaEpsilon;
            if (transparent)
            {
                SetFloatIfPresent(mat, "_Surface", 1f);
                SetFloatIfPresent(mat, "_Blend", 0f);
                SetFloatIfPresent(mat, "_AlphaClip", 0f);
                SetFloatIfPresent(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                SetFloatIfPresent(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(mat, "_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return;
            }

            SetFloatIfPresent(mat, "_Surface", 0f);
            SetFloatIfPresent(mat, "_Blend", 0f);
            SetFloatIfPresent(mat, "_AlphaClip", 0f);
            SetFloatIfPresent(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetFloatIfPresent(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            SetFloatIfPresent(mat, "_ZWrite", 1f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        private static void SetFloatIfPresent(Material mat, string propertyName, float value)
        {
            if (mat != null && mat.HasProperty(propertyName))
            {
                mat.SetFloat(propertyName, value);
            }
        }

        private void OnDestroy()
        {
            ReleaseAllObjectAssetRefs();
            _simObjectDict.Clear();
            _simObjTransDict.Clear();
            _simObjectContentHashes.Clear();
            _simObjectAssetRefs.Clear();
        }
    }
}

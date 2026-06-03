using System.Collections.Generic;
using MessagePack;
using UnityEngine;

namespace SMJV
{
    [MessagePackObject]
    public class SimTransform
    {
        [Key("pos")] public float[] pos;
        [Key("rot")] public float[] rot;
        [Key("scale")] public float[] scale;

        public Vector3 GetPos() => new Vector3(pos[0], pos[1], pos[2]);
        public Quaternion GetRot() => new Quaternion(rot[0], rot[1], rot[2], rot[3]);
        public Vector3 GetScale() => new Vector3(scale[0], scale[1], scale[2]);
    }

    [MessagePackObject]
    public class SimVisual
    {
        [Key("name")] public string name;
        [Key("type")] public string type;
        [Key("mesh")] public SimMesh mesh;
        [Key("material")] public SimMaterial material;
        [Key("trans")] public SimTransform trans;
    }

    [MessagePackObject]
    public class SimObject
    {
        [Key("name")] public string name;
        [Key("parent")] public string parent;
        [Key("trans")] public SimTransform trans;
        [Key("visuals")] public SimVisual[] visuals;
        [Key("contentHash")] public string contentHash;
    }

    [MessagePackObject]
    public class SimMesh
    {
        [Key("hash")] public string hash;
        [Key("indices")] public byte[] indices;
        [Key("vertices")] public byte[] vertices;
        [Key("normals")] public byte[] normals;
        [Key("uv")] public byte[] uv;
    }

    [MessagePackObject]
    public class SimMaterial
    {
        [Key("hash")] public string hash;
        [Key("color")] public float[] color;
        [Key("emissionColor")] public float[] emissionColor;
        [Key("specular")] public float specular;
        [Key("shininess")] public float shininess;
        [Key("reflectance")] public float reflectance;
        [Key("texture")] public SimTexture texture;
    }

    [MessagePackObject]
    public class SimTexture
    {
        [Key("hash")] public string hash;
        [Key("width")] public int width;
        [Key("height")] public int height;
        [Key("textureType")] public string textureType;
        [Key("textureScale")] public float[] textureScale;
        [Key("textureData")] public byte[] textureData;
    }

    [MessagePackObject]
    public class SimSceneConfig
    {
        [Key("name")] public string name;
        [Key("pos")] public float[] pos;
        [Key("rot")] public float[] rot;
        [Key("scale")] public float[] scale;
    }

    [MessagePackObject]
    public class SimAssetBundle
    {
        [Key("meshes")] public Dictionary<string, SimMesh> meshes;
        [Key("textures")] public Dictionary<string, SimTexture> textures;
        [Key("materials")] public Dictionary<string, SimMaterial> materials;
    }

    [MessagePackObject]
    public class SceneManifestPayload
    {
        [Key("version")] public int version;
        [Key("config")] public SimSceneConfig config;
        [Key("objects")] public SimObject[] objects;
        [Key("sceneHash")] public string sceneHash;
    }

    [MessagePackObject]
    public class AssetHashSetPayload
    {
        [Key("meshes")] public string[] meshes;
        [Key("textures")] public string[] textures;
        [Key("materials")] public string[] materials;
    }

    [MessagePackObject]
    public class AssetRequestPayload
    {
        [Key("version")] public int version;
        [Key("requestId")] public string requestId;
        [Key("sceneHash")] public string sceneHash;
        [Key("meshes")] public string[] meshes;
        [Key("textures")] public string[] textures;
        [Key("materials")] public string[] materials;
    }

    [MessagePackObject]
    public class AssetResponsePayload
    {
        [Key("version")] public int version;
        [Key("requestId")] public string requestId;
        [Key("sceneHash")] public string sceneHash;
        [Key("assets")] public SimAssetBundle assets;
        [Key("missing")] public AssetHashSetPayload missing;
    }

    [MessagePackObject]
    public class StreamMessage
    {
        [Key("data")] public Dictionary<string, List<float>> data;
    }
}

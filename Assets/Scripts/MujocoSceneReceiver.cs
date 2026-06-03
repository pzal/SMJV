using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MessagePack;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace SMJV
{
    public class MujocoSceneReceiver : MonoBehaviour
    {
        [SerializeField] private int port = 8765;
        [SerializeField] private Transform simRoot;
        [SerializeField] private GameObject originGizmo;
        [SerializeField] private InfoWindow infoWindow;
        [SerializeField] private Material opaqueBaseMaterial;
        [SerializeField] private Material transparentBaseMaterial;

        private WebSocketServer _server;
        private readonly ConcurrentQueue<byte[]> _inbox = new();

        private GameObject _sceneRoot;
        private SimSceneLoader _sceneLoader;
        private Dictionary<string, Transform> _objectsTrans;
        private readonly SimAssetCache _assetCache = new();
        private SceneManifestPayload _pendingManifest;
        private string _pendingAssetRequestId;
        private int _assetRequestSerial;
        private bool _logFirstPose;

        private string _addressString = "";
        private volatile bool _connectionChanged;
        private bool _isConnected;
        private int _outboundInputCount;
        private int _inboundPosesCount;
        private float _rateWindowStart;
        private float _lastOutboundHz;
        private float _lastInboundHz;

        // Pose timing diagnostics
        private float _lastPoseTime = -1f;
        private float _maxPoseGapMs;
        private float _lastMaxPoseGapMs;
        private float _lastMaxJitterMs;

        [MessagePackObject]
        public class Envelope
        {
            [Key("type")] public string type;
            [Key("data")] public byte[] data;
        }

        [MessagePackObject]
        public class ScenePayload
        {
            [Key("config")] public SimSceneConfig config;
            [Key("objects")] public SimObject[] objects;
        }

        private class SimBridge : WebSocketBehavior
        {
            public MujocoSceneReceiver Owner;
            protected override void OnOpen()
            {
                Debug.Log($"[Recv] OnOpen from {Context.UserEndPoint} id={ID}");
                // Single-publisher policy: kick any other session so the new
                // client wins. Saves us from two Pythons clobbering each other.
                foreach (var otherId in Sessions.IDs)
                {
                    if (otherId == ID) continue;
                    Debug.Log($"[Recv] Replacing prior session {otherId}");
                    Sessions.CloseSession(otherId);
                }
                Owner._connectionChanged = true;
            }
            protected override void OnClose(CloseEventArgs e)
            {
                Debug.Log($"[Recv] OnClose id={ID} code={e.Code} reason='{e.Reason}' clean={e.WasClean}");
                Owner._connectionChanged = true;
            }
            protected override void OnError(ErrorEventArgs e)
            {
                Debug.LogError($"[Recv] OnError id={ID}: {e.Message}");
            }
            protected override void OnMessage(MessageEventArgs e)
            {
                if (e.IsBinary) Owner._inbox.Enqueue(e.RawData);
            }
        }

        void Start()
        {
            _server = new WebSocketServer(System.Net.IPAddress.Any, port);
            _server.AddWebSocketService<SimBridge>("/sim", b => b.Owner = this);
            _server.Start();
            Debug.Log($"MujocoSceneReceiver listening on ws://*:{port}/sim");

            var ips = GetExternalIPv4Addresses();
            var addr = ips.Count == 0 ? "<no IPv4>" : string.Join(", ", ips);
            _addressString = $"{addr}:{port}";
            UpdateInfoWindow();
        }

        void UpdateInfoWindow()
        {
            if (infoWindow == null) return;
            var status = _isConnected ? "connected" : "waiting";
            infoWindow.SetText(
                $"ws://{_addressString}/sim\n" +
                $"status: {status}\n" +
                $"in: {_lastInboundHz:F1} Hz   out: {_lastOutboundHz:F1} Hz\n" +
                $"max gap: {_lastMaxPoseGapMs:F0} ms  jitter: +{_lastMaxJitterMs:F0} ms"
            );
        }

        static List<string> GetExternalIPv4Addresses()
        {
            var result = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(ua.Address.ToString());
                }
            }
            return result;
        }

        void OnDestroy()
        {
            if (_server != null && _server.IsListening) _server.Stop();
            _assetCache.Clear();
        }

        public bool TryBroadcast(byte[] payload)
        {
            var host = _server?.WebSocketServices?["/sim"];
            if (host == null || host.Sessions.Count == 0) return false;
            host.Sessions.Broadcast(payload);
            _outboundInputCount++;
            return true;
        }

        private float _addressRefreshTime;

        void Update()
        {
            if (infoWindow != null && infoWindow.IsVisible &&
                Time.unscaledTime - _addressRefreshTime >= 1f)
            {
                _addressRefreshTime = Time.unscaledTime;
                var ips = GetExternalIPv4Addresses();
                var addr = ips.Count == 0 ? "<no IPv4>" : string.Join(", ", ips);
                _addressString = $"{addr}:{port}";
                UpdateInfoWindow();
            }

            while (_inbox.TryDequeue(out var bytes))
            {
                try { Handle(bytes); }
                catch (Exception ex) { Debug.LogError($"[Recv] dispatch failed (first {bytes.Length}B): {ex}"); }
            }

            if (_connectionChanged)
            {
                _connectionChanged = false;
                var sessionCount = _server?.WebSocketServices?["/sim"]?.Sessions.Count ?? 0;
                var nowConnected = sessionCount > 0;
                if (nowConnected != _isConnected)
                {
                    _isConnected = nowConnected;
                    _outboundInputCount = 0;
                    _inboundPosesCount = 0;
                    _lastOutboundHz = 0;
                    _lastInboundHz = 0;
                    _lastPoseTime = -1f;
                    _maxPoseGapMs = 0f;
                    _lastMaxPoseGapMs = 0f;
                    _rateWindowStart = Time.unscaledTime;
                    UpdateInfoWindow();
                }
            }

            if (_isConnected)
            {
                var elapsed = Time.unscaledTime - _rateWindowStart;
                if (elapsed >= 1f)
                {
                    _lastOutboundHz = _outboundInputCount / elapsed;
                    _lastInboundHz = _inboundPosesCount / elapsed;
                    _lastMaxPoseGapMs = _maxPoseGapMs;
                    var idealPeriodMs = _lastInboundHz > 0f ? 1000f / _lastInboundHz : 0f;
                    _lastMaxJitterMs = _lastInboundHz > 0f ? _maxPoseGapMs - idealPeriodMs : 0f;
                    _outboundInputCount = 0;
                    _inboundPosesCount = 0;
                    _maxPoseGapMs = 0f;
                    _rateWindowStart = Time.unscaledTime;
                    Debug.Log($"[Recv] poses {_lastInboundHz:F1} Hz  max gap {_lastMaxPoseGapMs:F0} ms  jitter +{_lastMaxJitterMs:F0} ms");
                    UpdateInfoWindow();
                }
            }
        }

        void Handle(byte[] bytes)
        {
            var env = MessagePackSerializer.Deserialize<Envelope>(bytes);
            // Skip per-pose logs (too spammy); other envelope types log once each.
            if (env.type != "poses")
                Debug.Log($"[Recv] envelope type={env.type} dataLen={(env.data?.Length ?? 0)}");
            switch (env.type)
            {
                case "scene":
                    var scene = MessagePackSerializer.Deserialize<ScenePayload>(env.data);
                    SpawnScene(scene);
                    break;
                case "scene_manifest":
                    var manifest = MessagePackSerializer.Deserialize<SceneManifestPayload>(env.data);
                    HandleSceneManifest(manifest);
                    break;
                case "asset_response":
                    var assetResponse = MessagePackSerializer.Deserialize<AssetResponsePayload>(env.data);
                    HandleAssetResponse(assetResponse);
                    break;
                case "poses":
                    var stream = MessagePackSerializer.Deserialize<StreamMessage>(env.data);
                    var now = Time.unscaledTime;
                    if (_lastPoseTime >= 0f)
                    {
                        var gapMs = (now - _lastPoseTime) * 1000f;
                        if (gapMs > _maxPoseGapMs) _maxPoseGapMs = gapMs;
                    }
                    _lastPoseTime = now;
                    ApplyPoses(stream);
                    _inboundPosesCount++;
                    break;
                case "clear":
                    ClearScene(clearAssets: true);
                    break;
                default:
                    Debug.LogWarning($"[Recv] Unknown envelope type: {env.type}");
                    break;
            }
        }

        void SpawnScene(ScenePayload scene)
        {
            var prevName = _sceneRoot != null ? _sceneRoot.name : "<null>";
            Debug.Log($"[Recv] SpawnScene name={scene.config.name} previous={prevName} objectCount={scene.objects?.Length ?? 0}");
            ClearScene(clearAssets: true);
            _sceneLoader = CreateSceneRoot(scene.config.name);

            foreach (var obj in scene.objects)
                _sceneLoader.CreateSimObject(obj);

            _objectsTrans = _sceneLoader.GetObjectsTrans();
            Debug.Log($"[Recv] SpawnScene done: rootChildren={_sceneRoot.transform.childCount} objectsTrans={_objectsTrans?.Count ?? 0}");
            _logFirstPose = true;
        }

        SimSceneLoader CreateSceneRoot(string name)
        {
            var parent = simRoot != null ? simRoot : transform;
            _sceneRoot = new GameObject(name);
            _sceneRoot.transform.SetParent(parent, false);
            if (originGizmo != null) originGizmo.SetActive(false);
            var loader = _sceneRoot.AddComponent<SimSceneLoader>();
            loader.Configure(opaqueBaseMaterial, transparentBaseMaterial);
            loader.SetAssetCache(_assetCache);
            return loader;
        }

        void HandleSceneManifest(SceneManifestPayload scene)
        {
            if (scene == null || scene.config == null)
            {
                Debug.LogWarning("[Recv] scene_manifest payload missing config; ignoring.");
                return;
            }

            _pendingManifest = null;
            _pendingAssetRequestId = null;

            SimAssetCache.AssetRefs missing = _assetCache.FindMissingAssets(scene.objects);
            if (_assetCache.HasMissingAssets(missing))
            {
                RequestMissingAssets(scene, missing);
                return;
            }

            ApplySceneManifest(scene);
        }

        void ApplySceneManifest(SceneManifestPayload scene)
        {
            if (_sceneRoot != null && _sceneRoot.name != scene.config.name)
            {
                Debug.Log($"[Recv] scene_manifest scene name changed {_sceneRoot.name} -> {scene.config.name}; recreating root.");
                ClearScene(clearAssets: false);
            }

            if (_sceneRoot == null)
            {
                _sceneLoader = CreateSceneRoot(scene.config.name);
            }
            else
            {
                if (_sceneLoader == null)
                {
                    _sceneLoader = _sceneRoot.GetComponent<SimSceneLoader>();
                }
                _sceneLoader.SetAssetCache(_assetCache);
            }

            _sceneLoader.ReconcileScene(scene.config, scene.objects);
            _objectsTrans = _sceneLoader.GetObjectsTrans();
            Debug.Log(
                $"[Recv] ReconcileScene name={scene.config.name} hash={scene.sceneHash} objects={scene.objects?.Length ?? 0} objectsTrans={_objectsTrans?.Count ?? 0}"
            );
            _logFirstPose = true;
        }

        void RequestMissingAssets(SceneManifestPayload scene, SimAssetCache.AssetRefs missing)
        {
            string requestId = (++_assetRequestSerial).ToString();
            _pendingManifest = scene;
            _pendingAssetRequestId = requestId;

            var request = new AssetRequestPayload
            {
                version = 1,
                requestId = requestId,
                sceneHash = scene.sceneHash,
                meshes = ToArray(missing.Meshes),
                textures = ToArray(missing.Textures),
                materials = ToArray(missing.Materials),
            };

            var envelope = new Envelope
            {
                type = "asset_request",
                data = MessagePackSerializer.Serialize(request)
            };
            bool sent = TryBroadcast(MessagePackSerializer.Serialize(envelope));
            Debug.Log(
                $"[Recv] asset_request id={requestId} sceneHash={scene.sceneHash} meshes={request.meshes.Length} textures={request.textures.Length} materials={request.materials.Length} sent={sent}"
            );

            if (!sent)
            {
                Debug.LogError("[Recv] asset_request could not be sent; keeping previous scene alive.");
                _pendingManifest = null;
                _pendingAssetRequestId = null;
            }
        }

        void HandleAssetResponse(AssetResponsePayload response)
        {
            if (_pendingManifest == null ||
                response == null ||
                response.requestId != _pendingAssetRequestId ||
                response.sceneHash != _pendingManifest.sceneHash)
            {
                Debug.LogWarning(
                    $"[Recv] Ignoring stale asset_response id={response?.requestId} sceneHash={response?.sceneHash}"
                );
                return;
            }

            if (HasMissing(response.missing))
            {
                Debug.LogError(
                    $"[Recv] asset_response id={response.requestId} sceneHash={response.sceneHash} unresolved missing(mesh={Count(response.missing.meshes)}, tex={Count(response.missing.textures)}, mat={Count(response.missing.materials)}); keeping previous scene alive."
                );
                _pendingManifest = null;
                _pendingAssetRequestId = null;
                return;
            }

            _assetCache.RegisterAssets(response.assets);
            SimAssetCache.AssetRefs stillMissing = _assetCache.FindMissingAssets(_pendingManifest.objects);
            if (_assetCache.HasMissingAssets(stillMissing))
            {
                Debug.LogError(
                    $"[Recv] asset_response id={response.requestId} did not populate all requested assets; still missing(mesh={stillMissing.Meshes.Count}, tex={stillMissing.Textures.Count}, mat={stillMissing.Materials.Count})."
                );
                _pendingManifest = null;
                _pendingAssetRequestId = null;
                return;
            }

            SceneManifestPayload readyScene = _pendingManifest;
            _pendingManifest = null;
            _pendingAssetRequestId = null;
            ApplySceneManifest(readyScene);
        }

        static string[] ToArray(HashSet<string> hashes)
        {
            var list = new List<string>(hashes);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        static bool HasMissing(AssetHashSetPayload missing)
        {
            return missing != null &&
                   (Count(missing.meshes) > 0 ||
                    Count(missing.textures) > 0 ||
                    Count(missing.materials) > 0);
        }

        static int Count(string[] values)
        {
            return values?.Length ?? 0;
        }

        void ClearScene(bool clearAssets = true)
        {
            _pendingManifest = null;
            _pendingAssetRequestId = null;
            if (_sceneRoot != null)
            {
                Debug.Log($"[Recv] ClearScene name={_sceneRoot.name} clearAssets={clearAssets}");
                // DestroyImmediate so SimSceneLoader.OnDestroy (which unregisters
                // services on our IrisStub's LocalInfo) runs synchronously, BEFORE
                // we instantiate the next scene with the same service names.
                DestroyImmediate(_sceneRoot);
                _sceneRoot = null;
                _sceneLoader = null;
                _objectsTrans = null;
            }
            if (originGizmo != null) originGizmo.SetActive(true);

            if (clearAssets)
            {
                _assetCache.Clear();
                // IRIS-Viz creates runtime Mesh/Texture2D/Material assets in
                // SimSceneLoader.BuildMesh / BuildTexture and SimMaterialResolver,
                // none of which it destroys.  GC managed-only refs first so
                // UnloadUnusedAssets sees them as unreachable.
                System.GC.Collect();
                Resources.UnloadUnusedAssets();
            }
        }

        void ApplyPoses(StreamMessage msg)
        {
            if (_objectsTrans == null || _sceneRoot == null) return;
            var rootTrans = _sceneRoot.transform;
            int matched = 0;
            foreach (var (name, v) in msg.data)
            {
                if (!_objectsTrans.TryGetValue(name, out var t)) continue;
                matched++;
                t.position = rootTrans.TransformPoint(new Vector3(v[0], v[1], v[2]));
                t.rotation = rootTrans.rotation * new Quaternion(v[3], v[4], v[5], v[6]);
            }
            if (_logFirstPose)
            {
                Debug.Log($"[Recv] ApplyPoses (first after spawn): incoming={msg.data?.Count ?? 0} matched={matched}");
                _logFirstPose = false;
            }
        }
    }
}

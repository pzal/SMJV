using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using IRIS.SceneLoader;
using MessagePack;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace SMJV
{
    public class MujocoSceneReceiver : MonoBehaviour
    {
        [SerializeField] private int port = 8765;
        [SerializeField] private GameObject simScenePrefab;

        private WebSocketServer _server;
        private readonly ConcurrentQueue<byte[]> _inbox = new();

        private GameObject _sceneRoot;
        private SimSceneLoader _sceneLoader;
        private Dictionary<string, Transform> _objectsTrans;
        private bool _logFirstPose;

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
            }
            protected override void OnClose(CloseEventArgs e)
            {
                Debug.Log($"[Recv] OnClose id={ID} code={e.Code} reason='{e.Reason}' clean={e.WasClean}");
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
        }

        void OnDestroy()
        {
            if (_server != null && _server.IsListening) _server.Stop();
        }

        void Update()
        {
            while (_inbox.TryDequeue(out var bytes))
            {
                try { Handle(bytes); }
                catch (Exception ex) { Debug.LogError($"[Recv] dispatch failed (first {bytes.Length}B): {ex}"); }
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
                case "poses":
                    var stream = MessagePackSerializer.Deserialize<StreamMessage>(env.data);
                    ApplyPoses(stream);
                    break;
                case "clear":
                    ClearScene();
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
            ClearScene();
            _sceneRoot = Instantiate(simScenePrefab, transform);
            _sceneRoot.name = scene.config.name;
            _sceneLoader = _sceneRoot.GetComponent<SimSceneLoader>();
            _sceneLoader.InitializeServices(scene.config.name);

            foreach (var obj in scene.objects)
                _sceneLoader.CreateSimObject(obj);

            _objectsTrans = _sceneLoader.GetObjectsTrans();
            Debug.Log($"[Recv] SpawnScene done: rootChildren={_sceneRoot.transform.childCount} objectsTrans={_objectsTrans?.Count ?? 0}");
            _logFirstPose = true;
        }

        void ClearScene()
        {
            if (_sceneRoot == null) return;
            Debug.Log($"[Recv] ClearScene name={_sceneRoot.name}");
            // DestroyImmediate so SimSceneLoader.OnDestroy (which unregisters
            // services on our IrisStub's LocalInfo) runs synchronously, BEFORE
            // we instantiate the next scene with the same service names.
            DestroyImmediate(_sceneRoot);
            _sceneRoot = null;
            _sceneLoader = null;
            _objectsTrans = null;
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

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
                catch (Exception ex) { Debug.LogError($"MujocoSceneReceiver dispatch failed: {ex}"); }
            }
        }

        void Handle(byte[] bytes)
        {
            var env = MessagePackSerializer.Deserialize<Envelope>(bytes);
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
                default:
                    Debug.LogWarning($"Unknown envelope type: {env.type}");
                    break;
            }
        }

        void SpawnScene(ScenePayload scene)
        {
            if (_sceneRoot != null) Destroy(_sceneRoot);
            _sceneRoot = Instantiate(simScenePrefab, transform);
            _sceneRoot.name = scene.config.name;
            _sceneLoader = _sceneRoot.GetComponent<SimSceneLoader>();
            _sceneLoader.InitializeServices(scene.config.name);

            foreach (var obj in scene.objects)
                _sceneLoader.CreateSimObject(obj);

            _objectsTrans = _sceneLoader.GetObjectsTrans();
        }

        void ApplyPoses(StreamMessage msg)
        {
            if (_objectsTrans == null) return;
            var rootTrans = _sceneRoot.transform;
            foreach (var (name, v) in msg.data)
            {
                if (!_objectsTrans.TryGetValue(name, out var t)) continue;
                t.position = rootTrans.TransformPoint(new Vector3(v[0], v[1], v[2]));
                t.rotation = rootTrans.rotation * new Quaternion(v[3], v[4], v[5], v[6]);
            }
        }
    }
}

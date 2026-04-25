using System.Reflection;
using System.Runtime.Serialization;
using IRIS.Node;
using IRIS.Utilities;
using UnityEngine;

namespace SMJV
{
    // Replaces the IRISNode prefab WITHOUT subclassing IRISXRNode (which would
    // still run its private Start() — Unity dispatches to private base messages
    // on inherited components). Instead, we create an IRISXRNode object via
    // FormatterServices.GetUninitializedObject (no constructor, no Unity
    // lifecycle) and assign it to Singleton<IRISXRNode>.Instance via reflection.
    // SimSceneSpawner / SimSceneLoader resolve IRISXRNode.Instance to our
    // hollow object whose ServiceManager / SubscriberManager / localInfo we
    // pre-populated so RegisterServiceCallback runs as a no-op.
    [DefaultExecutionOrder(-200)]  // earlier than SimSceneSpawner (-100)
    public class IrisStub : MonoBehaviour
    {
        void Awake()
        {
            // Allocate IRISXRNode without running its (Unity-coupled) ctor or Start().
            var node = (IRISXRNode)FormatterServices.GetUninitializedObject(typeof(IRISXRNode));

            // localInfo is a public auto-property; nodeInfo + service dict
            // are real types we can construct cleanly (no ZMQ).
            node.localInfo = new LocalInfo("SMJV/Stub");

            // ServiceManager has only one ctor and it binds a ZMQ socket.
            // Allocate without ctor; init the inline-initialized fields.
            var svc = (ServiceManager)FormatterServices.GetUninitializedObject(typeof(ServiceManager));
            SetField(svc, "_serviceDict", new System.Collections.Generic.Dictionary<string, IService>());
            SetField(svc, "_serviceLock", new object());
            SetBackingField(node, nameof(ServiceManager), svc);

            // SubscriberManager has a default ctor that only initializes a dict.
            SetBackingField(node, nameof(SubscriberManager), new SubscriberManager());

            // Singleton<IRISXRNode>.Instance has a private setter; set the
            // backing field directly. The getter on Singleton<T> walks up the
            // type hierarchy to find the static Instance property.
            var singletonType = typeof(IRISXRNode).BaseType; // Singleton<IRISXRNode>
            var instanceBacking = singletonType.GetField("<Instance>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceBacking.SetValue(null, node);
        }

        static void SetField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            f.SetValue(target, value);
        }

        static void SetBackingField(object target, string propName, object value)
        {
            var f = typeof(IRISXRNode).GetField($"<{propName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            f.SetValue(target, value);
        }
    }
}

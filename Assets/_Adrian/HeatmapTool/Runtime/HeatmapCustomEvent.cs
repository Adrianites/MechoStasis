using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace HeatmapTool
{
    public enum HeatmapWatchKind
    {
        None,
        Event,
        Bool,
        Number,
        Enum,
    }

    public enum HeatmapTrigger
    {
        WhenRaised,
        BecomesTrue,
        BecomesFalse,
        Changes,
        Increases,
        Decreases,
        RisesAbove,
        FallsBelow,
        Becomes,
    }

    public enum HeatmapEventLocation
    {
        Player,
        WatchedObject,
    }

    [Serializable]
    public sealed class HeatmapCustomEvent
    {
        public string eventName = "";
        public GameObject target;
        public Component component;
        public string member = "";
        public HeatmapTrigger trigger;
        public float value;
        public HeatmapEventLocation location;
        [Min(0f)] public float cooldown;

        [NonSerialized] HeatmapTracker tracker;
        [NonSerialized] Func<double> read;
        [NonSerialized] double previous;
        [NonSerialized] Action unsubscribe;
        [NonSerialized] float lastFired;

        public string Name => string.IsNullOrWhiteSpace(eventName) ? member : eventName;

        internal void Start(HeatmapTracker owner)
        {
            Stop();
            tracker = owner;
            lastFired = float.NegativeInfinity;
            if (component == null || string.IsNullOrEmpty(member)) return;

            if (!HeatmapWatch.TryFind(component.GetType(), member, out HeatmapWatchable watchable))
            {
                Debug.LogWarning($"[Heatmap] Custom event \"{Name}\": {component.GetType().Name} has nothing called \"{member}\" to watch.", owner);
                return;
            }

            try
            {
                if (watchable.kind == HeatmapWatchKind.Event)
                {
                    unsubscribe = HeatmapWatch.Subscribe(component, watchable.member, Fire);
                    if (unsubscribe == null)
                        Debug.LogWarning($"[Heatmap] Custom event \"{Name}\": couldn't listen to {component.GetType().Name}.{member}.", owner);
                }
                else
                {
                    read = HeatmapWatch.CreateReader(component, watchable.member);
                    previous = read();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Heatmap] Custom event \"{Name}\": couldn't watch {component.GetType().Name}.{member}: {e.Message}", owner);
                read = null;
            }
        }

        internal void Stop()
        {
            try { unsubscribe?.Invoke(); }
            catch (Exception) { }
            unsubscribe = null;
            read = null;
            tracker = null;
        }

        internal void Poll()
        {
            if (read == null || component == null) return;

            double current;
            try
            {
                current = read();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Heatmap] Custom event \"{Name}\" stopped: reading {member} failed: {e.Message}", tracker);
                read = null;
                return;
            }

            double before = previous;
            previous = current;
            if (HeatmapWatch.Evaluate(trigger, before, current, value, out float eventValue)) Fire(eventValue);
        }

        void Fire(float eventValue)
        {
            if (tracker == null || !tracker.IsRecording) return;

            float now = Time.time;
            if (now - lastFired < cooldown) return;
            lastFired = now;

            if (location == HeatmapEventLocation.WatchedObject && component != null)
                tracker.RecordEvent(Name, component.transform.position, eventValue);
            else
                tracker.RecordEvent(Name, eventValue);
        }
    }

    public readonly struct HeatmapWatchable
    {
        public readonly string name;
        public readonly HeatmapWatchKind kind;
        public readonly Type valueType;
        public readonly MemberInfo member;

        public HeatmapWatchable(string name, HeatmapWatchKind kind, Type valueType, MemberInfo member)
        {
            this.name = name;
            this.kind = kind;
            this.valueType = valueType;
            this.member = member;
        }

        public string TypeLabel
        {
            get
            {
                if (kind == HeatmapWatchKind.Event) return "event";
                if (kind == HeatmapWatchKind.Enum) return valueType.Name;
                return HeatmapWatch.Alias(valueType);
            }
        }
    }

    public static class HeatmapWatch
    {
        const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

        static readonly Type[] StopTypes = { typeof(Object), typeof(Component), typeof(Behaviour), typeof(MonoBehaviour) };
        static readonly Dictionary<Type, List<HeatmapWatchable>> cache = new Dictionary<Type, List<HeatmapWatchable>>();

        static readonly HeatmapTrigger[] EventTriggers = { HeatmapTrigger.WhenRaised };
        static readonly HeatmapTrigger[] BoolTriggers = { HeatmapTrigger.BecomesTrue, HeatmapTrigger.BecomesFalse, HeatmapTrigger.Changes };
        static readonly HeatmapTrigger[] NumberTriggers =
        {
            HeatmapTrigger.Changes, HeatmapTrigger.Increases, HeatmapTrigger.Decreases,
            HeatmapTrigger.RisesAbove, HeatmapTrigger.FallsBelow, HeatmapTrigger.Becomes,
        };
        static readonly HeatmapTrigger[] EnumTriggers = { HeatmapTrigger.Changes, HeatmapTrigger.Becomes };

        public static IReadOnlyList<HeatmapWatchable> GetMembers(Type type)
        {
            if (cache.TryGetValue(type, out List<HeatmapWatchable> cached)) return cached;

            var result = new List<HeatmapWatchable>();
            var seen = new HashSet<string>();
            for (Type t = type; t != null && Array.IndexOf(StopTypes, t) < 0; t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(AllInstance))
                {
                    if (field.Name.IndexOf('<') >= 0 || IsObsolete(field)) continue;
                    HeatmapWatchKind kind = KindOf(field.FieldType);
                    if (kind != HeatmapWatchKind.None && seen.Add(field.Name))
                        result.Add(new HeatmapWatchable(field.Name, kind, field.FieldType, field));
                }

                foreach (PropertyInfo property in t.GetProperties(PublicInstance))
                {
                    if (property.GetGetMethod() == null || property.GetIndexParameters().Length > 0 || IsObsolete(property)) continue;
                    HeatmapWatchKind kind = KindOf(property.PropertyType);
                    if (kind != HeatmapWatchKind.None && seen.Add(property.Name))
                        result.Add(new HeatmapWatchable(property.Name, kind, property.PropertyType, property));
                }

                foreach (EventInfo info in t.GetEvents(PublicInstance))
                {
                    if (IsObsolete(info) || HandlerFor(info.EventHandlerType) == null) continue;
                    if (seen.Add(info.Name))
                        result.Add(new HeatmapWatchable(info.Name, HeatmapWatchKind.Event, info.EventHandlerType, info));
                }
            }

            result.Sort((a, b) =>
            {
                bool aEvent = a.kind == HeatmapWatchKind.Event, bEvent = b.kind == HeatmapWatchKind.Event;
                if (aEvent != bEvent) return aEvent ? -1 : 1;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });
            cache[type] = result;
            return result;
        }

        public static bool TryFind(Type type, string name, out HeatmapWatchable result)
        {
            foreach (HeatmapWatchable watchable in GetMembers(type))
            {
                if (watchable.name != name) continue;
                result = watchable;
                return true;
            }
            result = default;
            return false;
        }

        public static IReadOnlyList<HeatmapTrigger> TriggersFor(HeatmapWatchKind kind)
        {
            switch (kind)
            {
                case HeatmapWatchKind.Event: return EventTriggers;
                case HeatmapWatchKind.Bool: return BoolTriggers;
                case HeatmapWatchKind.Number: return NumberTriggers;
                case HeatmapWatchKind.Enum: return EnumTriggers;
                default: return Array.Empty<HeatmapTrigger>();
            }
        }

        public static bool NeedsValue(HeatmapTrigger trigger)
        {
            return trigger == HeatmapTrigger.RisesAbove || trigger == HeatmapTrigger.FallsBelow || trigger == HeatmapTrigger.Becomes;
        }

        public static bool Evaluate(HeatmapTrigger trigger, double before, double now, double target, out float eventValue)
        {
            eventValue = (float)now;
            if (double.IsNaN(before) || double.IsNaN(now) || before == now) return false;

            switch (trigger)
            {
                case HeatmapTrigger.BecomesTrue:
                    eventValue = 1f;
                    return before == 0 && now != 0;
                case HeatmapTrigger.BecomesFalse:
                    eventValue = 0f;
                    return before != 0 && now == 0;
                case HeatmapTrigger.Changes:
                    return true;
                case HeatmapTrigger.Increases:
                    eventValue = (float)(now - before);
                    return now > before;
                case HeatmapTrigger.Decreases:
                    eventValue = (float)(before - now);
                    return now < before;
                case HeatmapTrigger.RisesAbove:
                    return before <= target && now > target;
                case HeatmapTrigger.FallsBelow:
                    return before >= target && now < target;
                case HeatmapTrigger.Becomes:
                    return Math.Abs(now - target) < 1e-4 && Math.Abs(before - target) >= 1e-4;
                default:
                    return false;
            }
        }

        public static Func<double> CreateReader(object owner, MemberInfo member)
        {
            if (member is FieldInfo field) return () => ToDouble(field.GetValue(owner));

            var property = (PropertyInfo)member;
            MethodInfo getter = property.GetGetMethod();
            Type type = property.PropertyType;
            if (type == typeof(bool))
            {
                var get = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), owner, getter);
                return () => get() ? 1.0 : 0.0;
            }
            if (type == typeof(int))
            {
                var get = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), owner, getter);
                return () => get();
            }
            if (type == typeof(float))
            {
                var get = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), owner, getter);
                return () => get();
            }
            return () => ToDouble(property.GetValue(owner));
        }

        public static Action Subscribe(object owner, MemberInfo member, Action<float> onRaised)
        {
            var relay = new Relay(onRaised);

            if (member is EventInfo info)
            {
                Delegate handler = CreateHandler(info.EventHandlerType, relay);
                if (handler == null) return null;
                info.AddEventHandler(owner, handler);
                return () => info.RemoveEventHandler(owner, handler);
            }

            object unityEvent = member is FieldInfo field ? field.GetValue(owner)
                : member is PropertyInfo property ? property.GetValue(owner)
                : null;
            if (unityEvent == null) return null;

            MethodInfo add = FindListenerMethod(unityEvent.GetType(), "AddListener");
            MethodInfo remove = FindListenerMethod(unityEvent.GetType(), "RemoveListener");
            if (add == null || remove == null) return null;

            Delegate listener = CreateHandler(add.GetParameters()[0].ParameterType, relay);
            if (listener == null) return null;
            add.Invoke(unityEvent, new object[] { listener });
            return () => remove.Invoke(unityEvent, new object[] { listener });
        }

        internal static string Alias(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(long)) return "long";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(sbyte)) return "sbyte";
            return type.Name;
        }

        static HeatmapWatchKind KindOf(Type type)
        {
            if (type == typeof(bool)) return HeatmapWatchKind.Bool;
            if (type.IsEnum) return HeatmapWatchKind.Enum;
            if (type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long)
                || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ushort)
                || type == typeof(sbyte))
                return HeatmapWatchKind.Number;
            if (typeof(UnityEventBase).IsAssignableFrom(type))
            {
                MethodInfo add = FindListenerMethod(type, "AddListener");
                if (add != null && HandlerFor(add.GetParameters()[0].ParameterType) != null) return HeatmapWatchKind.Event;
            }
            return HeatmapWatchKind.None;
        }

        static bool IsObsolete(MemberInfo member)
        {
            return member.IsDefined(typeof(ObsoleteAttribute), true);
        }

        static double ToDouble(object value)
        {
            if (value is bool b) return b ? 1.0 : 0.0;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        static MethodInfo FindListenerMethod(Type eventType, string name)
        {
            foreach (MethodInfo method in eventType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.Name != name) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && typeof(Delegate).IsAssignableFrom(parameters[0].ParameterType)) return method;
            }
            return null;
        }

        static MethodInfo HandlerFor(Type delegateType)
        {
            if (delegateType == null || !typeof(Delegate).IsAssignableFrom(delegateType)) return null;
            MethodInfo invoke = delegateType.GetMethod("Invoke");
            if (invoke == null || invoke.ReturnType != typeof(void)) return null;

            ParameterInfo[] parameters = invoke.GetParameters();
            string name = null;
            if (parameters.Length == 0)
            {
                name = nameof(Relay.OnRaised);
            }
            else if (parameters.Length == 1)
            {
                Type p = parameters[0].ParameterType;
                if (p.IsByRef) return null;
                if (p == typeof(int)) name = nameof(Relay.OnInt);
                else if (p == typeof(float)) name = nameof(Relay.OnFloat);
                else if (p == typeof(double)) name = nameof(Relay.OnDouble);
                else if (p == typeof(long)) name = nameof(Relay.OnLong);
                else if (p == typeof(bool)) name = nameof(Relay.OnBool);
                else if (!p.IsValueType) name = nameof(Relay.OnObject);
            }
            else if (parameters.Length == 2 && !parameters[0].ParameterType.IsValueType && !parameters[1].ParameterType.IsValueType
                     && !parameters[0].ParameterType.IsByRef && !parameters[1].ParameterType.IsByRef)
            {
                name = nameof(Relay.OnObjects);
            }
            return name == null ? null : typeof(Relay).GetMethod(name);
        }

        static Delegate CreateHandler(Type delegateType, Relay relay)
        {
            MethodInfo method = HandlerFor(delegateType);
            return method == null ? null : Delegate.CreateDelegate(delegateType, relay, method, false);
        }

        sealed class Relay
        {
            readonly Action<float> onRaised;

            public Relay(Action<float> onRaised)
            {
                this.onRaised = onRaised;
            }

            public void OnRaised() => onRaised(0f);
            public void OnObject(object a) => onRaised(0f);
            public void OnObjects(object a, object b) => onRaised(0f);
            public void OnInt(int v) => onRaised(v);
            public void OnFloat(float v) => onRaised(v);
            public void OnDouble(double v) => onRaised((float)v);
            public void OnLong(long v) => onRaised(v);
            public void OnBool(bool v) => onRaised(v ? 1f : 0f);
        }
    }
}

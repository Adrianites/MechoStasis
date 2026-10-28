using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HeatmapTool
{
    [CustomPropertyDrawer(typeof(HeatmapCustomEvent))]
    sealed class HeatmapCustomEventDrawer : PropertyDrawer
    {
        static readonly GUIContent NameLabel = new GUIContent("Event Name", "The name this event gets in the Heatmap Viewer.");
        static readonly GUIContent TargetLabel = new GUIContent("Object",
            "Drag in the object that has the script you want to watch. Leave empty to use this player.");
        static readonly GUIContent WatchLabel = new GUIContent("Watch",
            "An event (UnityEvent or C# event) records every time it fires. A value (bool, number or enum) records when it changes the way you pick below.");
        static readonly GUIContent TriggerLabel = new GUIContent("Record When");
        static readonly GUIContent ValueLabel = new GUIContent("Value");
        static readonly GUIContent LocationLabel = new GUIContent("Record At",
            "Player: where the player is when it happens. Watched Object: where the watched object is.");
        static readonly GUIContent CooldownLabel = new GUIContent("Cooldown (s)",
            "Ignore repeats within this many seconds, e.g. for a value that changes every frame.");

        static float Line => EditorGUIUtility.singleLineHeight;
        static float Gap => EditorGUIUtility.standardVerticalSpacing;

        struct State
        {
            public GameObject owner;
            public Component component;
            public string member;
            public bool found;
            public HeatmapWatchable watchable;
            public string problem;
            public bool showTrigger;
            public bool showValue;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return Line;

            State state = Inspect(property);
            int rows = 6;
            if (state.showTrigger) rows++;
            if (state.showValue) rows++;
            float height = rows * (Line + Gap) - Gap;
            if (state.problem != null) height += Gap + Line * 2f;
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            State state = Inspect(property);

            SerializedProperty nameProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.eventName));
            SerializedProperty targetProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.target));
            SerializedProperty componentProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.component));
            SerializedProperty memberProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.member));
            SerializedProperty triggerProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.trigger));
            SerializedProperty valueProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.value));

            var row = new Rect(position.x, position.y, position.width, Line);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, Header(nameProp, triggerProp, state), true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;

            NextRow(ref row);
            EditorGUI.PropertyField(row, nameProp, NameLabel);

            NextRow(ref row);
            EditorGUI.BeginChangeCheck();
            EditorGUI.ObjectField(row, targetProp, typeof(GameObject), TargetLabel);
            if (EditorGUI.EndChangeCheck())
            {
                GameObject newOwner = OwnerOf(property, targetProp.objectReferenceValue as GameObject);
                var current = componentProp.objectReferenceValue as Component;
                if (current != null && current.gameObject != newOwner)
                {
                    componentProp.objectReferenceValue = null;
                    memberProp.stringValue = "";
                }
            }

            NextRow(ref row);
            Rect button = EditorGUI.PrefixLabel(row, WatchLabel);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            if (EditorGUI.DropdownButton(button, new GUIContent(WatchText(state)), FocusType.Keyboard))
                ShowWatchMenu(button, property, state);
            EditorGUI.indentLevel = indent;

            if (state.showTrigger)
            {
                NextRow(ref row);
                IReadOnlyList<HeatmapTrigger> allowed = HeatmapWatch.TriggersFor(state.watchable.kind);
                int index = IndexOf(allowed, (HeatmapTrigger)triggerProp.intValue);
                if (index < 0) index = 0;
                var options = new GUIContent[allowed.Count];
                for (int i = 0; i < options.Length; i++) options[i] = new GUIContent(ObjectNames.NicifyVariableName(allowed[i].ToString()));
                index = EditorGUI.Popup(row, TriggerLabel, index, options);
                triggerProp.intValue = (int)allowed[index];
            }

            if (state.showValue)
            {
                NextRow(ref row);
                if (state.watchable.kind == HeatmapWatchKind.Enum) DrawEnumValue(row, valueProp, state.watchable.valueType);
                else EditorGUI.PropertyField(row, valueProp, ValueLabel);
            }

            NextRow(ref row);
            EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(HeatmapCustomEvent.location)), LocationLabel);

            NextRow(ref row);
            EditorGUI.PropertyField(row, property.FindPropertyRelative(nameof(HeatmapCustomEvent.cooldown)), CooldownLabel);

            if (state.problem != null)
            {
                var box = new Rect(row.x, row.yMax + Gap, row.width, Line * 2f);
                EditorGUI.HelpBox(EditorGUI.IndentedRect(box), state.problem, MessageType.Warning);
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        static State Inspect(SerializedProperty property)
        {
            var state = new State
            {
                owner = OwnerOf(property, property.FindPropertyRelative(nameof(HeatmapCustomEvent.target)).objectReferenceValue as GameObject),
                component = property.FindPropertyRelative(nameof(HeatmapCustomEvent.component)).objectReferenceValue as Component,
                member = property.FindPropertyRelative(nameof(HeatmapCustomEvent.member)).stringValue,
            };

            if (string.IsNullOrEmpty(state.member)) return state;

            if (state.component == null)
            {
                state.problem = "The script being watched is missing. Pick something else to watch.";
                return state;
            }

            state.found = HeatmapWatch.TryFind(state.component.GetType(), state.member, out state.watchable);
            if (!state.found)
            {
                state.problem = $"{state.component.GetType().Name} no longer has \"{state.member}\". Pick something else to watch.";
                return state;
            }

            state.showTrigger = state.watchable.kind != HeatmapWatchKind.Event;
            if (state.showTrigger)
            {
                IReadOnlyList<HeatmapTrigger> allowed = HeatmapWatch.TriggersFor(state.watchable.kind);
                var trigger = (HeatmapTrigger)property.FindPropertyRelative(nameof(HeatmapCustomEvent.trigger)).intValue;
                if (IndexOf(allowed, trigger) < 0) trigger = allowed[0];
                state.showValue = HeatmapWatch.NeedsValue(trigger);
            }
            return state;
        }

        static GameObject OwnerOf(SerializedProperty property, GameObject target)
        {
            if (target != null) return target;
            var tracker = property.serializedObject.targetObject as Component;
            return tracker != null ? tracker.gameObject : null;
        }

        static string Header(SerializedProperty nameProp, SerializedProperty triggerProp, State state)
        {
            string name = string.IsNullOrWhiteSpace(nameProp.stringValue) ? "New Event" : nameProp.stringValue;
            if (!state.found) return name;

            string when = state.watchable.kind == HeatmapWatchKind.Event
                ? "fires"
                : ObjectNames.NicifyVariableName(((HeatmapTrigger)triggerProp.intValue).ToString()).ToLowerInvariant();
            return $"{name}   ({state.member} {when})";
        }

        static string WatchText(State state)
        {
            if (state.found) return $"{state.component.GetType().Name}.{state.member}   ({state.watchable.TypeLabel})";
            if (!string.IsNullOrEmpty(state.member)) return $"{state.member} (missing)";
            return "Choose an event or value...";
        }

        static void ShowWatchMenu(Rect rect, SerializedProperty property, State state)
        {
            var menu = new GenericMenu();
            SerializedObject serializedObject = property.serializedObject;
            string path = property.propertyPath;

            if (state.owner == null)
            {
                menu.AddDisabledItem(new GUIContent("Drag an object into the Object field first"));
                menu.DropDown(rect);
                return;
            }

            var seenTypes = new Dictionary<Type, int>();
            bool any = false;
            foreach (Component c in state.owner.GetComponents<Component>())
            {
                if (c == null || c is Transform || c is HeatmapTracker) continue;

                Type type = c.GetType();
                seenTypes.TryGetValue(type, out int copies);
                seenTypes[type] = copies + 1;
                string group = copies == 0 ? type.Name : $"{type.Name} ({copies + 1})";

                IReadOnlyList<HeatmapWatchable> members = HeatmapWatch.GetMembers(type);
                if (members.Count == 0) continue;

                foreach (HeatmapWatchable member in members)
                {
                    bool selected = c == state.component && member.name == state.member;
                    menu.AddItem(new GUIContent($"{group}/{member.name}   ({member.TypeLabel})"), selected,
                        () => Assign(serializedObject, path, c, member));
                    any = true;
                }
            }

            if (!any) menu.AddDisabledItem(new GUIContent($"Nothing on {state.owner.name} can be watched"));
            menu.DropDown(rect);
        }

        static void Assign(SerializedObject serializedObject, string path, Component component, HeatmapWatchable member)
        {
            serializedObject.Update();
            SerializedProperty property = serializedObject.FindProperty(path);
            if (property == null) return;

            property.FindPropertyRelative(nameof(HeatmapCustomEvent.component)).objectReferenceValue = component;
            property.FindPropertyRelative(nameof(HeatmapCustomEvent.member)).stringValue = member.name;
            property.FindPropertyRelative(nameof(HeatmapCustomEvent.trigger)).intValue = (int)HeatmapWatch.TriggersFor(member.kind)[0];

            SerializedProperty nameProp = property.FindPropertyRelative(nameof(HeatmapCustomEvent.eventName));
            if (string.IsNullOrWhiteSpace(nameProp.stringValue)) nameProp.stringValue = ObjectNames.NicifyVariableName(member.name);

            property.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        static void DrawEnumValue(Rect row, SerializedProperty valueProp, Type enumType)
        {
            string[] names = Enum.GetNames(enumType);
            Array values = Enum.GetValues(enumType);
            int index = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (Math.Abs(Convert.ToDouble(values.GetValue(i)) - valueProp.floatValue) < 1e-4) index = i;
            }

            var options = new GUIContent[names.Length];
            for (int i = 0; i < names.Length; i++) options[i] = new GUIContent(ObjectNames.NicifyVariableName(names[i]));
            index = EditorGUI.Popup(row, ValueLabel, index, options);
            valueProp.floatValue = (float)Convert.ToDouble(values.GetValue(index));
        }

        static int IndexOf(IReadOnlyList<HeatmapTrigger> list, HeatmapTrigger trigger)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == trigger) return i;
            return -1;
        }

        static void NextRow(ref Rect row)
        {
            row.y += Line + Gap;
        }
    }
}

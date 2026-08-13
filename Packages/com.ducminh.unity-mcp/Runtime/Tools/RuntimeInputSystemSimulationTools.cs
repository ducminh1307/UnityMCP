using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [Serializable] public sealed class InputSimulateKeyInput
    {
        /// <summary>Allowlisted Input System Key enum name, for example Space, A, or LeftShift.</summary>
        public string key;
        public bool pressed = true;
        public bool apply;
    }

    [Serializable] public sealed class InputSimulatePointerInput
    {
        /// <summary>Player-window pointer coordinates. x and y must be supplied together.</summary>
        public float? x;
        public float? y;
        /// <summary>left, right, middle, back, or forward.</summary>
        public string button;
        public bool? pressed;
        public float? scrollX;
        public float? scrollY;
        public bool apply;
    }

    [Serializable] public sealed class InputSimulationOutput
    {
        public bool dryRun;
        public bool queued;
        public int deviceId;
        public string device;
        public List<string> operations = new List<string>();
        public string note;
    }

    /// <summary>
    /// Queues narrowly allowlisted synthetic input through the public Input System event APIs.
    /// No Input System types are referenced at compile time, so these tools are absent when the
    /// optional package is not installed. Events are processed on the next normal InputSystem update.
    /// </summary>
    public static class RuntimeInputSystemSimulationTools
    {
        private const float MaxPointerCoordinate = 100000f;
        private static readonly HashSet<string> AllowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
            "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
            "Space", "Enter", "NumpadEnter", "Tab", "Backspace", "Escape", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
            "UpArrow", "DownArrow", "LeftArrow", "RightArrow", "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
        };

        [UnityMcpTool("input-simulate-key", Description = "Queue an allowlisted keyboard state event through the Input System; dry-run unless apply is true.", Category = "audio-input", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, RequiredType = "UnityEngine.InputSystem.InputSystem")]
        public static InputSimulationOutput InputSimulateKey(InputSimulateKeyInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var keyName = NormalizeKey(input.key);
            var keyboardType = RequireType("UnityEngine.InputSystem.Keyboard");
            var keyType = RequireType("UnityEngine.InputSystem.Key");
            var keyboard = RequireCurrentDevice(keyboardType, "Keyboard");
            var key = Enum.Parse(keyType, keyName, true);
            var control = RequireIndexedControl(keyboard, keyboardType, keyType, key, "Keyboard key");
            var output = CreateOutput(context, keyboard, "Keyboard");
            output.operations.Add((input.pressed ? "Press " : "Release ") + keyName + ".");
            if (!context.DryRun) QueueDelta(control, input.pressed ? 1f : 0f);
            return output;
        }

        [UnityMcpTool("input-simulate-pointer", Description = "Queue bounded pointer movement, button, or scroll state through the Input System; dry-run unless apply is true.", Category = "audio-input", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, RequiredType = "UnityEngine.InputSystem.InputSystem")]
        public static InputSimulationOutput InputSimulatePointer(InputSimulatePointerInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var hasPosition = input.x.HasValue || input.y.HasValue;
            if (hasPosition && (!input.x.HasValue || !input.y.HasValue)) throw new ArgumentException("x and y must be supplied together.");
            if (hasPosition && (Mathf.Abs(input.x.Value) > MaxPointerCoordinate || Mathf.Abs(input.y.Value) > MaxPointerCoordinate))
                throw new ArgumentOutOfRangeException("x", "Pointer coordinates must stay within +/-" + MaxPointerCoordinate + ".");
            var hasScroll = input.scrollX.HasValue || input.scrollY.HasValue;
            var hasButton = !string.IsNullOrWhiteSpace(input.button) || input.pressed.HasValue;
            if (hasButton && (string.IsNullOrWhiteSpace(input.button) || !input.pressed.HasValue))
                throw new ArgumentException("button and pressed must be supplied together.");
            if (!hasPosition && !hasScroll && !hasButton) throw new ArgumentException("Supply a position, a scroll value, or a button state.");

            var mouseType = RequireType("UnityEngine.InputSystem.Mouse");
            var mouse = RequireCurrentDevice(mouseType, "Mouse");
            var output = CreateOutput(context, mouse, "Mouse");
            if (hasPosition)
            {
                var position = new Vector2(input.x.Value, input.y.Value);
                output.operations.Add("Set pointer position to (" + position.x + ", " + position.y + ").");
                if (!context.DryRun) QueueDelta(RequireNamedControl(mouse, mouseType, "position"), position);
            }
            if (hasScroll)
            {
                var scroll = new Vector2(input.scrollX ?? 0f, input.scrollY ?? 0f);
                output.operations.Add("Queue pointer scroll (" + scroll.x + ", " + scroll.y + ").");
                if (!context.DryRun) QueueDelta(RequireNamedControl(mouse, mouseType, "scroll"), scroll);
            }
            if (hasButton)
            {
                var property = PointerButtonProperty(input.button);
                output.operations.Add((input.pressed.Value ? "Press " : "Release ") + input.button.Trim().ToLowerInvariant() + " mouse button.");
                if (!context.DryRun) QueueDelta(RequireNamedControl(mouse, mouseType, property), input.pressed.Value ? 1f : 0f);
            }
            return output;
        }

        private static InputSimulationOutput CreateOutput(UnityMcpContext context, object device, string deviceName)
        {
            return new InputSimulationOutput
            {
                dryRun = context.DryRun,
                queued = !context.DryRun,
                deviceId = ReadInt(device, "deviceId"),
                device = deviceName,
                note = context.DryRun
                    ? "Validated only; no input event was queued."
                    : "The event was queued through InputSystem and will be processed on its next normal input update."
            };
        }

        private static string NormalizeKey(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) throw new ArgumentException("key is required.");
            var match = AllowedKeys.FirstOrDefault(value => string.Equals(value, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null) throw new ArgumentException("key is not allowlisted. Supported keys are letters, digits, numpad digits, navigation keys, modifiers, and F1-F12.");
            return match;
        }

        private static string PointerButtonProperty(string requested)
        {
            switch ((requested ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "left": return "leftButton";
                case "right": return "rightButton";
                case "middle": return "middleButton";
                case "back": return "backButton";
                case "forward": return "forwardButton";
                default: throw new ArgumentException("button must be left, right, middle, back, or forward.");
            }
        }

        private static void QueueDelta(object control, object value)
        {
            var inputSystemType = RequireType("UnityEngine.InputSystem.InputSystem");
            var method = inputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(candidate =>
            {
                if (candidate.Name != "QueueDeltaStateEvent" || !candidate.IsGenericMethodDefinition || candidate.GetGenericArguments().Length != 1) return false;
                var parameters = candidate.GetParameters();
                return parameters.Length == 3 && parameters[1].ParameterType.IsGenericParameter && parameters[2].ParameterType == typeof(double);
            });
            if (method == null) throw new InvalidOperationException("The installed Input System does not expose the supported public QueueDeltaStateEvent API.");
            try
            {
                method.MakeGenericMethod(value.GetType()).Invoke(null, new object[] { control, value, -1d });
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("Input System rejected the synthetic input event: " + (exception.InnerException?.Message ?? exception.Message), exception.InnerException ?? exception);
            }
        }

        private static object RequireCurrentDevice(Type deviceType, string deviceName)
        {
            var property = deviceType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            var device = property == null ? null : property.GetValue(null, null);
            if (device == null) throw new InvalidOperationException("No current " + deviceName + " device is available to the Input System.");
            return device;
        }

        private static object RequireIndexedControl(object device, Type deviceType, Type indexType, object index, string label)
        {
            var property = deviceType.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetIndexParameters();
                return candidate.Name == "Item" && parameters.Length == 1 && parameters[0].ParameterType == indexType;
            });
            var control = property == null ? null : property.GetValue(device, new[] { index });
            if (control == null) throw new InvalidOperationException(label + " control is unavailable on the current device.");
            return control;
        }

        private static object RequireNamedControl(object device, Type deviceType, string propertyName)
        {
            var property = deviceType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            var control = property == null ? null : property.GetValue(device, null);
            if (control == null) throw new InvalidOperationException("The current input device does not expose '" + propertyName + "'.");
            return control;
        }

        private static int ReadInt(object target, string name)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null) throw new InvalidOperationException(target.GetType().FullName + " does not expose '" + name + "'.");
            return Convert.ToInt32(property.GetValue(target, null), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType(fullName, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            throw new InvalidOperationException("The required Input System type is unavailable: " + fullName);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace CDSP.Overlay
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CDSP_OVERLAY_IN_RELEASE
    internal static class CDSPOverlayInput
    {
        static readonly Dictionary<KeyCode, object> _mappedKeys = new Dictionary<KeyCode, object>();
        static readonly HashSet<KeyCode> _unmappable = new HashSet<KeyCode>();

        static bool _reflectInitAttempted;
        static Type _keyboardType;
        static PropertyInfo _keyboardCurrentProp;
        static MethodInfo _keyboardGetItem;
        static Type _keyEnumType;
        static PropertyInfo _wasPressedProp;

        static Type _mouseType;
        static PropertyInfo _mouseCurrentProp;
        static PropertyInfo _mousePositionProp;
        static MethodInfo _mouseReadValue;
        static Type _mousePositionControlType;

        static Vector2 _lastMouseGUI;
        static bool _hasLastMouse;
        static readonly HashSet<string> _warned = new HashSet<string>();

        static void WarnOnce(string key, string message)
        {
            if (_warned.Contains(key))
                return;
            _warned.Add(key);
            try { Debug.LogWarning(message); }
            catch { }
        }

        public static bool WasPressed(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(key))
                    return true;
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception e)
            {
                WarnOnce("legacy-key", $"[CDSP] Legacy input check failed ({key}): {e.GetType().Name}");
            }
#endif
            try
            {
                return WasPressedNewSystem(key);
            }
            catch (Exception e)
            {
                WarnOnce("new-key", $"[CDSP] New input check failed ({key}): {e.GetType().Name}");
                return false;
            }
        }

        public static Vector2 MousePositionGUI()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                Vector3 p = Input.mousePosition;
                var gui = new Vector2(p.x, Screen.height - p.y);
                _lastMouseGUI = gui;
                _hasLastMouse = true;
                return gui;
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception e)
            {
                WarnOnce("legacy-mouse", $"[CDSP] Legacy mouse check failed: {e.GetType().Name}");
            }
#endif
            try
            {
                if (TryGetMouseScreenBottomLeft(out var bottomLeft))
                {
                    var gui = new Vector2(bottomLeft.x, Screen.height - bottomLeft.y);
                    _lastMouseGUI = gui;
                    _hasLastMouse = true;
                    return gui;
                }
            }
            catch (Exception e)
            {
                WarnOnce("new-mouse", $"[CDSP] New mouse check failed: {e.GetType().Name}");
            }

            try
            {
                var e2 = Event.current;
                if (e2 != null)
                {
                    _lastMouseGUI = e2.mousePosition;
                    _hasLastMouse = true;
                    return e2.mousePosition;
                }
            }
            catch
            {
            }

            return _hasLastMouse ? _lastMouseGUI : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        static bool WasPressedNewSystem(KeyCode key)
        {
            EnsureReflectionInit();
            if (_keyboardType == null || _keyboardCurrentProp == null ||
                _keyboardGetItem == null || _keyEnumType == null)
                return false;

            object keyboard;
            try { keyboard = _keyboardCurrentProp.GetValue(null, null); }
            catch { return false; }
            if (keyboard == null)
                return false;

            object keyValue = MapKeyCode(key);
            if (keyValue == null)
                return false;

            object control;
            try { control = _keyboardGetItem.Invoke(keyboard, new[] { keyValue }); }
            catch { return false; }
            if (control == null)
                return false;

            try
            {
                if (_wasPressedProp == null || _wasPressedProp.DeclaringType == null ||
                    !_wasPressedProp.DeclaringType.IsAssignableFrom(control.GetType()))
                {
                    _wasPressedProp = control.GetType().GetProperty(
                        "wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                }
                if (_wasPressedProp == null)
                    return false;
                return (bool)_wasPressedProp.GetValue(control, null);
            }
            catch
            {
                return false;
            }
        }

        static bool TryGetMouseScreenBottomLeft(out Vector2 screenPos)
        {
            screenPos = default;
            EnsureReflectionInit();
            if (_mouseType == null || _mouseCurrentProp == null || _mousePositionProp == null)
                return false;

            object mouse;
            try { mouse = _mouseCurrentProp.GetValue(null, null); }
            catch { return false; }
            if (mouse == null)
                return false;

            object positionControl;
            try { positionControl = _mousePositionProp.GetValue(mouse, null); }
            catch { return false; }
            if (positionControl == null)
                return false;

            try
            {
                if (_mouseReadValue == null || _mousePositionControlType != positionControl.GetType())
                {
                    _mousePositionControlType = positionControl.GetType();
                    _mouseReadValue = _mousePositionControlType.GetMethod(
                        "ReadValue", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }
                if (_mouseReadValue == null)
                    return false;
                object v = _mouseReadValue.Invoke(positionControl, null);
                if (v is Vector2 pv)
                {
                    screenPos = pv;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        static void EnsureReflectionInit()
        {
            if (_reflectInitAttempted)
                return;
            _reflectInitAttempted = true;

            try
            {
                _keyEnumType = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
                _keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                _mouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
                if (_keyboardType == null || _keyEnumType == null)
                    return;

                _keyboardCurrentProp = _keyboardType.GetProperty(
                    "current", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                _keyboardGetItem = _keyboardType.GetMethod(
                    "get_Item", BindingFlags.Public | BindingFlags.Instance, null, new[] { _keyEnumType }, null);

                if (_mouseType != null)
                {
                    _mouseCurrentProp = _mouseType.GetProperty(
                        "current", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    _mousePositionProp = _mouseType.GetProperty(
                        "position", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                }
            }
            catch
            {
                _keyboardType = null;
                _mouseType = null;
            }
        }

        static object MapKeyCode(KeyCode key)
        {
            if (_mappedKeys.TryGetValue(key, out var cached))
                return cached;
            if (_unmappable.Contains(key) || _keyEnumType == null)
                return null;

            string name = KeyCodeToInputSystemName(key) ?? key.ToString();
            try
            {
                if (!Enum.IsDefined(_keyEnumType, name))
                {
                    _unmappable.Add(key);
                    return null;
                }
                object value = Enum.Parse(_keyEnumType, name);
                _mappedKeys[key] = value;
                return value;
            }
            catch
            {
                _unmappable.Add(key);
                return null;
            }
        }

        static string KeyCodeToInputSystemName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Alpha0: return "Digit0";
                case KeyCode.Alpha1: return "Digit1";
                case KeyCode.Alpha2: return "Digit2";
                case KeyCode.Alpha3: return "Digit3";
                case KeyCode.Alpha4: return "Digit4";
                case KeyCode.Alpha5: return "Digit5";
                case KeyCode.Alpha6: return "Digit6";
                case KeyCode.Alpha7: return "Digit7";
                case KeyCode.Alpha8: return "Digit8";
                case KeyCode.Alpha9: return "Digit9";
                case KeyCode.Keypad0: return "Numpad0";
                case KeyCode.Keypad1: return "Numpad1";
                case KeyCode.Keypad2: return "Numpad2";
                case KeyCode.Keypad3: return "Numpad3";
                case KeyCode.Keypad4: return "Numpad4";
                case KeyCode.Keypad5: return "Numpad5";
                case KeyCode.Keypad6: return "Numpad6";
                case KeyCode.Keypad7: return "Numpad7";
                case KeyCode.Keypad8: return "Numpad8";
                case KeyCode.Keypad9: return "Numpad9";
                case KeyCode.KeypadPeriod: return "NumpadPeriod";
                case KeyCode.KeypadDivide: return "NumpadDivide";
                case KeyCode.KeypadMultiply: return "NumpadMultiply";
                case KeyCode.KeypadMinus: return "NumpadMinus";
                case KeyCode.KeypadPlus: return "NumpadPlus";
                case KeyCode.KeypadEnter: return "NumpadEnter";
                case KeyCode.KeypadEquals: return "NumpadEquals";
                case KeyCode.Return: return "Enter";
                case KeyCode.LeftControl: return "LeftCtrl";
                case KeyCode.RightControl: return "RightCtrl";
                default: return null;
            }
        }
    }

#else

    internal static class CDSPOverlayInput
    {
        public static bool WasPressed(KeyCode key) => false;
        public static Vector2 MousePositionGUI() => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

#endif
}

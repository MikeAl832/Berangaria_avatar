using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Berangaria.Avatar.Runtime
{
    [DisallowMultipleComponent]
    public sealed class DesktopOverlayWindow : MonoBehaviour
    {
        private const string PreferencePrefix = "Berangaria.Overlay.";

        [Header("Primary-screen overlay")]
        [SerializeField, Min(360)] private int defaultWidth = 560;
        [SerializeField, Min(480)] private int defaultHeight = 900;
        [SerializeField, Min(0)] private int rightMargin = 24;
        [SerializeField, Min(80)] private int minimumVisibleHeight = 180;
        [SerializeField] private bool alwaysOnTop = true;

        private bool _configureMode;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int WsCaption = 0x00C00000;
        private const int WsThickFrame = 0x00040000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;
        private const int WsSystemMenu = 0x00080000;
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsVisible = 0x10000000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const uint SpiGetWorkArea = 0x0030;
        private const int VkControl = 0x11;
        private const int VkShift = 0x10;
        private const int VkF8 = 0x77;
        private const uint GwOwner = 4;

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNotTopmost = new IntPtr(-2);

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int left;
            public int right;
            public int top;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            out NativeRect value,
            uint update);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

        private IntPtr _window;
        private NativeRect _workArea;
        private NativeRect _windowRect;
        private NativePoint _dragCursor;
        private NativeRect _dragRect;
        private bool _dragging;
        private bool _toggleHeld;
        private int _windowWidth;
        private int _windowHeight;
#endif

        private void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            var camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
        }

        private IEnumerator Start()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _windowWidth = Mathf.Max(360, PlayerPrefs.GetInt(PreferencePrefix + "Width", defaultWidth));
            _windowHeight = Mathf.Max(480, PlayerPrefs.GetInt(PreferencePrefix + "Height", defaultHeight));
            Screen.SetResolution(_windowWidth, _windowHeight, FullScreenMode.Windowed);
            for (var attempt = 0; attempt < 120 && _window == IntPtr.Zero; attempt++)
            {
                yield return null;
                _window = FindPlayerWindow();
            }
            if (_window == IntPtr.Zero)
            {
                Debug.LogError("BERANGARIA_OVERLAY_WINDOW_NOT_FOUND");
                yield break;
            }

            ConfigureNativeWindow();
            Debug.Log(
                $"BERANGARIA_OVERLAY_READY size={_windowWidth}x{_windowHeight} " +
                $"minimumVisibleHeight={minimumVisibleHeight} " +
                "clickThrough=true hotkey=Ctrl+Shift+F8");
#else
            yield break;
#endif
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_window == IntPtr.Zero)
            {
                return;
            }

            var togglePressed = IsKeyDown(VkControl) && IsKeyDown(VkShift) && IsKeyDown(VkF8);
            if (togglePressed && !_toggleHeld)
            {
                SetConfigureMode(!_configureMode);
            }
            _toggleHeld = togglePressed;

            if (!_configureMode)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
            {
                SetConfigureMode(false);
                return;
            }

            HandleDrag();
            var wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                ResizeBy(Mathf.RoundToInt(wheel * 40f));
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static IntPtr FindPlayerWindow()
        {
            var processId = GetCurrentProcessId();
            var active = GetActiveWindow();
            if (active != IntPtr.Zero
                && IsWindowVisible(active)
                && GetWindowThreadProcessId(active, out var activeProcessId) != 0
                && activeProcessId == processId)
            {
                return active;
            }

            var bestWindow = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows(
                (candidate, _parameter) =>
                {
                    if (!IsWindowVisible(candidate)
                        || GetWindow(candidate, GwOwner) != IntPtr.Zero
                        || GetWindowThreadProcessId(candidate, out var candidateProcessId) == 0
                        || candidateProcessId != processId
                        || !GetWindowRect(candidate, out var bounds))
                    {
                        return true;
                    }

                    var width = Math.Max(0, bounds.right - bounds.left);
                    var height = Math.Max(0, bounds.bottom - bounds.top);
                    var area = (long)width * height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestWindow = candidate;
                    }
                    return true;
                },
                IntPtr.Zero);
            return bestWindow;
        }

        private void ConfigureNativeWindow()
        {
            var margins = new Margins { left = -1, right = -1, top = -1, bottom = -1 };
            DwmExtendFrameIntoClientArea(_window, ref margins);

            var style = GetWindowLong(_window, GwlStyle);
            style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSystemMenu);
            style |= WsPopup | WsVisible;
            SetWindowLong(_window, GwlStyle, style);

            SetClickThrough(enabled: true);
            SystemParametersInfo(SpiGetWorkArea, 0, out _workArea, 0);
            var defaultX = _workArea.right - _windowWidth - rightMargin;
            var defaultY = _workArea.bottom - _windowHeight;
            var x = PlayerPrefs.GetInt(PreferencePrefix + "X", defaultX);
            var y = PlayerPrefs.GetInt(PreferencePrefix + "Y", defaultY);
            MoveAndResize(ClampX(x), ClampY(y), _windowWidth, _windowHeight);
        }

        private void SetConfigureMode(bool enabled)
        {
            _configureMode = enabled;
            _dragging = false;
            SetClickThrough(!enabled);
            if (enabled)
            {
                SetForegroundWindow(_window);
            }
            else
            {
                SavePlacement();
            }
            Debug.Log($"BERANGARIA_OVERLAY_CONFIG enabled={enabled}");
        }

        private void SetClickThrough(bool enabled)
        {
            var extended = GetWindowLong(_window, GwlExStyle);
            extended |= WsExLayered | WsExToolWindow;
            if (enabled)
            {
                extended |= WsExTransparent | WsExNoActivate;
            }
            else
            {
                extended &= ~(WsExTransparent | WsExNoActivate);
            }
            SetWindowLong(_window, GwlExStyle, extended);
            GetWindowRect(_window, out _windowRect);
            MoveAndResize(
                _windowRect.left,
                _windowRect.top,
                _windowRect.right - _windowRect.left,
                _windowRect.bottom - _windowRect.top);
        }

        private void HandleDrag()
        {
            if (Input.GetMouseButtonDown(0) && GetCursorPos(out _dragCursor))
            {
                GetWindowRect(_window, out _dragRect);
                _dragging = true;
            }
            if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
            }
            if (!_dragging || !GetCursorPos(out var cursor))
            {
                return;
            }

            var x = _dragRect.left + cursor.x - _dragCursor.x;
            var y = _dragRect.top + cursor.y - _dragCursor.y;
            MoveAndResize(ClampX(x), ClampY(y), _windowWidth, _windowHeight);
        }

        private void ResizeBy(int delta)
        {
            if (delta == 0)
            {
                return;
            }
            GetWindowRect(_window, out var current);
            var aspect = (float)defaultHeight / defaultWidth;
            var maximumWidth = Mathf.Min(900, Mathf.FloorToInt((_workArea.bottom - _workArea.top) / aspect));
            _windowWidth = Mathf.Clamp(_windowWidth + delta, 360, maximumWidth);
            _windowHeight = Mathf.RoundToInt(_windowWidth * aspect);
            MoveAndResize(ClampX(current.left), ClampY(current.top), _windowWidth, _windowHeight);
        }

        private int ClampX(int x)
        {
            return Mathf.Clamp(x, _workArea.left, Mathf.Max(_workArea.left, _workArea.right - _windowWidth));
        }

        private int ClampY(int y)
        {
            // Let the lower part of the avatar leave the work area so users can hide
            // legs while retaining enough of the overlay to find and reposition it.
            var visibleHeight = Mathf.Clamp(minimumVisibleHeight, 80, _windowHeight);
            var maximumY = Mathf.Max(_workArea.top, _workArea.bottom - visibleHeight);
            return Mathf.Clamp(y, _workArea.top, maximumY);
        }

        private void MoveAndResize(int x, int y, int width, int height)
        {
            var insertAfter = alwaysOnTop ? HwndTopmost : HwndNotTopmost;
            SetWindowPos(
                _window,
                insertAfter,
                x,
                y,
                width,
                height,
                SwpNoActivate | SwpFrameChanged | SwpShowWindow);
        }

        private void SavePlacement()
        {
            if (!GetWindowRect(_window, out var current))
            {
                return;
            }
            PlayerPrefs.SetInt(PreferencePrefix + "X", current.left);
            PlayerPrefs.SetInt(PreferencePrefix + "Y", current.top);
            PlayerPrefs.SetInt(PreferencePrefix + "Width", _windowWidth);
            PlayerPrefs.SetInt(PreferencePrefix + "Height", _windowHeight);
            PlayerPrefs.Save();
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
#endif

        private void OnGUI()
        {
            if (!_configureMode)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = Color.white },
            };
            GUI.Box(
                new Rect(16f, 16f, Mathf.Min(420f, Screen.width - 32f), 72f),
                "Placement mode\nDrag to move · mouse wheel to scale · Enter to finish",
                style);
        }
    }
}

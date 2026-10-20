using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Linq;

namespace Ryujinx.Library
{
    class NativeTouchDriver : IGamepadDriver
    {
        private const int CursorHideIdleTime = 5;

        private bool _isDisposed;
        private readonly HideCursorMode _hideCursorMode;
        private bool _isHidden;
        private long _lastCursorMoveTime;

        public bool[] PressedButtons { get; }
        public Vector2 Scroll { get; private set; }
        public Size ClientSize;

        private static readonly TouchStateBuffer _touches = new();
        private static readonly string[] _ids = { "0" };

        public ReadOnlyMemory<KeyValuePair<int, Vector2>> CurrentTouches => _touches.GetSnapshot();

        public NativeTouchDriver(HideCursorMode hideCursorMode)
        {
            PressedButtons = new bool[(int)MouseButton.Count];
            _hideCursorMode = hideCursorMode;
            _touches.Clear();
        }

        [UnmanagedCallersOnly(EntryPoint = "touch_began")]
        public static void TouchBeganAtPoint(float x, float y, int index)
        {
            _touches.Begin(index, new Vector2(x, y));
        }


        [UnmanagedCallersOnly(EntryPoint = "touch_moved")]
        public static void TouchMovedAtPoint(float x, float y, int index)
        {
            _touches.Move(index, new Vector2(x, y));
        }

        [UnmanagedCallersOnly(EntryPoint = "touch_ended")]
        public static void TouchEndedForIndex(int index)
        {
            _touches.End(index);
        }

        public void UpdatePosition()
        {
            CheckIdle();
        }

        private void CheckIdle()
        {
            if (_hideCursorMode != HideCursorMode.OnIdle)
            {
                return;
            }

            long cursorMoveDelta = Stopwatch.GetTimestamp() - _lastCursorMoveTime;

            if (cursorMoveDelta >= CursorHideIdleTime * Stopwatch.Frequency)
            {
                if (!_isHidden)
                {
                    Logger.Debug?.Print(LogClass.Application, "Hiding cursor due to inactivity.");
                    _isHidden = true;
                }
            }
            else
            {
                if (_isHidden)
                {
                    Logger.Debug?.Print(LogClass.Application, "Showing cursor after activity.");
                    _isHidden = false;
                }
            }
        }

        public IEnumerable<IGamepad> GetGamepads() => [GetGamepad("0")];

        public void SetClientSize(int width, int height)
        {
            ClientSize = new Size(width, height);
        }

        public bool IsAnyPressed(MouseButton button)
        {
            return button == MouseButton.Button1 && !CurrentTouches.IsEmpty;
        }

        public Size GetClientSize()
        {
            return ClientSize;
        }

        public string DriverName => "NativeTouchDriver";

        public event Action<string> OnGamepadConnected
        {
            add { }
            remove { }
        }

        public event Action<string> OnGamepadDisconnected
        {
            add { }
            remove { }
        }

        public ReadOnlySpan<string> GamepadsIds => _ids;

        public IGamepad GetGamepad(string id)
        {
            return new NativeMouse(this);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _touches.Clear();
            _isDisposed = true;
        }
    }
}

using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace LegendaryCSharp.Services;

public sealed class InputService
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFMiddleDown = 0x0020;
    private const uint MouseEventFMiddleUp = 0x0040;
    private const uint MouseEventFXDown = 0x0080;
    private const uint MouseEventFXUp = 0x0100;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;
    private const short KeyStateDown = unchecked((short)0x8000);
    private const uint KeyEventFKeyUp = 0x0002;

    public void MoveMouseBy(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        Span<Input> inputs =
        [
            new()
            {
                Type = InputMouse,
                Mouse = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    Flags = MouseEventFMove
                }
            }
        ];

        Send(inputs);
    }

    public void TapKey(string key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            TapMouseButton(button);
            return;
        }

        KeyDown(key);
        KeyUp(key);
    }

    public void TapMouseButton(GlobalMouseButton button)
    {
        MouseButtonDown(button);
        MouseButtonUp(button);
    }

    public void MouseButtonDown(GlobalMouseButton button) => SendMouseButton(button, true);

    public void MouseButtonUp(GlobalMouseButton button) => SendMouseButton(button, false);

    public void KeyDown(string key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            MouseButtonDown(button);
            return;
        }

        var vk = KeyNameMapper.ToVirtualKey(key);
        if (vk == 0)
        {
            return;
        }

        Span<Input> inputs =
        [
            new()
            {
                Type = InputKeyboard,
                Keyboard = new KeyboardInput { VirtualKey = vk }
            }
        ];

        Send(inputs);
    }

    public void KeyUp(string key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            MouseButtonUp(button);
            return;
        }

        var vk = KeyNameMapper.ToVirtualKey(key);
        if (vk == 0)
        {
            return;
        }

        Span<Input> inputs =
        [
            new()
            {
                Type = InputKeyboard,
                Keyboard = new KeyboardInput { VirtualKey = vk, Flags = KeyEventFKeyUp }
            }
        ];

        Send(inputs);
    }

    public bool IsKeyDown(string key)
    {
        if (TryGetMouseButton(key, out var button))
        {
            return IsMouseButtonDown(button);
        }

        var vk = KeyNameMapper.ToVirtualKey(key);
        return vk != 0 && (GetAsyncKeyState(vk) & KeyStateDown) != 0;
    }

    private static bool IsMouseButtonDown(GlobalMouseButton button)
    {
        var vk = button switch
        {
            GlobalMouseButton.Left => (int)Forms.Keys.LButton,
            GlobalMouseButton.Right => (int)Forms.Keys.RButton,
            GlobalMouseButton.Middle => (int)Forms.Keys.MButton,
            GlobalMouseButton.XButton1 => (int)Forms.Keys.XButton1,
            GlobalMouseButton.XButton2 => (int)Forms.Keys.XButton2,
            _ => 0
        };

        return vk != 0 && (GetAsyncKeyState(vk) & KeyStateDown) != 0;
    }

    private static bool TryGetMouseButton(string key, out GlobalMouseButton button)
    {
        switch (KeyNameMapper.Normalize(key))
        {
            case "LBUTTON":
                button = GlobalMouseButton.Left;
                return true;
            case "RBUTTON":
                button = GlobalMouseButton.Right;
                return true;
            case "MBUTTON":
                button = GlobalMouseButton.Middle;
                return true;
            case "XBUTTON1":
                button = GlobalMouseButton.XButton1;
                return true;
            case "XBUTTON2":
                button = GlobalMouseButton.XButton2;
                return true;
            default:
                button = default;
                return false;
        }
    }

    private static void SendMouseButton(GlobalMouseButton button, bool isDown)
    {
        var (flags, data) = button switch
        {
            GlobalMouseButton.Left => (isDown ? MouseEventFLeftDown : MouseEventFLeftUp, 0U),
            GlobalMouseButton.Right => (isDown ? MouseEventFRightDown : MouseEventFRightUp, 0U),
            GlobalMouseButton.Middle => (isDown ? MouseEventFMiddleDown : MouseEventFMiddleUp, 0U),
            GlobalMouseButton.XButton1 => (isDown ? MouseEventFXDown : MouseEventFXUp, XButton1),
            GlobalMouseButton.XButton2 => (isDown ? MouseEventFXDown : MouseEventFXUp, XButton2),
            _ => (0U, 0U)
        };

        if (flags == 0)
        {
            return;
        }

        Span<Input> inputs =
        [
            new()
            {
                Type = InputMouse,
                Mouse = new MouseInput
                {
                    MouseData = data,
                    Flags = flags
                }
            }
        ];

        Send(inputs);
    }

    private static void Send(Span<Input> inputs)
    {
        _ = SendInput((uint)inputs.Length, ref inputs[0], Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, ref Input inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;

        public MouseInput Mouse
        {
            set => Union.Mouse = value;
        }

        public KeyboardInput Keyboard
        {
            set => Union.Keyboard = value;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}

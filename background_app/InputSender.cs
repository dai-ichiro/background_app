using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace background_app
{
    public static class InputSender
    {
        // P/Invoke definitions
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const int INPUT_HARDWARE = 2;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const uint MAPVK_VK_TO_VSC = 0x00;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
            public static int Size => Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        public static void Send(string keyString)
        {
            if (string.IsNullOrEmpty(keyString)) return;

            Keys key;
            try
            {
                var kc = new KeysConverter();
                // Handle special cases or ensure compatibility
                key = (Keys)kc.ConvertFromString(keyString);
            }
            catch (Exception)
            {
                // If conversion fails, ignore or log
                Console.WriteLine($"Invalid key string: {keyString}");
                return;
            }

            List<INPUT> inputs = new List<INPUT>();

            // Extract modifiers
            bool shift = (key & Keys.Shift) == Keys.Shift;
            bool ctrl = (key & Keys.Control) == Keys.Control;
            bool alt = (key & Keys.Alt) == Keys.Alt;

            // Extract key code (remove modifiers)
            Keys keyCode = key & Keys.KeyCode;

            // 1. Modifiers Down
            if (shift) AddKeyInput(inputs, Keys.ShiftKey, false);
            if (ctrl) AddKeyInput(inputs, Keys.ControlKey, false);
            if (alt) AddKeyInput(inputs, Keys.Menu, false);

            // 2. Key Down & Up (if not just a modifier)
            // If keyCode is None, it means we only have modifiers (e.g. "Control")
            // In that case, we effectively press and release the modifier key.
            // Note: If input was "Ctrl+A", modifiers are set, keyCode is A.
            // If input was "Ctrl", modifiers are set (Ctrl), keyCode is None.
            // In the "Ctrl" case, we press Ctrl (in step 1) and release Ctrl (in step 3).
            // That works for a single key press of a modifier.

            // Wait, if keyCode is None, we don't add anything here.
            // But if input was "Alt" -> alt=true, keyCode=None.
            // We press Menu (step 1), then release Menu (step 3). This is correct.

            // However, there is a catch with KeysConverter.
            // "Control" -> Keys.ControlKey (17).
            // key & Keys.Control (0x20000) is FALSE.
            // key & Keys.KeyCode is Keys.ControlKey (17).
            // So shift=false, ctrl=false, alt=false.
            // keyCode = Keys.ControlKey.
            // We press ControlKey Down, ControlKey Up. Correct.

            // "Ctrl+A" -> Keys.Control (0x20000) | Keys.A (65).
            // shift=false, ctrl=true, alt=false.
            // keyCode = Keys.A.
            // We press ControlKey Down (step 1).
            // We press A Down, A Up (step 2).
            // We press ControlKey Up (step 3).
            // Correct.

            // "Alt" -> Keys.Alt (0x40000).
            // shift=false, ctrl=false, alt=true.
            // keyCode = Keys.None.
            // We press Menu Down (step 1).
            // We press Menu Up (step 3).
            // Correct.

            if (keyCode != Keys.None)
            {
                AddKeyInput(inputs, keyCode, false);
                AddKeyInput(inputs, keyCode, true);
            }

            // 3. Modifiers Up (Reverse order)
            if (alt) AddKeyInput(inputs, Keys.Menu, true);
            if (ctrl) AddKeyInput(inputs, Keys.ControlKey, true);
            if (shift) AddKeyInput(inputs, Keys.ShiftKey, true);

            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
            }
        }

        private static void AddKeyInput(List<INPUT> inputs, Keys key, bool keyUp)
        {
            ushort vk = (ushort)key;
            ushort scanCode = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scanCode,
                        dwFlags = KEYEVENTF_SCANCODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            // Handle extended keys
            if (key == Keys.RMenu || key == Keys.RControlKey ||
                key == Keys.Left || key == Keys.Right ||
                key == Keys.Up || key == Keys.Down ||
                key == Keys.Home || key == Keys.End ||
                key == Keys.Prior || key == Keys.Next ||
                key == Keys.Insert || key == Keys.Delete ||
                key == Keys.LWin || key == Keys.RWin ||
                key == Keys.Apps)
            {
                input.U.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
            }

            inputs.Add(input);
        }
    }
}

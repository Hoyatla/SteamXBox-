using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sc2Xboxed.Core.Mapping;

public static class InputHelper
{
	private const int INPUT_KEYBOARD = 1;
	private const int INPUT_MOUSE = 0;
	private const uint KEYEVENTF_KEYUP = 0x0002;
	private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
	private const uint KEYEVENTF_UNICODE = 0x0004;
	private const uint MOUSEEVENTF_MOVE = 0x0001;
	private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
	private const uint MOUSEEVENTF_LEFTUP = 0x0004;
	private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
	private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
	private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
	private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
	private const uint MOUSEEVENTF_WHEEL = 0x0800;
	private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
	private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

	public const int VK_LWIN = 0x5B;
	public const int VK_TAB = 0x09;
	public const int VK_MENU = 0x12;
	public const int VK_LEFT = 0x25;
	public const int VK_UP = 0x26;
	public const int VK_RIGHT = 0x27;
	public const int VK_DOWN = 0x28;
	public const int VK_SNAPSHOT = 0x2C;
	public const int VK_F4 = 0x73;

	public const int WHEEL_DELTA = 120;
	public const int MOUSE_SENSITIVITY = 80;
	public const int SCROLL_SENSITIVITY = 1;

	private struct INPUT
	{
		public uint Type;
		public INPUTUNION Union;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct INPUTUNION
	{
		[FieldOffset(0)]
		public MOUSEINPUT Mouse;
		[FieldOffset(0)]
		public KEYBDINPUT Keyboard;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MOUSEINPUT
	{
		public int Dx;
		public int Dy;
		public uint MouseData;
		public uint DwFlags;
		public uint Time;
		public IntPtr ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT
	{
		public ushort WVk;
		public ushort WScan;
		public uint DwFlags;
		public uint Time;
		public IntPtr ExtraInfo;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern bool SetCursorPos(int X, int Y);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out POINT lpPoint);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	public static void KeyDown(ushort vk)
	{
		INPUT input = new INPUT
		{
			Type = INPUT_KEYBOARD,
			Union = new INPUTUNION
			{
				Keyboard = new KEYBDINPUT
				{
					WVk = vk,
					DwFlags = KEYEVENTF_EXTENDEDKEY
				}
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void KeyUp(ushort vk)
	{
		INPUT input = new INPUT
		{
			Type = INPUT_KEYBOARD,
			Union = new INPUTUNION
			{
				Keyboard = new KEYBDINPUT
				{
					WVk = vk,
					DwFlags = KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY
				}
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void KeyTap(ushort vk)
	{
		KeyDown(vk);
		KeyUp(vk);
	}

	public static void UnicodeChar(char character)
	{
		INPUT down = new INPUT
		{
			Type = INPUT_KEYBOARD,
			Union = new INPUTUNION
			{
				Keyboard = new KEYBDINPUT
				{
					WScan = (ushort)character,
					DwFlags = KEYEVENTF_UNICODE
				}
			}
		};
		INPUT up = new INPUT
		{
			Type = INPUT_KEYBOARD,
			Union = new INPUTUNION
			{
				Keyboard = new KEYBDINPUT
				{
					WScan = (ushort)character,
					DwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
				}
			}
		};
		SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
	}

	public static void KeyCombination(ushort[] vks)
	{
		foreach (ushort vk in vks)
			KeyDown(vk);
		for (int i = vks.Length - 1; i >= 0; i--)
			KeyUp(vks[i]);
	}

	public static void MouseMoveRelative(int dx, int dy)
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT
				{
					Dx = dx,
					Dy = dy,
					DwFlags = MOUSEEVENTF_MOVE
				}
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseLeftDown()
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_LEFTDOWN }
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseLeftUp()
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_LEFTUP }
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseRightDown()
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_RIGHTDOWN }
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseRightUp()
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_RIGHTUP }
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseMiddleDown()
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_MIDDLEDOWN }
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseMiddleUp()
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT { DwFlags = MOUSEEVENTF_MIDDLEUP }
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void MouseWheel(int delta)
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT
				{
					MouseData = (uint)(delta * WHEEL_DELTA),
					DwFlags = MOUSEEVENTF_WHEEL
				}
			}
		};
		SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
	}

	public static void LaunchOrBringToFront(string processName, string? arguments = null)
	{
		Process[] existing = Process.GetProcessesByName(processName);
		if (existing.Length > 0)
		{
			IntPtr hWnd = existing[0].MainWindowHandle;
			if (hWnd != IntPtr.Zero)
			{
				SetForegroundWindow(hWnd);
				return;
			}
		}
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = processName,
			Arguments = arguments ?? "",
			UseShellExecute = true
		};
		Process.Start(startInfo);
	}

	public static void LaunchSteam()
	{
		string[] paths = new[]
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", "steam.exe"),
		};

		foreach (string path in paths)
		{
			if (File.Exists(path))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = path,
					Arguments = "steam://open/bigpicture",
					UseShellExecute = true
				});
				return;
			}
		}

		Process.Start(new ProcessStartInfo
		{
			FileName = "steam://open/bigpicture",
			UseShellExecute = true
		});
	}

	public static void KillProcess(string processName)
	{
		Process[] processes = Process.GetProcessesByName(processName);
		foreach (Process proc in processes)
		{
			try
			{
				proc.Kill(entireProcessTree: true);
			}
			catch { }
		}
	}

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	public static bool IsProcessRunning(string processName)
	{
		return Process.GetProcessesByName(processName).Length > 0;
	}

	public static bool IsSteamWindowActive()
	{
		try
		{
			return Process.GetProcessesByName("steam")
				.Any(p =>
				{
					try { return p.MainWindowHandle != IntPtr.Zero; }
					catch { return false; }
				});
		}
		catch { return false; }
	}

	private static int _oskCheckTick;
	private static bool _oskRunning;

	public static bool IsOskRunning()
	{
		int now = Environment.TickCount;
		if (now - _oskCheckTick > 500)
		{
			_oskCheckTick = now;
			_oskRunning = IsProcessRunning("osk");
		}
		return _oskRunning;
	}
}

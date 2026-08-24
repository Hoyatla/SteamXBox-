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
	private const uint MOUSEEVENTF_HWHEEL = 0x01000;
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

	private static int _refused;
	private static int _lastError;

	/// <summary>
	/// Sends input, and remembers when Windows refuses it.
	/// </summary>
	/// <remarks>
	/// Every call here used to discard the return value. <c>SendInput</c> reports how many events it
	/// actually inserted, and it inserts none when the injection is refused — most often because a
	/// window running at a higher integrity level holds the foreground, which is exactly the state a
	/// user is in when something stops responding.
	///
	/// <para>
	/// The counters were incremented regardless, so the log read "mouse events=103" for a second in
	/// which nothing had been sent at all. Measured 12 August: the core claimed 103 movements while
	/// the monitor's low-level hook, watching the whole machine, saw not one injected event. A
	/// counter that reports success it did not verify is worse than no counter — it sends the search
	/// somewhere else entirely, and it did, for hours.
	/// </para>
	/// </remarks>
	private static uint Send(uint count, INPUT[] inputs)
	{
		var sent = SendInput(count, inputs, Marshal.SizeOf<INPUT>());

		if (sent < count)
		{
			Interlocked.Increment(ref _refused);
			_lastError = Marshal.GetLastWin32Error();
		}

		return sent;
	}

	/// <summary>How many injections Windows has refused, and why the last one failed.</summary>
	public static (int Count, int LastError) Refused => (Volatile.Read(ref _refused), _lastError);

	/// <summary>Reads the refusals since the last call and clears the tally.</summary>
	public static (int Count, int LastError) DrainRefused()
	{
		var count = Interlocked.Exchange(ref _refused, 0);
		return (count, _lastError);
	}

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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
		Send(2, new[] { down, up });
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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
		Send(1, new[] { input });
	}

	/// <summary>Horizontal wheel, used for side-scrolling from the left pad's X axis.</summary>
	public static void MouseHorizontalWheel(int delta)
	{
		INPUT input = new INPUT
		{
			Type = INPUT_MOUSE,
			Union = new INPUTUNION
			{
				Mouse = new MOUSEINPUT
				{
					MouseData = unchecked((uint)(delta * WHEEL_DELTA)),
					DwFlags = MOUSEEVENTF_HWHEEL
				}
			}
		};
		Send(1, new[] { input });
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
		Send(1, new[] { input });
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
			IntPtr hWnd = GetForegroundWindow();
			if (hWnd == IntPtr.Zero) return false;
			uint pid;
			GetWindowThreadProcessId(hWnd, out pid);
			if (pid == 0) return false;
			using var proc = Process.GetProcessById((int)pid);
			return string.Equals(proc.ProcessName, "steam", StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	private static int _oskCheckTick;
	private static bool _oskRunning;

	[DllImport("user32.dll")]
	private static extern bool SystemParametersInfo(int action, int param, ref int value, int winIni);

	private const int SPI_GETMOUSESPEED = 0x0070;

	/// <summary>
	/// Reads the Windows pointer speed setting (1–20, default 10) so the right-pad trackball
	/// can be calibrated against it.  At 100 % the GUI matches this speed; lower percentages
	/// slow the cursor proportionally.
	/// </summary>
	public static int GetWindowsMouseSpeed()
	{
		int speed = 10;
		SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0);
		return Math.Clamp(speed, 1, 20);
	}

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

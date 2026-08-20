using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class ScreenSettingsHelper : MonoBehaviour
{
	private class ResolutionComparer : IComparer<Resolution>
	{
		public int Compare(Resolution x, Resolution y)
		{
			if (x.width == y.width)
			{
				return y.height.CompareTo(x.height);
			}
			if (x.width <= y.width)
			{
				return 1;
			}
			return -1;
		}
	}

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private List<Dropdown.OptionData> m_OptionDataCache = new List<Dropdown.OptionData>();

	private Resolution[] m_Resolutions;

	private Resolution[] m_EmptyResolution = new Resolution[1];

	private List<DisplayInfo> m_Displays = new List<DisplayInfo>();

	private bool m_MoveWindowInProgress;

	private Vector2Int m_LastWindowPosition;

	public static readonly FullScreenMode[] FullScreenModes = new FullScreenMode[2]
	{
		FullScreenMode.Windowed,
		FullScreenMode.FullScreenWindow
	};

	private static readonly ResolutionComparer s_ResolutionComparer = new ResolutionComparer();

	private static readonly Dictionary<(int, int), string> s_ResolutionNameCache = new Dictionary<(int, int), string>();

	private static readonly Dictionary<DisplayInfo, string> s_DisplayNameCache = new Dictionary<DisplayInfo, string>();

	[SerializeField]
	private bool allowFullscreen = true;

	[SerializeField]
	private float aspectRatioWidth = 16f;

	[SerializeField]
	private float aspectRatioHeight = 9f;

	[SerializeField]
	private int minWidthPixel = 16;

	[SerializeField]
	private int minHeightPixel = 9;

	[SerializeField]
	private int maxWidthPixel = 2160;

	[SerializeField]
	private int maxHeightPixel = 3840;

	private float aspect;

	private int setWidth = -1;

	private int setHeight = -1;

	private bool wasFullscreenLastFrame;

	private bool isAspectRatioAdjusterInitialized;

	private int pixelHeightOfCurrentScreen;

	private int pixelWidthOfCurrentScreen;

	private bool quitStarted;

	private const int WM_SIZING = 532;

	private const int WMSZ_LEFT = 1;

	private const int WMSZ_RIGHT = 2;

	private const int WMSZ_TOP = 3;

	private const int WMSZ_BOTTOM = 6;

	private const int GWLP_WNDPROC = -4;

	private WndProcDelegate wndProcDelegate;

	private const string UNITY_WND_CLASSNAME = "UnityWndClass";

	private IntPtr unityHWnd;

	private IntPtr oldWndProcPtr;

	private IntPtr newWndProcPtr;

	private void Start()
	{
		m_LastWindowPosition = new Vector2Int(int.MinValue, int.MaxValue);
	}

	public void SetVsync(bool isChecked)
	{
		QualitySettings.vSyncCount = (isChecked ? 1 : 0);
	}

	public void ChangeFullScreenMode(int index)
	{
		FullScreenMode fullScreenMode = FullScreenModes[index];
		if (Screen.fullScreenMode != fullScreenMode)
		{
			Debug.Log("Setting FullScreenMode." + fullScreenMode);
			if (fullScreenMode == FullScreenMode.Windowed)
			{
				Screen.fullScreenMode = fullScreenMode;
				return;
			}
			DisplayInfo mainWindowDisplayInfo = Screen.mainWindowDisplayInfo;
			Screen.SetResolution(mainWindowDisplayInfo.width, mainWindowDisplayInfo.height, fullScreenMode);
		}
	}

	public Resolution[] GetAvailableResolutions()
	{
		Resolution[] resolutions = Screen.resolutions;
		List<Resolution> list = new List<Resolution>();
		if (resolutions.Length != 0)
		{
			list = resolutions.Where((Resolution r) => !Mathf.Approximately(r.height / 3 * 4, r.width)).ToList();
			list = (from r in list
				group r by new { r.width, r.height } into g
				select g.First() into r
				orderby r.width * r.height descending
				select r).ToList();
		}
		else
		{
			list.Add(Screen.currentResolution);
		}
		m_Resolutions = list.ToArray();
		return m_Resolutions;
	}

	public void ChangeResolution(int index)
	{
		Resolution resolution = m_Resolutions[index];
		if (Screen.width != resolution.width || Screen.height != resolution.height)
		{
			Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreenMode);
		}
	}

	public List<DisplayInfo> GetAvailableDisplays()
	{
		Screen.GetDisplayLayout(m_Displays);
		if (m_Displays.Count == 0)
		{
			Resolution currentResolution = Screen.currentResolution;
			DisplayInfo item = new DisplayInfo
			{
				name = "Generic Monitor"
			};
			item.width = currentResolution.width;
			item.height = currentResolution.height;
			item.workArea = new RectInt(0, 0, currentResolution.width, currentResolution.height);
			item.refreshRate.denominator = 1u;
			m_Displays.Add(item);
		}
		Vector2Int mainWindowPosition = Screen.mainWindowPosition;
		DisplayInfo mainWindowDisplayInfo = Screen.mainWindowDisplayInfo;
		for (int i = 0; i < m_Displays.Count; i++)
		{
			m_Displays[i].Equals(mainWindowDisplayInfo);
		}
		if (m_LastWindowPosition != mainWindowPosition)
		{
			m_LastWindowPosition = mainWindowPosition;
			Debug.Log($"Main Window Position: [{mainWindowPosition.x}; {mainWindowPosition.y}]");
		}
		return m_Displays;
	}

	public async UniTask ChangeDisplay(int index)
	{
		await MoveToDisplay(index);
	}

	private async UniTask MoveToDisplay(int index)
	{
		try
		{
			DisplayInfo display = m_Displays[index];
			Debug.Log("Moving window to " + display.name);
			Vector2Int position = new Vector2Int(0, 0);
			if (Screen.fullScreenMode != FullScreenMode.Windowed)
			{
				position.x += display.width / 2;
				position.y += display.height / 2;
			}
			await Screen.MoveMainWindowTo(in display, position);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
		}
	}

	private IEnumerator SnapWindow(DisplayInfo display, RectInt targetCoords)
	{
		if (targetCoords.width > 0 || targetCoords.height > 0)
		{
			m_MoveWindowInProgress = true;
			try
			{
				Screen.SetResolution(targetCoords.width, targetCoords.height, FullScreenMode.Windowed);
				Debug.Log("Snapping to " + targetCoords);
				Vector2Int position = new Vector2Int(targetCoords.x, targetCoords.y);
				yield return Screen.MoveMainWindowTo(in display, position);
			}
			finally
			{
				m_MoveWindowInProgress = false;
			}
		}
	}

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetWindowRect(IntPtr hwnd, ref RECT lpRect);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowLong")]
	private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowLongPtr")]
	private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	private void InitializeAspectRatioAdjuster()
	{
		if (Application.isEditor)
		{
			return;
		}
		Application.wantsToQuit += ApplicationWantsToQuit;
		EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr hWnd, IntPtr lParam)
		{
			StringBuilder stringBuilder = new StringBuilder("UnityWndClass".Length + 1);
			GetClassName(hWnd, stringBuilder, stringBuilder.Capacity);
			if (stringBuilder.ToString() == "UnityWndClass")
			{
				unityHWnd = hWnd;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		SetAspectRatio(aspectRatioWidth, aspectRatioHeight, apply: true);
		wasFullscreenLastFrame = Screen.fullScreen;
		wndProcDelegate = wndProc;
		newWndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
		oldWndProcPtr = SetWindowLong(unityHWnd, -4, newWndProcPtr);
		isAspectRatioAdjusterInitialized = true;
	}

	public void SetAspectRatio(float newAspectWidth, float newAspectHeight, bool apply)
	{
		aspectRatioWidth = newAspectWidth;
		aspectRatioHeight = newAspectHeight;
		aspect = aspectRatioWidth / aspectRatioHeight;
		if (apply)
		{
			Screen.SetResolution(Screen.width, Mathf.RoundToInt((float)Screen.width / aspect), Screen.fullScreen);
		}
	}

	private IntPtr wndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == 532)
		{
			RECT structure = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));
			RECT lpRect = default(RECT);
			GetWindowRect(unityHWnd, ref lpRect);
			RECT lpRect2 = default(RECT);
			GetClientRect(unityHWnd, ref lpRect2);
			int num = lpRect.Right - lpRect.Left - (lpRect2.Right - lpRect2.Left);
			int num2 = lpRect.Bottom - lpRect.Top - (lpRect2.Bottom - lpRect2.Top);
			structure.Right -= num;
			structure.Bottom -= num2;
			int num3 = Mathf.Clamp(structure.Right - structure.Left, minWidthPixel, maxWidthPixel);
			int num4 = Mathf.Clamp(structure.Bottom - structure.Top, minHeightPixel, maxHeightPixel);
			switch (wParam.ToInt32())
			{
			case 1:
				structure.Left = structure.Right - num3;
				structure.Bottom = structure.Top + Mathf.RoundToInt((float)num3 / aspect);
				break;
			case 2:
				structure.Right = structure.Left + num3;
				structure.Bottom = structure.Top + Mathf.RoundToInt((float)num3 / aspect);
				break;
			case 3:
				structure.Top = structure.Bottom - num4;
				structure.Right = structure.Left + Mathf.RoundToInt((float)num4 * aspect);
				break;
			case 6:
				structure.Bottom = structure.Top + num4;
				structure.Right = structure.Left + Mathf.RoundToInt((float)num4 * aspect);
				break;
			case 8:
				structure.Right = structure.Left + num3;
				structure.Bottom = structure.Top + Mathf.RoundToInt((float)num3 / aspect);
				break;
			case 5:
				structure.Right = structure.Left + num3;
				structure.Top = structure.Bottom - Mathf.RoundToInt((float)num3 / aspect);
				break;
			case 7:
				structure.Left = structure.Right - num3;
				structure.Bottom = structure.Top + Mathf.RoundToInt((float)num3 / aspect);
				break;
			case 4:
				structure.Left = structure.Right - num3;
				structure.Top = structure.Bottom - Mathf.RoundToInt((float)num3 / aspect);
				break;
			}
			setWidth = structure.Right - structure.Left;
			setHeight = structure.Bottom - structure.Top;
			structure.Right += num;
			structure.Bottom += num2;
			Marshal.StructureToPtr(structure, lParam, fDeleteOld: true);
		}
		return CallWindowProc(oldWndProcPtr, hWnd, msg, wParam, lParam);
	}

	private void TryAdjustRatio()
	{
		if (Application.isEditor)
		{
			return;
		}
		if (!allowFullscreen && Screen.fullScreen)
		{
			Screen.fullScreen = false;
		}
		if (Screen.fullScreen && !wasFullscreenLastFrame)
		{
			int height;
			int width;
			if (aspect < (float)pixelWidthOfCurrentScreen / (float)pixelHeightOfCurrentScreen)
			{
				height = pixelHeightOfCurrentScreen;
				width = Mathf.RoundToInt((float)pixelHeightOfCurrentScreen * aspect);
			}
			else
			{
				width = pixelWidthOfCurrentScreen;
				height = Mathf.RoundToInt((float)pixelWidthOfCurrentScreen / aspect);
			}
			Screen.SetResolution(width, height, fullscreen: true);
		}
		else if (!Screen.fullScreen && wasFullscreenLastFrame)
		{
			Screen.SetResolution(setWidth, setHeight, fullscreen: false);
		}
		else if (!Screen.fullScreen && setWidth != -1 && setHeight != -1 && (Screen.width != setWidth || Screen.height != setHeight))
		{
			setHeight = Screen.height;
			setWidth = Mathf.RoundToInt((float)Screen.height * aspect);
			if (!IsAspectRatioValid(Screen.width, Screen.height))
			{
				Screen.SetResolution(setWidth, setHeight, Screen.fullScreen);
			}
		}
		else if (!Screen.fullScreen)
		{
			pixelHeightOfCurrentScreen = Screen.currentResolution.height;
			pixelWidthOfCurrentScreen = Screen.currentResolution.width;
		}
		wasFullscreenLastFrame = Screen.fullScreen;
		bool IsAspectRatioValid(int screenWidth, int screenHeight)
		{
			float num = (float)screenWidth / (float)screenHeight;
			float num2 = aspectRatioWidth / aspectRatioHeight;
			float num3 = 0.01f;
			if (Mathf.Abs(num - num2) > num3)
			{
				return false;
			}
			return true;
		}
	}

	private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
	{
		if (IntPtr.Size == 4)
		{
			return SetWindowLong32(hWnd, nIndex, dwNewLong);
		}
		return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
	}

	private bool ApplicationWantsToQuit()
	{
		if (!isAspectRatioAdjusterInitialized)
		{
			return false;
		}
		if (!quitStarted)
		{
			StartCoroutine("DelayedQuit");
			return false;
		}
		return true;
	}

	private IEnumerator DelayedQuit()
	{
		SetWindowLong(unityHWnd, -4, oldWndProcPtr);
		yield return new WaitForEndOfFrame();
		quitStarted = true;
		Application.Quit();
	}
}

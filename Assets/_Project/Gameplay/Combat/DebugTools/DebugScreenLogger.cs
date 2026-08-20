using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.DebugTools
{
	public class DebugScreenLogger : MonoBehaviour
	{
		private struct LogEntry
		{
			public string Message;

			public float ScreenTime;
		}

		public static DebugScreenLogger Instance;

		[SerializeField]
		private KeyCode _toggleKey = KeyCode.Home;

		private bool _showDebugUI = true;

		private Queue<LogEntry> _logQueue = new Queue<LogEntry>();

		private bool _isProcessingQueue;

		private string _currentMessage = "";

		private bool _showMessage;

		private string _statusMessage = "";

		private bool _showStatusMessage;

		private Coroutine _statusCoroutine;

		private float _basePopupWidth = 250f;

		private float _basePadding = 20f;

		private int _baseFontSize = 14;

		private void Awake()
		{
			if (Instance != null)
			{
				Object.Destroy(Instance);
			}
			Instance = this;
		}

		public void Log(string message, float displayTime = 1.5f)
		{
		}

		private IEnumerator ProcessQueue()
		{
			_isProcessingQueue = true;
			while (_logQueue.Count > 0)
			{
				LogEntry logEntry = _logQueue.Dequeue();
				yield return StartCoroutine(ShowLog(logEntry.Message, logEntry.ScreenTime));
			}
			_isProcessingQueue = false;
		}

		private IEnumerator ShowLog(string message, float displayTime = 1.5f)
		{
			_currentMessage = message;
			_showMessage = true;
			yield return new WaitForSecondsRealtime(displayTime);
			_showMessage = false;
		}

		private IEnumerator ShowStatusLog(string message, float displayTime = 1.5f)
		{
			_statusMessage = message;
			_showStatusMessage = true;
			yield return new WaitForSecondsRealtime(displayTime);
			_showStatusMessage = false;
		}
	}
}

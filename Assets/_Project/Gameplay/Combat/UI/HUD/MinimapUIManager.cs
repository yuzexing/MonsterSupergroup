using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class MinimapUIManager : MonoBehaviour
	{
		private struct MinimapIconCreationRequest
		{
			public Transform Target;

			public Sprite IconSprite;

			public float IconSize;

			public MinimapIcon.PingMode PingMode;

			public MinimapIconType IconType;

			public Action<MinimapIcon> OnCreatedCallback;
		}

		public enum MinimapIconType
		{
			None = 0,
			Boss = 1,
			Health = 2,
			Quest = 3,
			Shrine = 4,
			Ulti = 5
		}

		[Serializable]
		public struct MinimapPingSoundEntry
		{
			public MinimapIconType Type;

			public EventReference PingOne;

			public EventReference PingTwo;
		}

		public static MinimapUIManager Instance;

		[SerializeField]
		private MinimapIcon minimapIconPrefab;

		[SerializeField]
		private int frameDivider = 2;

		[SerializeField]
		private float heightInUnits = 60f;

		private MinimapUIView _currentMinimapUIView;

		private List<MinimapIcon> _icons = new List<MinimapIcon>();

		private Transform _followTarget;

		private List<MinimapIconCreationRequest> _deferredRequests = new List<MinimapIconCreationRequest>();

		private Stack<MinimapIcon> _iconsPool = new Stack<MinimapIcon>();

		private readonly int MinimapGridPositionSID = Shader.PropertyToID("_MinimapPosition");

		[Header("Ping Sounds")]
		[SerializeField]
		private MinimapPingSoundEntry[] pingSounds;

		private Dictionary<MinimapIconType, MinimapPingSoundEntry> _pingSoundLookup;

		public int FrameDivider => frameDivider;

		public float HeightInUnits => heightInUnits;

		public IReadOnlyList<MinimapIcon> Icons => _icons;

		public Transform FollowTarget => _followTarget;

		private void Awake()
		{
			Instance = this;
			BuildPingSoundLookup();
			AssignTarget(GameDirector.Instance.Player.transform);
		}

		private void OnDestroy()
		{
			Instance = null;
		}

		public void RegisterMinimapUI(MinimapUIView minimapUIView)
		{
			_currentMinimapUIView = minimapUIView;
			ProcessDeferredRequests();
		}

		public void UnRegisterMinimapUI(MinimapUIView minimapUIView)
		{
			if (_currentMinimapUIView == minimapUIView)
			{
				_currentMinimapUIView = null;
			}
		}

		public void RegisterIcon(MinimapIcon icon)
		{
			if (!_icons.Contains(icon))
			{
				_icons.Add(icon);
			}
		}

		public void UnRegisterIcon(MinimapIcon icon)
		{
			if (_icons.Contains(icon))
			{
				_icons.Remove(icon);
			}
		}

		private MinimapIcon GetNewIcon(Transform target, Sprite iconSprite, float iconSize, MinimapIcon.PingMode pingMode = MinimapIcon.PingMode.None, MinimapIconType iconType = MinimapIconType.None)
		{
			if (_currentMinimapUIView == null)
			{
				Debug.LogWarning("[Minimap] Direct creation failed for " + target.name + ": UI View not registered yet. Use CreateMinimapIconDeferred instead.");
				return null;
			}
			if (_iconsPool.TryPop(out var result))
			{
				result.gameObject.SetActive(value: true);
			}
			else
			{
				result = UnityEngine.Object.Instantiate(minimapIconPrefab, _currentMinimapUIView.GetMinimapIconContainer());
			}
			result.Hide();
			result.SetTarget(target);
			result.SetIcon(iconSprite);
			result.SetSize(iconSize);
			result.SetPing(pingMode);
			result.SetIconType(iconType);
			result.Init();
			return result;
		}

		public void RequestMinimapIcon(Transform target, Sprite iconSprite, float iconSize, MinimapIcon.PingMode pingMode = MinimapIcon.PingMode.None, MinimapIconType iconType = MinimapIconType.None, Action<MinimapIcon> onCreated = null)
		{
			_deferredRequests.Add(new MinimapIconCreationRequest
			{
				Target = target,
				IconSprite = iconSprite,
				IconSize = iconSize,
				PingMode = pingMode,
				IconType = iconType,
				OnCreatedCallback = onCreated
			});
			ProcessDeferredRequests();
		}

		private void ProcessDeferredRequests()
		{
			if (_currentMinimapUIView == null || _deferredRequests.Count == 0)
			{
				return;
			}
			for (int i = 0; i < _deferredRequests.Count; i++)
			{
				MinimapIconCreationRequest minimapIconCreationRequest = _deferredRequests[i];
				if (!(minimapIconCreationRequest.Target == null))
				{
					MinimapIcon newIcon = GetNewIcon(minimapIconCreationRequest.Target, minimapIconCreationRequest.IconSprite, minimapIconCreationRequest.IconSize, minimapIconCreationRequest.PingMode, minimapIconCreationRequest.IconType);
					minimapIconCreationRequest.OnCreatedCallback?.Invoke(newIcon);
				}
			}
			_deferredRequests.Clear();
		}

		private void AssignTarget(Transform target)
		{
			_followTarget = target;
		}

		public void ReturnToPool(MinimapIcon icon)
		{
			UnRegisterIcon(icon);
			if ((bool)icon && (bool)icon.gameObject)
			{
				icon.gameObject.SetActive(value: false);
			}
			if (_iconsPool == null)
			{
				_iconsPool = new Stack<MinimapIcon>();
			}
			_iconsPool.Push(icon);
		}

		private void FixedUpdate()
		{
			if (Time.frameCount % frameDivider == 0 && !(_followTarget == null))
			{
				UpdateIcons();
				UpdateGrid();
			}
		}

		private void UpdateIcons()
		{
			for (int num = _icons.Count - 1; num >= 0; num--)
			{
				if (_icons[num] != null)
				{
					_icons[num].OnUpdate();
				}
			}
		}

		private void UpdateGrid()
		{
			Vector2 vector = _followTarget.position;
			float num = heightInUnits * 0.5f;
			Shader.SetGlobalVector(MinimapGridPositionSID, vector / num);
		}

		private void BuildPingSoundLookup()
		{
			_pingSoundLookup = new Dictionary<MinimapIconType, MinimapPingSoundEntry>();
			if (pingSounds != null)
			{
				for (int i = 0; i < pingSounds.Length; i++)
				{
					_pingSoundLookup[pingSounds[i].Type] = pingSounds[i];
				}
			}
		}

		public void PlayPingSound(MinimapIconType iconType, int pingNumber)
		{
			if (TryGetPingEvent(iconType, pingNumber, out var pingEvent))
			{
				RuntimeManager.PlayOneShot(pingEvent);
			}
		}

		public bool TryGetPingEvent(MinimapIconType iconType, int pingNumber, out EventReference pingEvent)
		{
			pingEvent = default(EventReference);
			if (iconType == MinimapIconType.None || _pingSoundLookup == null)
			{
				return false;
			}
			if (!_pingSoundLookup.TryGetValue(iconType, out var value))
			{
				return false;
			}
			pingEvent = ((pingNumber <= 1) ? value.PingOne : value.PingTwo);
			return !pingEvent.IsNull;
		}
	}
}

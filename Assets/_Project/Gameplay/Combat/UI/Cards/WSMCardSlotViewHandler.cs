using AstralShift.Control;
using AstralShift.HellMaiden.UI.Cards;
using Cysharp.Threading.Tasks;
using FMODUnity;
using Rewired;
using UnityEngine;

public class WSMCardSlotViewHandler : MonoBehaviour
{
	[Header("References")]
	[SerializeField]
	private Canvas canvas;

	[SerializeField]
	protected WSMCardSlotView slotView;

	[SerializeField]
	protected CanvasGroup canvasGroup;

	protected int _siblingIndex;

	protected Transform _transform;

	private RectTransform _rectTransform;

	private int _defaultOrderInLayer;

	protected bool _hasBeenDropped;

	[SerializeField]
	private UICardViewMouseHandler _mouseHandler;

	[SerializeField]
	private UICardViewGamepadHandler _gamepadHandler;

	[Header("Soundss")]
	[SerializeField]
	protected EventReference onSelectSound;

	public Canvas Canvas => canvas;

	public WSMCardSlotView SlotView => slotView;

	public int SiblingIndex => _siblingIndex;

	public Transform Transform
	{
		get
		{
			if (_transform == null)
			{
				_transform = base.transform;
			}
			return _transform;
		}
	}

	public RectTransform RectTransform
	{
		get
		{
			if (!_rectTransform)
			{
				TryGetComponent<RectTransform>(out _rectTransform);
			}
			return _rectTransform;
		}
	}

	public Vector3 GlobalScale => Transform.lossyScale / ((Canvas != null) ? Canvas.scaleFactor : 1f);

	public bool HasBeenDropped => _hasBeenDropped;

	public UICardViewMouseHandler MouseHandler
	{
		get
		{
			return _mouseHandler;
		}
		set
		{
			_mouseHandler = value;
		}
	}

	public UICardViewGamepadHandler GamepadHandler
	{
		get
		{
			return _gamepadHandler;
		}
		set
		{
			_gamepadHandler = value;
		}
	}

	public virtual void Initialize()
	{
		ControllerLifetime.OnBeforeControllerChanged += SwitchInputHandler;
		SlotView.Init(this);
	}

	public virtual UniTask InitializeAsync()
	{
		ControllerLifetime.OnBeforeControllerChanged += SwitchInputHandler;
		return SlotView.InitAsync(this);
	}

	public void SwitchInputHandler(ControllerType controllerType)
	{
		if (controllerType != ControllerType.Mouse)
		{
			if ((bool)MouseHandler)
			{
				MouseHandler.enabled = false;
			}
			if ((bool)GamepadHandler)
			{
				GamepadHandler.enabled = true;
			}
		}
		else
		{
			if ((bool)GamepadHandler)
			{
				GamepadHandler.enabled = false;
			}
			if ((bool)MouseHandler)
			{
				MouseHandler.enabled = true;
			}
		}
	}

	private void OnDestroy()
	{
		ControllerLifetime.OnBeforeControllerChanged -= SwitchInputHandler;
		SlotView.Dispose();
	}

	public void Show()
	{
		SlotView.Show();
	}

	public void Hide()
	{
		SlotView.Hide();
	}

	public void LockMotion(bool state)
	{
		if (state)
		{
			SlotView.LockAllMotion();
		}
		else
		{
			SlotView.UnlockAllMotion();
		}
	}

	public void AllowInteraction(bool value)
	{
		canvasGroup.interactable = value;
		canvasGroup.blocksRaycasts = value;
	}

	public virtual void SetSiblingIndex(int index)
	{
		_siblingIndex = index;
		Transform.SetSiblingIndex(_siblingIndex);
		SlotView.SetSiblingIndex(_siblingIndex);
		SlotView.RefreshIdleAnimation();
	}

	public void SetParentContainer(Transform transform)
	{
		Transform.SetParent(transform, worldPositionStays: true);
	}
}

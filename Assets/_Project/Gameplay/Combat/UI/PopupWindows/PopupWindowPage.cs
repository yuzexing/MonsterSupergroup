using UnityEngine;

public class PopupWindowPage : MonoBehaviour
{
	[SerializeField]
	protected Animator animator;

	private RectTransform _rectTransform;

	protected readonly int OpenParam = Animator.StringToHash("Open");

	protected readonly int CloseParam = Animator.StringToHash("Close");

	public RectTransform RectTransform
	{
		get
		{
			if (!_rectTransform)
			{
				_rectTransform = GetComponent<RectTransform>();
			}
			return _rectTransform;
		}
	}

	public void Open()
	{
		animator.Play(OpenParam);
	}

	public void Close()
	{
		animator.Play(CloseParam);
	}
}

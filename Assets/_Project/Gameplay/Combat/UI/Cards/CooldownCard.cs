using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;
using UnityEngine.UI;

public class CooldownCard : MonoBehaviour
{
	private WeaponBehaviour assignedWeapon;

	[SerializeField]
	private RectTransform rectTransform;

	[SerializeField]
	private Image fillImage;

	[Header("Handle")]
	[SerializeField]
	private RectTransform handle;

	private void OnEnable()
	{
		fillImage.fillAmount = 0f;
		handle.gameObject.SetActive(value: false);
	}

	public void SetWeapon(WeaponBehaviour weapon)
	{
		assignedWeapon = weapon;
		if (weapon != null)
		{
			handle.gameObject.SetActive(value: true);
			return;
		}
		fillImage.fillAmount = 0f;
		handle.gameObject.SetActive(value: false);
	}

	private void Update()
	{
		if (!(assignedWeapon == null))
		{
			fillImage.fillAmount = assignedWeapon.LastAttackElapsedTime / assignedWeapon.GetCooldown();
			if (handle != null)
			{
				handle.anchoredPosition = new Vector2(handle.anchoredPosition.x, rectTransform.rect.height * fillImage.fillAmount);
			}
		}
	}
}

using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI
{
	public class UIIconPing : MonoBehaviour
	{
		[SerializeField]
		private Image pingImage;

		[SerializeField]
		private float referenceScale = 1f;

		[SerializeField]
		private float thickness = 0.05f;

		[Header("Single Ping")]
		[SerializeField]
		private float singlePingDuration = 2f;

		[SerializeField]
		private float singlePingSize = 1f;

		[SerializeField]
		private CustomAnimationCurve singlePingEase;

		[Header("Continuous Ping")]
		[SerializeField]
		private float continuousPingDuration = 2f;

		[SerializeField]
		private float continuousPingSize = 1.5f;

		[SerializeField]
		private int continuousPingMaxPulses = 4;

		[SerializeField]
		private CustomAnimationCurve continuousPingEase;

		private Sequence _singlePing;

		private Sequence _continuousPing;

		private int _continuousPulseCount;

		private const float PulseCallbackEpsilon = 0.001f;

		private readonly int PingThicknessSID = Shader.PropertyToID("_Thickness");

		private readonly int PingProgress1SID = Shader.PropertyToID("_Progress1");

		private readonly int PingProgress2SID = Shader.PropertyToID("_Progress2");

		private Material pingMaterial
		{
			get
			{
				if (pingImage == null || pingImage.materialForRendering == null)
				{
					return null;
				}
				return pingImage?.materialForRendering;
			}
		}

		private bool IsInitialized { get; set; }

		public event Action<int> PulseStarted;

		private void Awake()
		{
			if (!IsInitialized)
			{
				IsInitialized = true;
				pingImage.material = new Material(pingImage.materialForRendering);
				ApplyThickness();
			}
		}

		public void ApplySize(float size)
		{
			pingImage.transform.localScale = Vector3.one * size;
		}

		public void ApplyThickness()
		{
			pingMaterial.SetFloat(PingThicknessSID, GetThickness());
		}

		private float GetThickness()
		{
			float x = base.transform.localScale.x;
			if (x > 0f)
			{
				float num = referenceScale / x;
				return thickness * num;
			}
			return thickness;
		}

		public UniTask RunPingOnce()
		{
			if (!pingMaterial)
			{
				return UniTask.CompletedTask;
			}
			float num = singlePingDuration / 1.5f;
			float atPosition = num / 2f;
			CancelPing();
			pingMaterial.SetFloat(PingProgress1SID, 0f);
			pingMaterial.SetFloat(PingProgress2SID, 0f);
			ApplySize(singlePingSize);
			ApplyThickness();
			_singlePing = DOTween.Sequence();
			_singlePing.Append(pingMaterial.DOFloat(1f, PingProgress1SID, num).From(0f));
			_singlePing.Insert(atPosition, pingMaterial.DOFloat(1f, PingProgress2SID, num).From(0f));
			_singlePing.InsertCallback(0.001f, delegate
			{
				this.PulseStarted?.Invoke(1);
			});
			_singlePing.InsertCallback(atPosition, delegate
			{
				this.PulseStarted?.Invoke(2);
			});
			_singlePing.SetEase(singlePingEase.GetEaseFunction());
			_singlePing.SetLink(base.gameObject);
			_singlePing.SetUpdate(UpdateType.Late);
			return _singlePing.ToUniTask(TweenCancelBehaviour.Kill, this.GetCancellationTokenOnDestroy());
		}

		public void RunContinuousPing()
		{
			if ((bool)pingMaterial)
			{
				pingImage.enabled = true;
				float num = continuousPingDuration / 1.5f;
				float atPosition = num / 2f;
				CancelPing();
				pingMaterial.SetFloat(PingProgress1SID, 0f);
				pingMaterial.SetFloat(PingProgress2SID, 0f);
				ApplySize(continuousPingSize);
				ApplyThickness();
				_continuousPulseCount = 0;
				_continuousPing = DOTween.Sequence();
				_continuousPing.Append(pingMaterial.DOFloat(1f, PingProgress1SID, num).From(0f));
				_continuousPing.Insert(atPosition, pingMaterial.DOFloat(1f, PingProgress2SID, num).From(0f));
				_continuousPing.InsertCallback(0.001f, delegate
				{
					TryInvokeContinuousPulse(1);
				});
				_continuousPing.InsertCallback(atPosition, delegate
				{
					TryInvokeContinuousPulse(2);
				});
				_continuousPing.SetEase(continuousPingEase.GetEaseFunction());
				_continuousPing.SetLoops(-1, LoopType.Restart);
				_continuousPing.SetLink(base.gameObject);
				_continuousPing.SetUpdate(UpdateType.Late);
			}
		}

		private void TryInvokeContinuousPulse(int pingNumber)
		{
			if (_continuousPulseCount < continuousPingMaxPulses)
			{
				_continuousPulseCount++;
				this.PulseStarted?.Invoke(pingNumber);
			}
		}

		public void CancelPing()
		{
			if (_continuousPing != null)
			{
				_continuousPing.Kill();
				_continuousPing = null;
			}
			if (_singlePing != null)
			{
				_singlePing.Kill();
				_singlePing = null;
			}
			pingMaterial?.SetFloat(PingProgress1SID, 0f);
			pingMaterial?.SetFloat(PingProgress2SID, 0f);
		}
	}
}

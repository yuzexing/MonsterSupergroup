using TMPro;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.Cinematics.Timeline
{
	public class TimelineSubtitlesMixer : PlayableBehaviour
	{
		private TextMeshProUGUI _textMeshPro;

		private Vector2 defaultTMPPos;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (!_textMeshPro)
			{
				_textMeshPro = playerData as TextMeshProUGUI;
				if ((bool)_textMeshPro)
				{
					defaultTMPPos = _textMeshPro.rectTransform.anchoredPosition;
				}
			}
			if (!_textMeshPro)
			{
				return;
			}
			string text = "";
			float a = 0f;
			int inputCount = playable.GetInputCount();
			for (int i = 0; i < inputCount; i++)
			{
				float inputWeight = playable.GetInputWeight(i);
				if (inputWeight > 0f)
				{
					TimelineSubtitleBehaviour behaviour = ((ScriptPlayable<TimelineSubtitleBehaviour>)playable.GetInput(i)).GetBehaviour();
					text = behaviour.Text;
					a = inputWeight;
					if (Application.isPlaying)
					{
						_textMeshPro.rectTransform.anchoredPosition = (behaviour.HasPositionOverride ? behaviour.Position : defaultTMPPos);
					}
				}
			}
			_textMeshPro.text = text;
			_textMeshPro.color = new Color(1f, 1f, 1f, a);
		}
	}
}

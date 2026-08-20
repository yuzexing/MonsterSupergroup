using UnityEngine;

namespace AstralShift.Rendering.Shaders.WindShader
{
	public class TreeWindManager : MonoBehaviour
	{
		[Header("Grass Settings")]
		[Space]
		public float grassWindNoiseSpeed = 2f;

		private float _lastGrassWindNoiseSpeed;

		public float grassWindNoiseScale = 0.1f;

		private float _lastGrassWindNoiseScale;

		public float grassWindIntensityFrom = -0.3f;

		private float _lastGrassWindIntensityFrom;

		public float grassWindIntensityTo = 0.3f;

		private float _lastGrassWindIntensityTo;

		[Space]
		[Header("Trees Settings")]
		[Space]
		public float treesWindSpeed = 2f;

		private float _lastTreesWindSpeed;

		public float treesWindFrequency = 0.1f;

		private float _lastTreesWindFrequency;

		public float treesWindAngle;

		private float _lastTreesWindAngle;

		private int _grassWindNoiseScaleID;

		private int _grassWindNoiseSpeedID;

		private int _grassWindIntensityFromID;

		private int _grassWindIntensityToID;

		private int _treesWindSpeedID;

		private int _treesWindFrequencyID;

		private int _treesWindAngleID;

		private Texture2D _arrow;

		private void Awake()
		{
			_grassWindNoiseScaleID = Shader.PropertyToID("WindNoiseScale");
			_grassWindNoiseSpeedID = Shader.PropertyToID("WindNoiseSpeed");
			_grassWindIntensityFromID = Shader.PropertyToID("WindMinIntensity");
			_grassWindIntensityToID = Shader.PropertyToID("WindMaxIntensity");
			_treesWindSpeedID = Shader.PropertyToID("WindSpeed");
			_treesWindFrequencyID = Shader.PropertyToID("WindFrequency");
			_treesWindAngleID = Shader.PropertyToID("WindAngle");
		}

		private void FixedUpdate()
		{
			if (_lastGrassWindNoiseScale != grassWindNoiseScale)
			{
				Shader.SetGlobalFloat(_grassWindNoiseScaleID, grassWindNoiseScale);
				_lastGrassWindNoiseScale = grassWindNoiseScale;
			}
			if (_lastGrassWindNoiseSpeed != grassWindNoiseSpeed)
			{
				Shader.SetGlobalFloat(_grassWindNoiseSpeedID, grassWindNoiseSpeed);
				_lastGrassWindNoiseSpeed = grassWindNoiseSpeed;
			}
			if (_lastGrassWindIntensityFrom != grassWindIntensityFrom)
			{
				Shader.SetGlobalFloat(_grassWindIntensityFromID, grassWindIntensityFrom);
				_lastGrassWindIntensityFrom = grassWindIntensityFrom;
			}
			if (_lastGrassWindIntensityTo != grassWindIntensityTo)
			{
				Shader.SetGlobalFloat(_grassWindIntensityToID, grassWindIntensityTo);
				_lastGrassWindIntensityTo = grassWindIntensityTo;
			}
			if (_lastTreesWindSpeed != treesWindSpeed)
			{
				Shader.SetGlobalFloat(_treesWindSpeedID, treesWindSpeed);
				_lastTreesWindSpeed = treesWindSpeed;
			}
			if (_lastTreesWindFrequency != treesWindFrequency)
			{
				Shader.SetGlobalFloat(_treesWindFrequencyID, treesWindFrequency);
				_lastTreesWindFrequency = treesWindFrequency;
			}
			if (_lastTreesWindAngle != treesWindAngle)
			{
				Shader.SetGlobalFloat(_treesWindAngleID, treesWindAngle);
				_lastTreesWindAngle = treesWindAngle;
			}
		}

		public void ResetValues()
		{
			grassWindNoiseScale = 0f;
			grassWindIntensityFrom = 0f;
			grassWindIntensityTo = 0f;
			grassWindNoiseSpeed = 0f;
			treesWindAngle = 0f;
			treesWindFrequency = 0f;
			treesWindSpeed = 0f;
		}
	}
}

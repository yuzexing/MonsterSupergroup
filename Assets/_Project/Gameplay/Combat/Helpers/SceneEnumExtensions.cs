using System;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;

namespace AstralShift.Helpers
{
	public static class SceneEnumExtensions
	{
		public static SceneEnum ConvertToSceneEnum(this string str)
		{
			try
			{
				return (SceneEnum)Enum.Parse(typeof(SceneEnum), str);
			}
			catch
			{
				Debug.LogWarning("Scene not found WARNING: Defaulting to Fallback [0] scene.");
				return SceneEnum.Loading;
			}
		}

		public static string[] GetSceneEnumNames()
		{
			return Enum.GetNames(typeof(SceneEnum));
		}
	}
}

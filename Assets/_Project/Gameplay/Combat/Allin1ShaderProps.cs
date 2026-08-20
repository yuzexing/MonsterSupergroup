using UnityEngine;

public class Allin1ShaderProps
{
	public const string URPShaderName = "AllIn1SpriteShader/AllIn1CustomUrp2dRenderer";

	public static int GlowColor { get; } = Shader.PropertyToID("_GlowColor");

	public static int GlowColorIntensity { get; } = Shader.PropertyToID("_Glow");

	public static int GlobalGlowIntensity { get; } = Shader.PropertyToID("_GlowGlobal");

	public static int FadeTexture { get; } = Shader.PropertyToID("_FadeTex");

	public static int FadeAmount { get; } = Shader.PropertyToID("_FadeAmount");

	public static int FadeBurnColor { get; } = Shader.PropertyToID("_FadeBurnColor");

	public static int GradientBlend { get; } = Shader.PropertyToID("_GradBlend");

	public static int GreyScaleBlend { get; } = Shader.PropertyToID("_GreyscaleBlend");

	public static int BlurIntensity { get; } = Shader.PropertyToID("_BlurIntensity");

	public static int BlurHD { get; } = Shader.PropertyToID("_BlurHD");

	public static string MotionBlurOn => "MOTIONBLUR_ON";

	public static int MotionBlurAngle { get; } = Shader.PropertyToID("_MotionBlurAngle");

	public static int MotionBlurDistance { get; } = Shader.PropertyToID("_MotionBlurDist");

	public static int ChromaticAberrationAmount { get; } = Shader.PropertyToID("_ChromAberrAmount");

	public static int ShineLocation { get; } = Shader.PropertyToID("_ShineLocation");

	public static int ShineWidth { get; } = Shader.PropertyToID("_ShineWidth");

	public static int Contrast { get; } = Shader.PropertyToID("_Contrast");

	public static int Brightness { get; } = Shader.PropertyToID("_Brightness");

	public static int DistortionAmount { get; } = Shader.PropertyToID("_DistortAmount");

	public static int PinchAmount { get; } = Shader.PropertyToID("_PinchUvAmount");

	public static int HitEffectColor { get; } = Shader.PropertyToID("_HitEffectColor");

	public static int HitEffectGlow { get; } = Shader.PropertyToID("_HitEffectGlow");

	public static int HitEffectBlend { get; } = Shader.PropertyToID("_HitEffectBlend");

	public static int OverlayColor { get; } = Shader.PropertyToID("_OverlayColor");

	public static int OverlayGlow { get; } = Shader.PropertyToID("_OverlayGlow");

	public static int OverlayBlend { get; } = Shader.PropertyToID("_OverlayBlend");
}

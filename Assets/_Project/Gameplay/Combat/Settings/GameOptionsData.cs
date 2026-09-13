using System;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Options
{
    [Serializable]
    public sealed class GameOptionsData
    {
        public int Version = 1;
        public string Language = "zh-CN";
        public float MasterVolume = 1, MusicVolume = 1, EffectsVolume = 1;
        public bool ScreenShake = true;
        public FullScreenMode DisplayMode;
        public int Width, Height;
        public uint RefreshNumerator, RefreshDenominator = 1;
        public bool VSync;
        public int FrameLimit = 60;
        public float RenderScale = 1;
        public int Msaa = 1, TextureLimit;

        public GameOptionsData Copy() => (GameOptionsData)MemberwiseClone();

        public void CopyGraphicsFrom(GameOptionsData source)
        {
            CopyDisplayFrom(source);
            VSync = source.VSync; FrameLimit = source.FrameLimit;
            RenderScale = source.RenderScale; Msaa = source.Msaa; TextureLimit = source.TextureLimit;
        }

        public void CopyDisplayFrom(GameOptionsData source)
        {
            DisplayMode = source.DisplayMode; Width = source.Width; Height = source.Height;
            RefreshNumerator = source.RefreshNumerator; RefreshDenominator = source.RefreshDenominator;
        }

        public bool SameDisplay(GameOptionsData other) => DisplayMode == other.DisplayMode && Width == other.Width &&
            Height == other.Height && RefreshNumerator == other.RefreshNumerator && RefreshDenominator == other.RefreshDenominator;

        public bool SameGraphics(GameOptionsData other) => SameDisplay(other) && VSync == other.VSync &&
            FrameLimit == other.FrameLimit && Mathf.Approximately(RenderScale, other.RenderScale) &&
            Msaa == other.Msaa && TextureLimit == other.TextureLimit;
    }
}

using System;
using System.Globalization;

namespace MonsterSupergroup.Builds
{
    public enum VersionUpdate { Patch, Minor, Major }

    public readonly struct GameVersion : IComparable<GameVersion>
    {
        public readonly int Major, Minor, Patch;
        public GameVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || patch < 0) throw new ArgumentOutOfRangeException(nameof(major));
            Major = major; Minor = minor; Patch = patch;
        }
        public static bool TryParse(string text, out GameVersion version)
        {
            version = default;
            var parts = text?.Split('.');
            if (parts == null || parts.Length != 3) return false;
            var numbers = new int[3];
            for (int i = 0; i < 3; i++)
                if (string.IsNullOrEmpty(parts[i]) || (parts[i].Length > 1 && parts[i][0] == '0') ||
                    !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0) return false;
            version = new GameVersion(numbers[0], numbers[1], numbers[2]); return true;
        }
        public static GameVersion Parse(string text) => TryParse(text, out var version) ? version :
            throw new FormatException("游戏版本必须是三段非负整数，例如 0.0.0（不含前缀、后缀或前导零）。");
        public GameVersion Increment(VersionUpdate update)
        {
            checked
            {
                return update switch {
                    VersionUpdate.Patch => new GameVersion(Major, Minor, Patch + 1),
                    VersionUpdate.Minor => new GameVersion(Major, Minor + 1, 0),
                    VersionUpdate.Major => new GameVersion(Major + 1, 0, 0),
                    _ => throw new ArgumentOutOfRangeException(nameof(update))
                };
            }
        }
        public int CompareTo(GameVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            return result == 0 ? Patch.CompareTo(other.Patch) : result;
        }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);
    }
}

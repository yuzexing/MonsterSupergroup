using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public static class EvidenceJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
            Culture = CultureInfo.InvariantCulture, TypeNameHandling = TypeNameHandling.None,
            ContractResolver = new EvidenceResolver(),
            FloatFormatHandling = FloatFormatHandling.String, ReferenceLoopHandling = ReferenceLoopHandling.Error,
            NullValueHandling = NullValueHandling.Ignore, Converters = { new UnsignedIdConverter(), new CombatContextConverter() } };
        public static string Encode(object value) => JsonConvert.SerializeObject(value, Settings);
        public static string EncodeBounded(object value, int maximumBytes = 16 << 20)
        {
            using var writer = new BoundedWriter(maximumBytes);
            using var json = new JsonTextWriter(writer);
            JsonSerializer.Create(Settings).Serialize(json, value); json.Flush(); return writer.ToString();
        }
        private sealed class BoundedWriter : StringWriter
        {
            private readonly int maximum; private int bytes;
            public BoundedWriter(int maximum) : base(CultureInfo.InvariantCulture) { this.maximum = maximum; }
            private void Charge(int count) { if ((long)bytes + count > maximum) throw new InvalidDataException("SerializedEvidenceLimitExceeded"); bytes += count; }
            public override void Write(char value) { Charge(value < 128 ? 1 : 3); base.Write(value); }
            public override void Write(string value) { if (value != null) Charge(Encoding.UTF8.GetByteCount(value)); base.Write(value); }
            public override void Write(char[] buffer, int index, int count) { Charge(Encoding.UTF8.GetByteCount(buffer, index, count)); base.Write(buffer, index, count); }
        }
        public static T Decode<T>(string value) => JsonConvert.DeserializeObject<T>(value, Settings);
        public static T Convert<T>(JToken value) => value.ToObject<T>(JsonSerializer.Create(Settings));
        public static string Hash(byte[] bytes) => Hash(bytes, bytes.Length);
        internal static string Hash(byte[] bytes, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (count < 0 || count > bytes.Length) throw new ArgumentOutOfRangeException(nameof(count));
            byte[] digest = HashBytes(bytes, count);
            const string hex = "0123456789abcdef";
            var text = new char[digest.Length * 2];
            for (int i = 0; i < digest.Length; i++) { text[i * 2] = hex[digest[i] >> 4]; text[i * 2 + 1] = hex[digest[i] & 15]; }
            return new string(text);
        }
        // Unity Mono's SHA256 providers all use its managed implementation on Windows.
        // BCryptHash computes the same SHA-256 with the OS provider; its pseudo-handle
        // owns no resource and is safe for concurrent disk/replication workers.
        // https://learn.microsoft.com/windows/win32/api/bcrypt/nf-bcrypt-bcrypthash
        private static volatile bool nativeHashAvailable = Environment.OSVersion.Platform == PlatformID.Win32NT;
        public static string HashImplementation => nativeHashAvailable ? "WindowsBCryptSHA256" : "FrameworkSHA256";
        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptHash(IntPtr algorithm, IntPtr secret, uint secretBytes,
            [In] byte[] input, uint inputBytes, [Out] byte[] output, uint outputBytes);
        private static byte[] HashBytes(byte[] bytes, int count)
        {
            if (nativeHashAvailable)
            {
                try
                {
                    var digest = new byte[32];
                    // BCRYPT_SHA256_ALG_HANDLE, available on Windows 10 and later.
                    if (BCryptHash(new IntPtr(0x41), IntPtr.Zero, 0, bytes, (uint)count, digest, 32) == 0) return digest;
                    nativeHashAvailable = false;
                }
                catch (DllNotFoundException) { nativeHashAvailable = false; }
                catch (EntryPointNotFoundException) { nativeHashAvailable = false; }
            }
            using var hash = SHA256.Create();
            return hash.ComputeHash(bytes, 0, count);
        }
        internal static void CompressInto(byte[] bytes, Stream output, EvidenceStageMetrics metrics = null)
            => CompressInto(bytes, bytes.Length, output, metrics);
        internal static void CompressInto(byte[] bytes, int count, Stream output, EvidenceStageMetrics metrics = null)
        {
            long started = EvidenceStageMetrics.Now;
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(bytes, 0, count);
            metrics?.Compression(started);
        }
        public static byte[] Compress(byte[] bytes, EvidenceStageMetrics metrics = null)
        {
            long started = EvidenceStageMetrics.Now;
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(bytes, 0, bytes.Length);
            byte[] result = output.ToArray(); metrics?.Compression(started); return result;
        }
        public static byte[] Decompress(byte[] bytes, int maximumBytes)
        {
            using var input = new MemoryStream(bytes, false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192]; int count;
            while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (output.Length + count > maximumBytes) throw new InvalidDataException("Evidence block exceeds limit.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        public static void AtomicWrite(string path, string text)
        {
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            // Windows scanners can briefly hold the destination without delete sharing. Retry only on the disk worker.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException error)
                {
                    if (attempt == 3) throw new IOException("EvidenceMetadataWriteFailed " + path + " (" + error.HResult + ")", error);
                    System.Threading.Thread.Sleep(10 << attempt);
                }
            }
        }
        private sealed class UnsignedIdConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(ulong);
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(((ulong)value).ToString(CultureInfo.InvariantCulture));
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer) => ulong.Parse(System.Convert.ToString(reader.Value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
        private sealed class CombatContextConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(MonsterSupergroup.GAS.CombatContext);
            public override bool CanWrite => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                var data = JObject.Load(reader);
                var id = data["EventId"].ToObject<MonsterSupergroup.GAS.CombatEventId>(serializer);
                // default context is a valid absence-of-origin value in a status checkpoint, but its validating constructor rejects it.
                if (!id.IsValid) return default(MonsterSupergroup.GAS.CombatContext);
                return new MonsterSupergroup.GAS.CombatContext(id,
                    data["RootEventId"].ToObject<MonsterSupergroup.GAS.CombatEventId>(serializer),
                    data["ParentEventId"].ToObject<MonsterSupergroup.GAS.CombatEventId>(serializer),
                    data["Sequence"].Value<uint>(), data["ChainDepth"].Value<ushort>(), data["SourcePlayerId"].Value<uint>(),
                    data["SourceEntityId"].Value<uint>(), data["TargetEntityId"].Value<uint>(), data["AbilityId"].Value<uint>(),
                    data["BuildId"].Value<uint>(), data["Tags"].ToObject<MonsterSupergroup.GAS.CombatTags>(serializer), data["TargetStateVersion"].Value<uint>());
            }
        }
        private sealed class EvidenceResolver : DefaultContractResolver
        {
            protected override System.Collections.Generic.IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization)
            {
                // Unity vectors expose computed normalized vectors: serializing those recursively is not evidence.
                if (type.Namespace == "UnityEngine" && type.IsValueType)
                {
                    var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                    if (fields.Length > 0) return fields.Select(f => base.CreateProperty(f, serialization)).ToList();
                }
                return base.CreateProperties(type, serialization);
            }
        }
    }
}

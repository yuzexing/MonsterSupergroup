using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat.Content
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Enemies/Catalog")]
    public sealed class EnemyDefinitionCatalog : ScriptableObject
    {
        [SerializeField] private EnemyDefinition[] definitions = Array.Empty<EnemyDefinition>();
        [SerializeField, HideInInspector] private string contentHash;
        public IReadOnlyList<EnemyDefinition> Definitions => Array.AsReadOnly(definitions ?? Array.Empty<EnemyDefinition>());
        public string ContentHash => contentHash;
        public EnemyDefinitionRegistry Capture() => new EnemyDefinitionRegistry(Definitions, contentHash);
#if UNITY_EDITOR
        public void SetAuthoringDefinitions(IEnumerable<EnemyDefinition> entries)
        { definitions = entries.Distinct().OrderBy(x => x.IdText, StringComparer.Ordinal).ToArray(); }
        public void SetContentHash(string value) => contentHash = value;
#endif
    }

    // A content lookup, never a second combat state or enemy simulation.
    public sealed class EnemyDefinitionRegistry
    {
        private readonly Dictionary<Guid, EnemyDefinitionSnapshot> entries = new Dictionary<Guid, EnemyDefinitionSnapshot>();
        public string Fingerprint { get; }
        public IEnumerable<EnemyDefinitionSnapshot> Entries => entries.Values;
        public EnemyDefinitionRegistry(IEnumerable<EnemyDefinition> definitions, string dependencyHash = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var signature = new StringBuilder("enemy-definition-protocol-1|").Append(dependencyHash).Append('|');
            foreach (var definition in definitions.OrderBy(x => x != null ? x.IdText : "", StringComparer.Ordinal))
            {
                if (definition == null) throw new ArgumentException("Enemy catalog contains a missing asset.");
                var snapshot = definition.Capture();
                if (entries.ContainsKey(snapshot.Id)) throw new ArgumentException("Duplicate DefinitionId: " + snapshot.Id + " (" + definition.name + ")");
                entries.Add(snapshot.Id, snapshot);
                signature.Append(snapshot.Id.ToString("N")).Append('|').Append(JsonUtility.ToJson(snapshot.Values)).Append('|')
                    .Append(snapshot.Appearance.ContentHash).Append('|');
            }
            using var sha = SHA256.Create();
            Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-", "").ToLowerInvariant();
        }
        public bool Contains(Guid id) => entries.ContainsKey(id);
        public EnemyDefinitionSnapshot Resolve(Guid id) => entries.TryGetValue(id, out var entry) ? entry :
            throw new ArgumentException("Unknown enemy DefinitionId: " + id.ToString("N") + ". Check the Boot catalog/content version.");
    }
}

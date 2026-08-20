using System.Collections.Concurrent;
using Cysharp.Threading.Tasks;

namespace AstralShift.HellMaiden.Interactions
{
	public class TimedCollection<T>
	{
		private readonly ConcurrentDictionary<T, byte> _items = new ConcurrentDictionary<T, byte>();

		public int Count => _items.Count;

		public void Add(T item)
		{
			_items[item] = 0;
			RemoveLater(item);
		}

		private async UniTask RemoveLater(T item)
		{
			await UniTask.Delay(1000);
			_items.TryRemove(item, out var _);
		}

		public bool Contains(T item)
		{
			return _items.ContainsKey(item);
		}
	}
}

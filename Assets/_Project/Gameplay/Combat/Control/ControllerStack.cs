using System.Collections;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden;

namespace AstralShift.Control
{
	public class ControllerStack<T> : Stack where T : GameController
	{
		public new T Pop()
		{
			T val = base.Pop() as T;
			if (val != null)
			{
				val.Deactivate();
			}
			if (Count > 0)
			{
				T val2 = Peek();
				if (val2 != null)
				{
					if (GameDirector.Instance.QuittingMenu && !(val2 is GameMenuController))
					{
						GameDirector.Instance.QuittingMenu = false;
					}
					val2.Activate();
				}
			}
			return val;
		}

		public void Push(T controller)
		{
			if (Count > 0)
			{
				Peek().Deactivate();
			}
			base.Push(controller);
			controller.Activate();
		}

		public T Replace(T controller)
		{
			T val = null;
			if ((bool)Peek())
			{
				val = base.Pop() as T;
				if (val != null)
				{
					val.Deactivate();
				}
			}
			Push(controller);
			return val;
		}

		public new T Peek()
		{
			return base.Peek() as T;
		}

		public new T[] ToArray()
		{
			T[] array = new T[base.Count];
			base.CopyTo(array, 0);
			return array;
		}
	}
}

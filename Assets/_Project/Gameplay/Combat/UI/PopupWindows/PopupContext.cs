using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.UI.PopupWindows
{
	public struct PopupContext
	{
		public struct ScrollableElement
		{
			public List<int> Indices;
		}

		public List<string> Texts;

		public List<int> Indices;

		public List<Action<int>> IndicesActions;

		public List<Action> Actions;

		public List<Sprite> Sprites;

		public PopupWindowContentReference ContentValue;

		public Vector2 Position;

		public PopupContext(params object[] context)
		{
			this = default(PopupContext);
			Texts = new List<string>();
			Indices = new List<int>();
			Actions = new List<Action>();
			IndicesActions = new List<Action<int>>();
			Sprites = new List<Sprite>();
			foreach (object obj in context)
			{
				if (!(obj is string item))
				{
					if (!(obj is int item2))
					{
						if (!(obj is IEnumerable<string> collection))
						{
							if (!(obj is IEnumerable<int> collection2))
							{
								if (!(obj is Action item3))
								{
									if (!(obj is Action<int> item4))
									{
										if (!(obj is Sprite item5))
										{
											if (!(obj is Vector2 position))
											{
												if (obj is PopupWindowContentReference contentValue)
												{
													ContentValue = contentValue;
												}
											}
											else
											{
												Position = position;
											}
										}
										else
										{
											Sprites.Add(item5);
										}
									}
									else
									{
										IndicesActions.Add(item4);
									}
								}
								else
								{
									Actions.Add(item3);
								}
							}
							else
							{
								Indices.AddRange(collection2);
							}
						}
						else
						{
							Texts.AddRange(collection);
						}
					}
					else
					{
						Indices.Add(item2);
					}
				}
				else
				{
					Texts.Add(item);
				}
			}
		}
	}
}

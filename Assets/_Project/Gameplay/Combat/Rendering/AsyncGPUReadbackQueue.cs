using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralShift.Rendering
{
	public class AsyncGPUReadbackQueue
	{
		private object owner;

		private readonly Queue<Action> _jobQueue = new Queue<Action>();

		private bool _isJobRunning;

		public AsyncGPUReadbackQueue(object owner)
		{
			this.owner = owner;
		}

		public void EnqueueRequest<T>(ComputeBuffer buffer, Action<NativeArray<T>> onComplete, Action onError = null) where T : struct
		{
			if (!buffer.IsValid())
			{
				return;
			}
			_jobQueue.Enqueue(delegate
			{
				AsyncGPUReadback.Request(buffer, delegate(AsyncGPUReadbackRequest request)
				{
					try
					{
						HandleRequest(request, onComplete, onError);
					}
					catch (Exception exception)
					{
						Debug.LogError("GPUReadbackJobQueue: Error while executing AsyncGPUReadback request for " + owner.GetType().Name + ".");
						Debug.LogException(exception);
						_isJobRunning = false;
						TryExecuteNext();
					}
				});
			});
			TryExecuteNext();
		}

		public void EnqueueRequest<T>(ComputeBuffer buffer, int count, Action<NativeArray<T>> onComplete, Action onError = null) where T : struct
		{
			if (!buffer.IsValid())
			{
				return;
			}
			_jobQueue.Enqueue(delegate
			{
				AsyncGPUReadback.Request(buffer, delegate(AsyncGPUReadbackRequest request)
				{
					HandleRequest(request, delegate(NativeArray<T> array)
					{
						try
						{
							int length = Mathf.Min(count, array.Length);
							onComplete(array.GetSubArray(0, length));
						}
						catch (Exception exception)
						{
							Debug.LogError("GPUReadbackJobQueue: Error while executing SubArray callback for " + owner.GetType().Name + ".");
							Debug.LogException(exception);
						}
					}, onError);
				});
			});
			TryExecuteNext();
		}

		private void HandleRequest<T>(AsyncGPUReadbackRequest request, Action<NativeArray<T>> onComplete, Action onError) where T : struct
		{
			_isJobRunning = false;
			if (request.hasError)
			{
				Debug.LogError("GPUReadbackJobQueue: Error while executing AsyncGPUReadback request for " + owner.GetType().Name + ".");
				try
				{
					onError?.Invoke();
					return;
				}
				finally
				{
					TryExecuteNext();
				}
			}
			try
			{
				onComplete?.Invoke(request.GetData<T>());
			}
			finally
			{
				TryExecuteNext();
			}
		}

		private void TryExecuteNext()
		{
			if (!_isJobRunning && _jobQueue.Count != 0)
			{
				_isJobRunning = true;
				_jobQueue.Dequeue()();
			}
		}

		public void Clear()
		{
			_jobQueue.Clear();
			_isJobRunning = false;
		}
	}
}

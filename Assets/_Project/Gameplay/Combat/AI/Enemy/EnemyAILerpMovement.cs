using System;
using System.Collections.Generic;
using Pathfinding;
using Pathfinding.Util;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[RequireComponent(typeof(Seeker))]
	public class EnemyAILerpMovement : BaseEnemyMovement
	{
		public bool interpolatePathSwitches = true;

		public float switchPathInterpolationSpeed = 5f;

		protected float _distanceToDestination;

		private Vector3 _tangent;

		protected OnPathDelegate _onPathComplete;

		protected Seeker _seeker;

		protected ABPath _path;

		protected bool _canSearchAgain = true;

		protected Vector3 _previousMovementOrigin;

		protected Vector3 _previousMovementDirection;

		protected float _pathSwitchInterpolationTime;

		protected PathInterpolator.Cursor _interpolator;

		protected PathInterpolator _interpolatorPath = new PathInterpolator();

		private bool _startHasRun;

		private Vector3 _simulatedPosition;

		public bool reachedEndOfPath { get; private set; }

		public bool reachedDestination
		{
			get
			{
				if (!reachedEndOfPath || !_interpolator.valid)
				{
					return false;
				}
				Vector3 vector = _destination - _interpolator.endPoint;
				vector.z = 0f;
				if (RemainingDistance + vector.magnitude >= 0.05f)
				{
					return false;
				}
				return true;
			}
		}

		public override Vector3 Destination
		{
			get
			{
				return _destination;
			}
			set
			{
				_destination = value;
				SearchPath();
			}
		}

		public Vector3 Tangent => _tangent;

		public bool updatePosition { get; set; } = true;

		public Vector3 Position
		{
			get
			{
				if (_transform == null)
				{
					return _simulatedPosition;
				}
				if (!updatePosition)
				{
					return _simulatedPosition;
				}
				return _transform.position;
			}
		}

		public float RemainingDistance
		{
			get
			{
				if (!_interpolator.valid)
				{
					return float.PositiveInfinity;
				}
				return Mathf.Max(_interpolator.remainingDistance, 0f);
			}
			set
			{
				if (!_interpolator.valid)
				{
					throw new InvalidOperationException("Cannot set the remaining distance on the AILerp component because it doesn't have a path to follow.");
				}
				_interpolator.remainingDistance = Mathf.Max(value, 0f);
			}
		}

		public bool hasPath => _interpolator.valid;

		public bool pathPending => !_canSearchAgain;

		public bool isStopped { get; set; }

		public Action onSearchPath { get; set; }

		protected EnemyAILerpMovement()
		{
			_destination = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
		}

		protected virtual void Awake()
		{
			_seeker = GetComponent<Seeker>();
			_seeker.startEndModifier.adjustStartPoint = () => _simulatedPosition;
		}

		protected virtual void Start()
		{
			_startHasRun = true;
			Init();
		}

		protected virtual void OnEnable()
		{
			_onPathComplete = OnPathComplete;
			Init();
		}

		protected void Init()
		{
			if (_startHasRun)
			{
				Teleport(Position, clearPath: false);
			}
		}

		protected void OnDisable()
		{
			ClearPath();
		}

		public void GetRemainingPath(List<Vector3> buffer, out bool stale)
		{
			buffer.Clear();
			if (!_interpolator.valid)
			{
				buffer.Add(Position);
				stale = true;
			}
			else
			{
				stale = false;
				_interpolator.GetRemainingPath(buffer);
				buffer[0] = Position;
			}
		}

		public void GetRemainingPath(List<Vector3> buffer, List<PathPartWithLinkInfo> partsBuffer, out bool stale)
		{
			GetRemainingPath(buffer, out stale);
			if (partsBuffer != null)
			{
				partsBuffer.Clear();
				partsBuffer.Add(new PathPartWithLinkInfo
				{
					startIndex = 0,
					endIndex = buffer.Count - 1
				});
			}
		}

		public void Teleport(Vector3 position, bool clearPath = true)
		{
			if (clearPath)
			{
				ClearPath();
			}
			_simulatedPosition = position;
			if (updatePosition && _transform != null)
			{
				_transform.position = position;
			}
			reachedEndOfPath = false;
			if (clearPath)
			{
				SearchPath();
			}
		}

		public virtual void SearchPath()
		{
			if (!float.IsPositiveInfinity(_destination.x))
			{
				if (onSearchPath != null)
				{
					onSearchPath();
				}
				Vector3 feetPosition = GetFeetPosition();
				_canSearchAgain = false;
				ABPath aBPath = ABPath.Construct(feetPosition, _destination);
				aBPath.nnConstraint.graphMask = GraphMask.everything;
				SetPath(aBPath, updateDestinationFromPath: false);
			}
		}

		public virtual void OnTargetReached()
		{
		}

		protected virtual void OnPathComplete(Path path)
		{
			if (!(path is ABPath aBPath))
			{
				throw new Exception("This function only handles ABPaths, do not use special path types");
			}
			_canSearchAgain = true;
			aBPath.Claim(this);
			if (aBPath.error)
			{
				aBPath.Release(this);
				return;
			}
			if (interpolatePathSwitches)
			{
				ConfigurePathSwitchInterpolation();
			}
			ABPath path2 = _path;
			_path = aBPath;
			reachedEndOfPath = false;
			if (_path is RandomPath randomPath)
			{
				_destination = randomPath.originalEndPoint;
			}
			else if (_path is MultiTargetPath multiTargetPath)
			{
				_destination = multiTargetPath.originalEndPoint;
			}
			if (_path.vectorPath != null && _path.vectorPath.Count == 1)
			{
				_path.vectorPath.Insert(0, GetFeetPosition());
			}
			ConfigureNewPath();
			path2?.Release(this);
			if (_interpolator.remainingDistance < 0.0001f && !reachedEndOfPath)
			{
				reachedEndOfPath = true;
				OnTargetReached();
			}
		}

		protected virtual void ClearPath()
		{
			if ((UnityEngine.Object)(object)_seeker != null)
			{
				_seeker.CancelCurrentPathRequest();
			}
			_canSearchAgain = true;
			reachedEndOfPath = false;
			if (_path != null)
			{
				_path.Release(this);
			}
			_path = null;
			_interpolatorPath.SetPath(null);
		}

		public void SetPath(Path path, bool updateDestinationFromPath = true)
		{
			if (updateDestinationFromPath && path is ABPath aBPath && !(path is RandomPath))
			{
				_destination = aBPath.originalEndPoint;
			}
			if (path == null)
			{
				ClearPath();
				return;
			}
			if (path.PipelineState == PathState.Created)
			{
				_canSearchAgain = false;
				_seeker.CancelCurrentPathRequest();
				_seeker.StartPath(path, _onPathComplete);
				return;
			}
			if (path.PipelineState >= PathState.Returning)
			{
				if (_seeker.GetCurrentPath() != path)
				{
					_seeker.CancelCurrentPathRequest();
				}
				OnPathComplete(path);
				return;
			}
			throw new ArgumentException("You must call the SetPath method with a path that either has been completely calculated or one whose path calculation has not been started at all. It looks like the path calculation for the path you tried to use has been started, but is not yet finished.");
		}

		protected virtual void ConfigurePathSwitchInterpolation()
		{
			bool flag = _interpolator.valid && _interpolator.remainingDistance < 0.0001f;
			if (_interpolator.valid && !flag)
			{
				_previousMovementOrigin = _interpolator.position;
				_previousMovementDirection = _interpolator.tangent.normalized * _interpolator.remainingDistance;
				_tangent = (Mathf.Approximately(_interpolator.tangent.normalized.magnitude, 0f) ? _tangent : _interpolator.tangent.normalized);
				_pathSwitchInterpolationTime = 0f;
			}
			else
			{
				_previousMovementOrigin = Vector3.zero;
				_previousMovementDirection = Vector3.zero;
				_pathSwitchInterpolationTime = float.PositiveInfinity;
			}
		}

		public virtual Vector3 GetFeetPosition()
		{
			return enemyController.MovementCenterPosition;
		}

		protected virtual void ConfigureNewPath()
		{
			bool valid = _interpolator.valid;
			Vector3 vector = (valid ? _interpolator.tangent : Vector3.zero);
			_interpolatorPath.SetPath(_path.vectorPath);
			_interpolator = _interpolatorPath.start;
			_interpolator.MoveToClosestPoint(GetFeetPosition());
			if (interpolatePathSwitches && switchPathInterpolationSpeed > 0.01f && valid)
			{
				float num = Mathf.Max(0f - Vector3.Dot(vector.normalized, _interpolator.tangent.normalized), 0f);
				_interpolator.distance -= base.Speed * num * (1f / switchPathInterpolationSpeed);
			}
		}

		public override void MovementUpdate()
		{
			if (_transform == null)
			{
				return;
			}
			if (!_canMove)
			{
				_rigidbody.linearVelocity = Vector3.zero;
				return;
			}
			if (updatePosition)
			{
				_simulatedPosition = enemyController.MovementCenterPosition;
			}
			Vector3 nextPosition = CalculateNextPosition(isStopped ? 0f : Time.fixedDeltaTime);
			FinalizeMovement(nextPosition);
		}

		protected void FinalizeMovement(Vector3 nextPosition)
		{
			if (_path != null)
			{
				_direction = (_path.endPoint - _simulatedPosition).normalized;
				_direction.Normalize();
			}
			_distanceToDestination = (_destination - _simulatedPosition).magnitude;
			_simulatedPosition = nextPosition;
			_rigidbody.linearVelocity = _tangent * base.Speed;
			if (enemyController.enemyFlyingType || enemyController.forceWindInteraction)
			{
				_rigidbody.linearVelocity += enemyController.windDirection + enemyController.ultimateWindInteraction * enemyController.stats.WindMultiplier;
			}
		}

		protected virtual Vector3 CalculateNextPosition(float deltaTime)
		{
			if (!_interpolator.valid)
			{
				return _simulatedPosition;
			}
			_interpolator.distance += deltaTime * base.Speed;
			_tangent = _interpolator.tangent.normalized;
			if (_interpolator.remainingDistance < 0.0001f && !reachedEndOfPath)
			{
				reachedEndOfPath = true;
				OnTargetReached();
			}
			_pathSwitchInterpolationTime += deltaTime;
			float num = switchPathInterpolationSpeed * _pathSwitchInterpolationTime;
			if (interpolatePathSwitches && num < 1f)
			{
				return Vector3.Lerp(_previousMovementOrigin + Vector3.ClampMagnitude(_previousMovementDirection, base.Speed * _pathSwitchInterpolationTime), _interpolator.position, num);
			}
			return _interpolator.position;
		}
	}
}

using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Helpers;
using AstralShift.Helpers.Attributes;
using AstralShift.Pooling;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using FMOD.Studio;
using FMODUnity;
// using PixelCrushers.DialogueSystem;
using Unity.Mathematics;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Minos
{
	public class JudgementBeamAttackBehaviour : BossAttackBehaviour
	{
		[Header("Settings")]
		[SerializeField]
		protected AnimatedBossAttack attackPrefab;

		[SerializeField]
		[ReadOnly]
		protected List<AnimatedBossAttack> attacks;

		[Header("Positioning")]
		[SerializeField]
		private MinosMovementController movementController;

		[SerializeField]
		protected Transform centerPosition;

		[SerializeField]
		protected Transform[] outerPositions;

		private int _currentPositionIndex;

		[SerializeField]
		[ReadOnly]
		protected List<Transform> availablePositions;

		protected Transform positionRemoved;

		[SerializeField]
		protected float coneAngle;

		[SerializeField]
		protected float totalAngle;

		[SerializeField]
		protected float maxDistance;

		[Header("Beam Settings")]
		[SerializeField]
		protected int numberOfBeams;

		[SerializeField]
		protected float beamPrecisionTickInSeconds = 0.3f;

		[SerializeField]
		protected float rotationSpeed = 40f;

		[SerializeField]
		protected AnimationCurve rotationCurve;

		private GenericPooler<AnimatedBossAttack> _pooler;

		private List<Tween> _rotationTweens;

		private int _beamsEndedCounter;

		private List<EventInstance> _FMODInstances;

		private List<Coroutine> _audioRoutines;

		protected Coroutine _fetchPlayerPositionCoroutine;

		protected Vector3 _lastPlayerPosition;

		[SerializeField]
		private bool followPlayer;

		[SerializeField]
		private bool alwaysInitPositions;

		[SerializeField]
		private Vector2 direction;

		[SerializeField]
		private bool moveWhileAttacking;

		[SerializeField]
		private bool applyScrollingBackground;

		protected string eventName = "event:/sx/dlg/sx_dlg_vo";

		[SerializeField]
		private List<string> VALineId;

		[Range(-1f, 1f)]
		[SerializeField]
		private int angleRaiseDirection;

		private float _soundVelocityPeak = 1f;

		private List<Tuple<Action, Tuple<float, float>>> _tempGizmos;

		public override void Init(BossController controller)
		{
			base.Init(controller);
			movementController.Init(controller);
			movementController.SetRigidBody(controller.RigidBody);
			InitializeAvailablePositions();
		}

		public void InitializeAvailablePositions()
		{
			if (availablePositions != null)
			{
				availablePositions = new List<Transform>();
				if (centerPosition != null)
				{
					availablePositions.Add(centerPosition);
				}
				availablePositions.AddRange(outerPositions);
			}
		}

		public override void Positioning()
		{
			if (availablePositions.Count > 0)
			{
				movementController.StopMovement();
				Transform transform = (alwaysInitPositions ? availablePositions[0] : availablePositions[UnityEngine.Random.Range(0, availablePositions.Count)]);
				movementController.SetDestination(transform.position, onPositioningEnd, moveSpeed, shootBullets ? shooter : null);
				if (!alwaysInitPositions && positionRemoved != null)
				{
					availablePositions.Add(positionRemoved);
				}
				availablePositions.Remove(transform);
				positionRemoved = transform;
				movementController.ResumeMovement();
				_currentPositionIndex++;
				StartFetchingPlayerPosition();
			}
			else
			{
				StartFetchingPlayerPosition();
				onPositioningEnd?.Invoke();
			}
		}

		private void StartFetchingPlayerPosition()
		{
			StopFetchingPlayerPosition();
			_fetchPlayerPositionCoroutine = StartCoroutine(FetchPlayerPositionCoroutine());
		}

		private void StopFetchingPlayerPosition()
		{
			if (_fetchPlayerPositionCoroutine != null)
			{
				StopCoroutine(_fetchPlayerPositionCoroutine);
			}
		}

		protected IEnumerator FetchPlayerPositionCoroutine()
		{
			WaitForSeconds waitForSeconds = new WaitForSeconds(beamPrecisionTickInSeconds);
			while (true)
			{
				_lastPlayerPosition = GameDirector.Instance.Player.EnemyAttackTarget.position;
				yield return waitForSeconds;
			}
		}

		public override void Warning()
		{
			if (applyScrollingBackground)
			{
				bossController.ApplyScrollingBackground();
			}
			BarkWarning();
			movementController.StopMovement();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(null);
			LaunchAttack();
			// DialogueManager.instance.gameObject.GetComponent<FmodProgramerEventPlayer>().PlayRandomDialogueFromList(eventName, VALineId, 1f);
			if (moveWhileAttacking)
			{
				onAttackEnd?.Invoke();
			}
		}

		public void LaunchAttack()
		{
			if (attacks == null)
			{
				attacks = new List<AnimatedBossAttack>();
			}
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			}
			if (attacks.Count > 0)
			{
				for (int num = attacks.Count - 1; num >= 0; num--)
				{
					attacks[num].Animancer.Stop();
					attacks[num].Animancer.Animator.Rebind();
					attacks[num].Animancer.Animator.Update(0f);
					_pooler.Return(attacks[num]);
					attacks.RemoveAt(num);
				}
			}
			_FMODInstances = new List<EventInstance>();
			_audioRoutines = new List<Coroutine>();
			for (int i = 0; i < numberOfBeams; i++)
			{
				AnimatedBossAttack orCreate = _pooler.GetOrCreate(base.transform, activate: true);
				orCreate.transform.position = new Vector3(base.transform.position.x, base.transform.position.y, orCreate.transform.position.z);
				attacks.Add(orCreate);
				EventInstance eventInstance = orCreate.GetComponentInChildren<StudioEventEmitter>().EventInstance;
				_FMODInstances.Add(eventInstance);
			}
			float num2 = 0f;
			num2 = totalAngle / (float)attacks.Count;
			Vector2 to = ((!followPlayer) ? direction.normalized : ((Vector2)(_lastPlayerPosition - base.transform.position)));
			to.Normalize();
			float num3 = Vector2.SignedAngle(Vector2.right, to);
			for (int j = 0; j < attacks.Count; j++)
			{
				attacks[j].transform.localEulerAngles = new Vector3(-45f, 0f, num3 + (float)(j + 1) * num2);
				attacks[j].RunInAnimation(delegate
				{
					RotateBeam(totalAngle);
				});
			}
			StopFetchingPlayerPosition();
		}

		public void RotateBeam(float angle)
		{
			if (_rotationTweens == null)
			{
				_rotationTweens = new List<Tween>();
			}
			float duration = angle / rotationSpeed;
			for (int i = 0; i < attacks.Count; i++)
			{
				attacks[i].RunLoopAnimation();
				TweenerCore<Quaternion, Vector3, QuaternionOptions> item = attacks[i].transform.DOLocalRotate(new Vector3(0f, 0f, angle * (float)angleRaiseDirection), duration, RotateMode.LocalAxisAdd);
				_rotationTweens.Add(item);
				List<Tween> rotationTweens = _rotationTweens;
				rotationTweens[rotationTweens.Count - 1].SetAutoKill(autoKillOnCompletion: false);
				List<Tween> rotationTweens2 = _rotationTweens;
				rotationTweens2[rotationTweens2.Count - 1].SetEase(rotationCurve);
				List<Tween> rotationTweens3 = _rotationTweens;
				rotationTweens3[rotationTweens3.Count - 1].onComplete = EndAttack;
				List<Tween> rotationTweens4 = _rotationTweens;
				rotationTweens4[rotationTweens4.Count - 1].Restart();
				Coroutine audioRoutine = StartCoroutine(AudioParameterRoutine(i, angle * (float)angleRaiseDirection));
				List<Tween> rotationTweens5 = _rotationTweens;
				Tween tween = rotationTweens5[rotationTweens5.Count - 1];
				tween.onComplete = (TweenCallback)Delegate.Combine(tween.onComplete, (TweenCallback)delegate
				{
					StopCoroutine(audioRoutine);
				});
				_audioRoutines.Add(audioRoutine);
			}
		}

		private IEnumerator AudioParameterRoutine(int i, float startingRot)
		{
			float z = attacks[i].transform.rotation.eulerAngles.z;
			float previousRot = z;
			while (true)
			{
				if (attacks != null && attacks[i] != null)
				{
					z = attacks[i].transform.rotation.eulerAngles.z;
					float value = ((!(z < previousRot) || !(z < 5f)) ? math.abs(z - previousRot) : math.abs(z + 360f - previousRot));
					value = Mathf.Clamp(value, 0f, _soundVelocityPeak) / _soundVelocityPeak;
					previousRot = z;
					_FMODInstances[i].setParameterByName("Velocity", value);
				}
				yield return new WaitForFixedUpdate();
			}
		}

		public override void Stop()
		{
			if (attacks == null || attacks.Count == 0)
			{
				return;
			}
			if (_rotationTweens != null)
			{
				for (int num = _rotationTweens.Count - 1; num >= 0; num--)
				{
					_rotationTweens[num].Kill();
					_rotationTweens.RemoveAt(num);
					StopCoroutine(_audioRoutines[num]);
					_audioRoutines.RemoveAt(num);
				}
				_audioRoutines = null;
				_rotationTweens = null;
			}
			if (attacks.Count > 0)
			{
				Debug.Log("RETURN " + attacks.Count + " attacks to pool!");
				for (int num2 = attacks.Count - 1; num2 >= 0; num2--)
				{
					attacks[num2].Animancer.Stop();
					attacks[num2].Animancer.Animator.Rebind();
					attacks[num2].Animancer.Animator.Update(0f);
					_pooler.Return(attacks[num2]);
					attacks.RemoveAt(num2);
				}
			}
			_beamsEndedCounter = 0;
			attacks = null;
		}

		public void EndAttack()
		{
			int num = 0;
			for (int i = 0; i < _rotationTweens.Count; i++)
			{
				if (_rotationTweens[i].IsComplete())
				{
					num++;
				}
			}
			if (num >= attacks.Count)
			{
				for (int num2 = _rotationTweens.Count - 1; num2 >= 0; num2--)
				{
					_rotationTweens[num2].Kill();
					_rotationTweens.RemoveAt(num2);
				}
				_rotationTweens = null;
				for (int j = 0; j < attacks.Count; j++)
				{
					attacks[j].RunOutAnimation(CheckAllBeamsEnded);
				}
			}
		}

		private void CheckAllBeamsEnded()
		{
			_beamsEndedCounter++;
			if (_beamsEndedCounter < attacks.Count)
			{
				return;
			}
			if (attacks.Count > 0)
			{
				Debug.Log("RETURN " + attacks.Count + " attacks to pool!");
				for (int num = attacks.Count - 1; num >= 0; num--)
				{
					attacks[num].Animancer.Stop();
					attacks[num].Animancer.Animator.Rebind();
					attacks[num].Animancer.Animator.Update(0f);
					_pooler.Return(attacks[num]);
					attacks.RemoveAt(num);
				}
			}
			_beamsEndedCounter = 0;
			attacks = null;
			onAttackEnd?.Invoke();
		}

		public override void Dispose()
		{
			if (attacks != null && attacks.Count != 0)
			{
				Stop();
				base.gameObject.SetActive(value: false);
			}
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEngine;

namespace Berangaria.Avatar.Runtime
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class AvatarRig : MonoBehaviour
    {
        private static readonly ExpressionKey[] MouthKeys =
        {
            ExpressionKey.Aa,
            ExpressionKey.Ih,
            ExpressionKey.Ou,
            ExpressionKey.Ee,
            ExpressionKey.Oh,
        };

        [Header("Replaceable avatar")]
        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private Transform gazeTarget;

        [Header("Motion")]
        [SerializeField, Range(0f, 1f)] private float naturalPoseWeight = 0.82f;
        [SerializeField, Range(0f, 1f)] private float idleMotionWeight = 0.32f;
        [SerializeField, Range(0f, 1f)] private float gazeMotionWeight = 0.55f;

        [Header("Face")]
        [SerializeField] private Vector2 blinkIntervalSeconds = new Vector2(2.8f, 5.8f);
        [SerializeField, Range(0.05f, 0.3f)] private float blinkDurationSeconds = 0.15f;
        [SerializeField, Range(0f, 1f)] private float lipSyncStrength = 0.72f;

        private readonly Dictionary<string, int> _muscleIndices =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly float[] _mouthWeights = new float[MouthKeys.Length];

        private Vrm10Instance _vrm;
        private Animator _animator;
        private HumanPoseHandler _poseHandler;
        private HumanPose _pose;
        private float[] _baseMuscles;
        private float[] _workingMuscles;

        private AvatarState _state;
        private float _stateBlend;
        private float _nextBlinkAt;
        private float _blinkStartedAt = -1f;
        private float _speechLevel;
        private float _smoothedSpeechLevel;
        private float _lastExternalSpeechAt = float.NegativeInfinity;
        private Vector2 _gazeOffset;
        private Vector2 _gazeOffsetTarget;
        private float _nextGazeShiftAt;
        private Vector3 _gazeVelocity;
        private bool _ready;

        public AvatarState State => _state;
        public bool IsReady => _ready;
        public float SpeechLevel => _smoothedSpeechLevel;
        public GameObject AvatarRoot => avatarRoot;

        public void Configure(GameObject newAvatarRoot, Transform newGazeTarget)
        {
            avatarRoot = newAvatarRoot;
            gazeTarget = newGazeTarget;
        }

        public void SetState(AvatarState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            _stateBlend = 0f;
        }

        public void SetSpeechLevel(float level)
        {
            _speechLevel = Mathf.Clamp01(level);
            _lastExternalSpeechAt = Time.unscaledTime;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            if (!_ready)
            {
                Initialize();
            }

            if (!_ready)
            {
                enabled = false;
                return;
            }

            ScheduleBlink();
            ScheduleGazeShift(immediate: true);
            Debug.Log(
                $"BERANGARIA_AVATAR_RUNTIME_READY state={_state} " +
                $"expressions={_vrm.Runtime.Expression.ExpressionKeys.Count} " +
                $"humanoid={_animator.avatar.isHuman}");
        }

        private void Initialize()
        {
            DisposePoseHandler();

            if (avatarRoot == null)
            {
                avatarRoot = gameObject;
            }

            _vrm = avatarRoot.GetComponentInChildren<Vrm10Instance>(true);
            _animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (_vrm == null || _animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
            {
                Debug.LogError("AvatarRig requires a VRM 1.0 avatar with a valid humanoid Animator.", this);
                _ready = false;
                return;
            }

            _poseHandler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            _pose = new HumanPose();
            _poseHandler.GetHumanPose(ref _pose);
            _baseMuscles = (float[])_pose.muscles.Clone();
            _workingMuscles = new float[_baseMuscles.Length];

            _muscleIndices.Clear();
            for (var index = 0; index < HumanTrait.MuscleName.Length; index++)
            {
                _muscleIndices[HumanTrait.MuscleName[index]] = index;
            }

            if (gazeTarget != null)
            {
                _vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
                _vrm.LookAtTarget = gazeTarget;
            }

            _ready = true;
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            _stateBlend = Damp(_stateBlend, 1f, 5.5f, Time.deltaTime);
            ApplyBodyPose();
            UpdateGaze();
            UpdateFace();
        }

        private void ApplyBodyPose()
        {
            Array.Copy(_baseMuscles, _workingMuscles, _baseMuscles.Length);

            var time = Time.time;
            var breath = Mathf.Sin(time * 1.55f) * 0.5f + 0.5f;
            var slowSway = Mathf.Sin(time * 0.43f);
            var secondarySway = Mathf.Sin(time * 0.71f + 1.2f);
            var activity = _state == AvatarState.Speaking ? 1f : _state == AvatarState.Listening ? 0.58f : 0.34f;
            var motion = idleMotionWeight * activity;

            // A relaxed neutral stance. Values are humanoid muscles, so this survives model replacement.
            SetMuscle("Left Arm Down-Up", -0.78f * naturalPoseWeight);
            SetMuscle("Right Arm Down-Up", -0.78f * naturalPoseWeight);
            AddMuscle("Left Arm Front-Back", -0.035f * naturalPoseWeight);
            AddMuscle("Right Arm Front-Back", -0.035f * naturalPoseWeight);
            AddMuscle("Left Forearm Stretch", 0.075f * naturalPoseWeight);
            AddMuscle("Right Forearm Stretch", 0.075f * naturalPoseWeight);
            AddMuscle("Left Shoulder Down-Up", -0.045f * naturalPoseWeight);
            AddMuscle("Right Shoulder Down-Up", -0.045f * naturalPoseWeight);

            AddMuscle("Chest Front-Back", (breath - 0.5f) * 0.018f * idleMotionWeight);
            AddMuscle("Chest Left-Right", slowSway * 0.016f * motion);
            AddMuscle("Chest Twist Left-Right", secondarySway * 0.012f * motion);
            AddMuscle("Spine Left-Right", -slowSway * 0.010f * motion);
            AddMuscle("Head Turn Left-Right", slowSway * 0.018f * motion);
            AddMuscle("Head Tilt Left-Right", secondarySway * 0.010f * motion);

            switch (_state)
            {
                case AvatarState.Listening:
                    AddMuscle("Head Tilt Left-Right", Mathf.Lerp(0f, 0.045f, _stateBlend));
                    AddMuscle("Head Nod Down-Up", Mathf.Lerp(0f, -0.018f, _stateBlend));
                    break;
                case AvatarState.Thinking:
                    AddMuscle("Head Turn Left-Right", Mathf.Lerp(0f, 0.055f, _stateBlend));
                    AddMuscle("Head Nod Down-Up", Mathf.Lerp(0f, 0.025f, _stateBlend));
                    AddMuscle("Chest Twist Left-Right", Mathf.Lerp(0f, -0.018f, _stateBlend));
                    break;
                case AvatarState.Speaking:
                    AddMuscle("Head Nod Down-Up", Mathf.Sin(time * 2.1f) * 0.015f * _stateBlend);
                    AddMuscle("Chest Twist Left-Right", Mathf.Sin(time * 0.95f) * 0.022f * _stateBlend);
                    break;
            }

            _pose.muscles = _workingMuscles;
            _poseHandler.SetHumanPose(ref _pose);
        }

        private void UpdateGaze()
        {
            if (gazeTarget == null || Camera.main == null)
            {
                return;
            }

            if (Time.time >= _nextGazeShiftAt)
            {
                ScheduleGazeShift(immediate: false);
            }

            _gazeOffset = Vector2.Lerp(_gazeOffset, _gazeOffsetTarget, 1f - Mathf.Exp(-Time.deltaTime * 3.2f));
            var cameraTransform = Camera.main.transform;
            var desired = cameraTransform.position
                + cameraTransform.right * _gazeOffset.x * gazeMotionWeight
                + cameraTransform.up * _gazeOffset.y * gazeMotionWeight;
            gazeTarget.position = Vector3.SmoothDamp(
                gazeTarget.position,
                desired,
                ref _gazeVelocity,
                0.16f,
                Mathf.Infinity,
                Time.deltaTime);
        }

        private void UpdateFace()
        {
            var expression = _vrm.Runtime.Expression;
            expression.SetWeight(ExpressionKey.Blink, CalculateBlinkWeight());

            var hasExternalSpeech = Time.unscaledTime - _lastExternalSpeechAt < 0.25f;
            var targetSpeech = hasExternalSpeech
                ? _speechLevel
                : _state == AvatarState.Speaking
                    ? SyntheticSpeechLevel(Time.time)
                    : 0f;
            _smoothedSpeechLevel = Damp(_smoothedSpeechLevel, targetSpeech, 18f, Time.deltaTime);
            ApplyMouth(expression, _smoothedSpeechLevel * lipSyncStrength);

            // Keep the model author's neutral face intact. State emotion mapping is added later.
            expression.SetWeight(ExpressionKey.Surprised, 0f);
        }

        private void ApplyMouth(Vrm10RuntimeExpression expression, float level)
        {
            var activeViseme = Mathf.FloorToInt(Time.time * 6.2f) % MouthKeys.Length;
            for (var index = 0; index < MouthKeys.Length; index++)
            {
                var target = index == activeViseme ? level : 0f;
                _mouthWeights[index] = Damp(_mouthWeights[index], target, 24f, Time.deltaTime);
                expression.SetWeight(MouthKeys[index], _mouthWeights[index]);
            }
        }

        private float CalculateBlinkWeight()
        {
            if (_blinkStartedAt < 0f)
            {
                if (Time.time < _nextBlinkAt)
                {
                    return 0f;
                }

                _blinkStartedAt = Time.time;
            }

            var phase = (Time.time - _blinkStartedAt) / blinkDurationSeconds;
            if (phase >= 1f)
            {
                _blinkStartedAt = -1f;
                ScheduleBlink();
                return 0f;
            }

            return phase < 0.42f
                ? Mathf.SmoothStep(0f, 1f, phase / 0.42f)
                : Mathf.SmoothStep(1f, 0f, (phase - 0.42f) / 0.58f);
        }

        private void ScheduleBlink()
        {
            var minimum = Mathf.Min(blinkIntervalSeconds.x, blinkIntervalSeconds.y);
            var maximum = Mathf.Max(blinkIntervalSeconds.x, blinkIntervalSeconds.y);
            _nextBlinkAt = Time.time + UnityEngine.Random.Range(minimum, maximum);
        }

        private void ScheduleGazeShift(bool immediate)
        {
            var scale = _state == AvatarState.Thinking ? 1.35f : _state == AvatarState.Listening ? 0.55f : 0.85f;
            _gazeOffsetTarget = new Vector2(
                UnityEngine.Random.Range(-0.075f, 0.075f),
                UnityEngine.Random.Range(-0.045f, 0.045f)) * scale;
            if (immediate)
            {
                _gazeOffset = _gazeOffsetTarget;
            }

            _nextGazeShiftAt = Time.time + UnityEngine.Random.Range(1.4f, 3.4f);
        }

        private void SetMuscle(string name, float value)
        {
            if (_muscleIndices.TryGetValue(name, out var index))
            {
                _workingMuscles[index] = Mathf.Clamp(value, -1f, 1f);
            }
        }

        private void AddMuscle(string name, float delta)
        {
            if (_muscleIndices.TryGetValue(name, out var index))
            {
                _workingMuscles[index] = Mathf.Clamp(_workingMuscles[index] + delta, -1f, 1f);
            }
        }

        private static float SyntheticSpeechLevel(float time)
        {
            var syllable = Mathf.Abs(Mathf.Sin(time * 8.3f));
            var consonant = Mathf.Abs(Mathf.Sin(time * 13.7f + 0.8f));
            var phraseGate = Mathf.SmoothStep(0f, 1f, Mathf.Sin(time * 2.15f) * 0.5f + 0.5f);
            return Mathf.Clamp01((0.18f + syllable * 0.58f + consonant * 0.16f) * phraseGate);
        }

        private static float Damp(float current, float target, float speed, float deltaTime)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * deltaTime));
        }

        private void OnDestroy()
        {
            DisposePoseHandler();
        }

        private void DisposePoseHandler()
        {
            _poseHandler?.Dispose();
            _poseHandler = null;
            _ready = false;
        }
    }
}

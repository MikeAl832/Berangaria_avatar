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
        private struct GesturePose
        {
            public float LeftArmLift;
            public float RightArmLift;
            public float LeftArmForward;
            public float RightArmForward;
            public float LeftForearmBend;
            public float RightForearmBend;
            public float LeftHandLift;
            public float RightHandLift;
            public float LeftShoulder;
            public float RightShoulder;
            public float ChestSide;
            public float ChestTwist;
            public float ChestFrontBack;
        }

        private static readonly ExpressionKey[] MouthKeys =
        {
            ExpressionKey.Aa,
            ExpressionKey.Ih,
            ExpressionKey.Ou,
            ExpressionKey.Ee,
            ExpressionKey.Oh,
        };

        public static IReadOnlyList<string> RequiredMuscleNames { get; } = new[]
        {
            "Left Arm Down-Up",
            "Right Arm Down-Up",
            "Left Arm Front-Back",
            "Right Arm Front-Back",
            "Left Forearm Stretch",
            "Right Forearm Stretch",
            "Left Hand Down-Up",
            "Right Hand Down-Up",
            "Left Shoulder Down-Up",
            "Right Shoulder Down-Up",
            "Chest Front-Back",
            "Chest Left-Right",
            "Chest Twist Left-Right",
            "Spine Left-Right",
            "Spine Twist Left-Right",
            "Head Turn Left-Right",
            "Head Tilt Left-Right",
            "Head Nod Down-Up",
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
        private AvatarEmotion _emotion;
        private float _idleStateWeight = 1f;
        private float _listeningStateWeight;
        private float _thinkingStateWeight;
        private float _speakingStateWeight;
        private float _emotionHeadTilt;
        private float _emotionHeadNod;
        private float _emotionHeadTurn;
        private float _emotionChestFrontBack;
        private float _emotionChestTwist;
        private float _emotionShoulder;
        private float _emotionChuckle;
        private GesturePose _gestureCurrent;
        private GesturePose _gestureTarget;
        private float _gestureEnergy = 0.72f;
        private float _nextGestureAt;
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
        private float _happyWeight;
        private float _angryWeight;
        private float _sadWeight;
        private float _relaxedWeight;
        private float _surprisedWeight;

        public AvatarState State => _state;
        public AvatarEmotion Emotion => _emotion;
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
            if (state == AvatarState.Speaking)
            {
                _nextGestureAt = Time.time + UnityEngine.Random.Range(0.12f, 0.38f);
            }
            else
            {
                _gestureTarget = default;
            }
        }

        public void SetSpeechLevel(float level)
        {
            _speechLevel = Mathf.Clamp01(level);
            _lastExternalSpeechAt = Time.unscaledTime;
        }

        public void SetEmotion(AvatarEmotion emotion)
        {
            if (_emotion == emotion)
            {
                return;
            }

            _emotion = emotion;
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
            _nextGestureAt = Time.time + UnityEngine.Random.Range(0.8f, 1.4f);
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

            UpdateStateWeights();
            UpdateEmotionPoseWeights();
            UpdateSpeakingGesture();
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
            var activity =
                _idleStateWeight * 0.34f
                + _listeningStateWeight * 0.58f
                + _thinkingStateWeight * 0.42f
                + _speakingStateWeight;
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
            AddMuscle("Spine Twist Left-Right", -secondarySway * 0.007f * motion);
            AddMuscle("Left Shoulder Down-Up", (breath - 0.5f) * 0.010f * motion);
            AddMuscle("Right Shoulder Down-Up", (breath - 0.5f) * 0.010f * motion);
            AddMuscle("Left Arm Front-Back", slowSway * 0.008f * motion);
            AddMuscle("Right Arm Front-Back", -slowSway * 0.008f * motion);
            AddMuscle("Head Turn Left-Right", slowSway * 0.018f * motion);
            AddMuscle("Head Tilt Left-Right", secondarySway * 0.010f * motion);

            AddMuscle("Head Tilt Left-Right", 0.045f * _listeningStateWeight);
            AddMuscle("Head Nod Down-Up", -0.018f * _listeningStateWeight);
            AddMuscle("Head Turn Left-Right", 0.055f * _thinkingStateWeight);
            AddMuscle("Head Nod Down-Up", 0.025f * _thinkingStateWeight);
            AddMuscle("Chest Twist Left-Right", -0.018f * _thinkingStateWeight);
            AddMuscle(
                "Head Nod Down-Up",
                Mathf.Sin(time * 2.1f) * 0.015f * _speakingStateWeight);
            AddMuscle(
                "Chest Twist Left-Right",
                Mathf.Sin(time * 0.95f) * 0.040f * _speakingStateWeight);
            AddMuscle(
                "Chest Left-Right",
                Mathf.Sin(time * 0.72f + 0.8f) * 0.018f * _speakingStateWeight);
            AddMuscle(
                "Spine Twist Left-Right",
                -Mathf.Sin(time * 0.95f) * 0.022f * _speakingStateWeight);
            AddMuscle(
                "Left Shoulder Down-Up",
                Mathf.Sin(time * 1.15f) * 0.012f * _speakingStateWeight);
            AddMuscle(
                "Right Shoulder Down-Up",
                -Mathf.Sin(time * 1.15f) * 0.012f * _speakingStateWeight);

            ApplySpeakingGesture();

            ApplyEmotionPose(time);

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
                : _speakingStateWeight > 0.001f
                    ? SyntheticSpeechLevel(Time.time) * _speakingStateWeight
                    : 0f;
            _smoothedSpeechLevel = Damp(_smoothedSpeechLevel, targetSpeech, 18f, Time.deltaTime);
            ApplyMouth(expression, _smoothedSpeechLevel * lipSyncStrength);

            ApplyEmotionExpression(expression);
        }

        private void ApplyEmotionExpression(Vrm10RuntimeExpression expression)
        {
            var happy = 0f;
            var angry = 0f;
            var sad = 0f;
            var relaxed = 0f;
            var surprised = 0.035f * _listeningStateWeight;

            switch (_emotion)
            {
                case AvatarEmotion.Calm:
                    relaxed = 0.16f;
                    break;
                case AvatarEmotion.Sarcastic:
                    happy = 0.11f;
                    relaxed = 0.08f;
                    break;
                case AvatarEmotion.Disdainful:
                    angry = 0.16f;
                    break;
                case AvatarEmotion.Bored:
                    relaxed = 0.10f;
                    sad = 0.07f;
                    break;
                case AvatarEmotion.Indifferent:
                    relaxed = 0.07f;
                    break;
                case AvatarEmotion.Confident:
                    happy = 0.17f;
                    relaxed = 0.05f;
                    break;
                case AvatarEmotion.Sighing:
                    sad = 0.15f;
                    relaxed = 0.07f;
                    break;
                case AvatarEmotion.Chuckling:
                    happy = 0.34f;
                    break;
            }

            const float faceTransitionSpeed = 3.0f;
            _happyWeight = Damp(_happyWeight, happy, faceTransitionSpeed, Time.deltaTime);
            _angryWeight = Damp(_angryWeight, angry, faceTransitionSpeed, Time.deltaTime);
            _sadWeight = Damp(_sadWeight, sad, faceTransitionSpeed, Time.deltaTime);
            _relaxedWeight = Damp(_relaxedWeight, relaxed, faceTransitionSpeed, Time.deltaTime);
            _surprisedWeight = Damp(
                _surprisedWeight,
                surprised,
                faceTransitionSpeed,
                Time.deltaTime);
            expression.SetWeight(ExpressionKey.Happy, _happyWeight);
            expression.SetWeight(ExpressionKey.Angry, _angryWeight);
            expression.SetWeight(ExpressionKey.Sad, _sadWeight);
            expression.SetWeight(ExpressionKey.Relaxed, _relaxedWeight);
            expression.SetWeight(ExpressionKey.Surprised, _surprisedWeight);
        }

        private void ApplyEmotionPose(float time)
        {
            AddMuscle("Head Tilt Left-Right", _emotionHeadTilt);
            AddMuscle("Head Nod Down-Up", _emotionHeadNod);
            AddMuscle("Head Turn Left-Right", _emotionHeadTurn);
            AddMuscle("Chest Front-Back", _emotionChestFrontBack);
            AddMuscle("Chest Twist Left-Right", _emotionChestTwist);
            AddMuscle("Left Shoulder Down-Up", _emotionShoulder);
            AddMuscle("Right Shoulder Down-Up", _emotionShoulder);

            var chuckle = Mathf.Abs(Mathf.Sin(time * 7.2f)) * _emotionChuckle;
            AddMuscle("Head Nod Down-Up", -0.014f * chuckle);
            AddMuscle("Left Shoulder Down-Up", 0.012f * chuckle);
            AddMuscle("Right Shoulder Down-Up", 0.012f * chuckle);
        }

        private void UpdateStateWeights()
        {
            const float stateTransitionSpeed = 3.2f;
            _idleStateWeight = Damp(
                _idleStateWeight,
                _state == AvatarState.Idle ? 1f : 0f,
                stateTransitionSpeed,
                Time.deltaTime);
            _listeningStateWeight = Damp(
                _listeningStateWeight,
                _state == AvatarState.Listening ? 1f : 0f,
                stateTransitionSpeed,
                Time.deltaTime);
            _thinkingStateWeight = Damp(
                _thinkingStateWeight,
                _state == AvatarState.Thinking ? 1f : 0f,
                stateTransitionSpeed,
                Time.deltaTime);
            _speakingStateWeight = Damp(
                _speakingStateWeight,
                _state == AvatarState.Speaking ? 1f : 0f,
                stateTransitionSpeed,
                Time.deltaTime);
        }

        private void UpdateEmotionPoseWeights()
        {
            var headTilt = 0f;
            var headNod = 0f;
            var headTurn = 0f;
            var chestFrontBack = 0f;
            var chestTwist = 0f;
            var shoulder = 0f;
            var chuckle = 0f;

            switch (_emotion)
            {
                case AvatarEmotion.Sarcastic:
                    headTilt = 0.032f;
                    chestTwist = -0.012f;
                    break;
                case AvatarEmotion.Disdainful:
                    headNod = 0.018f;
                    headTurn = 0.018f;
                    break;
                case AvatarEmotion.Bored:
                    headNod = -0.028f;
                    chestFrontBack = -0.018f;
                    break;
                case AvatarEmotion.Indifferent:
                    headTilt = -0.012f;
                    break;
                case AvatarEmotion.Confident:
                    chestFrontBack = 0.026f;
                    headNod = 0.012f;
                    shoulder = -0.016f;
                    break;
                case AvatarEmotion.Sighing:
                    chestFrontBack = -0.026f;
                    headNod = -0.022f;
                    break;
                case AvatarEmotion.Chuckling:
                    chuckle = 1f;
                    break;
            }

            const float poseTransitionSpeed = 2.8f;
            _emotionHeadTilt = Damp(
                _emotionHeadTilt,
                headTilt,
                poseTransitionSpeed,
                Time.deltaTime);
            _emotionHeadNod = Damp(
                _emotionHeadNod,
                headNod,
                poseTransitionSpeed,
                Time.deltaTime);
            _emotionHeadTurn = Damp(
                _emotionHeadTurn,
                headTurn,
                poseTransitionSpeed,
                Time.deltaTime);
            _emotionChestFrontBack = Damp(
                _emotionChestFrontBack,
                chestFrontBack,
                poseTransitionSpeed,
                Time.deltaTime);
            _emotionChestTwist = Damp(
                _emotionChestTwist,
                chestTwist,
                poseTransitionSpeed,
                Time.deltaTime);
            _emotionShoulder = Damp(
                _emotionShoulder,
                shoulder,
                poseTransitionSpeed,
                Time.deltaTime);
            _emotionChuckle = Damp(
                _emotionChuckle,
                chuckle,
                poseTransitionSpeed,
                Time.deltaTime);
        }

        private void UpdateSpeakingGesture()
        {
            _gestureEnergy = Damp(
                _gestureEnergy,
                EmotionGestureEnergy(_emotion),
                2.4f,
                Time.deltaTime);

            if (_state == AvatarState.Speaking && Time.time >= _nextGestureAt)
            {
                ScheduleSpeakingGesture();
            }
            else if (_state != AvatarState.Speaking)
            {
                _gestureTarget = default;
            }

            const float gestureTransitionSpeed = 1.85f;
            _gestureCurrent.LeftArmLift = Damp(
                _gestureCurrent.LeftArmLift,
                _gestureTarget.LeftArmLift,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.RightArmLift = Damp(
                _gestureCurrent.RightArmLift,
                _gestureTarget.RightArmLift,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.LeftArmForward = Damp(
                _gestureCurrent.LeftArmForward,
                _gestureTarget.LeftArmForward,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.RightArmForward = Damp(
                _gestureCurrent.RightArmForward,
                _gestureTarget.RightArmForward,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.LeftForearmBend = Damp(
                _gestureCurrent.LeftForearmBend,
                _gestureTarget.LeftForearmBend,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.RightForearmBend = Damp(
                _gestureCurrent.RightForearmBend,
                _gestureTarget.RightForearmBend,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.LeftHandLift = Damp(
                _gestureCurrent.LeftHandLift,
                _gestureTarget.LeftHandLift,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.RightHandLift = Damp(
                _gestureCurrent.RightHandLift,
                _gestureTarget.RightHandLift,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.LeftShoulder = Damp(
                _gestureCurrent.LeftShoulder,
                _gestureTarget.LeftShoulder,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.RightShoulder = Damp(
                _gestureCurrent.RightShoulder,
                _gestureTarget.RightShoulder,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.ChestSide = Damp(
                _gestureCurrent.ChestSide,
                _gestureTarget.ChestSide,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.ChestTwist = Damp(
                _gestureCurrent.ChestTwist,
                _gestureTarget.ChestTwist,
                gestureTransitionSpeed,
                Time.deltaTime);
            _gestureCurrent.ChestFrontBack = Damp(
                _gestureCurrent.ChestFrontBack,
                _gestureTarget.ChestFrontBack,
                gestureTransitionSpeed,
                Time.deltaTime);
        }

        private void ScheduleSpeakingGesture()
        {
            var pose = default(GesturePose);
            switch (UnityEngine.Random.Range(0, 5))
            {
                case 0:
                    pose.LeftArmLift = 0.140f;
                    pose.RightArmLift = 0.125f;
                    pose.LeftArmForward = -0.080f;
                    pose.RightArmForward = -0.065f;
                    pose.LeftForearmBend = -0.250f;
                    pose.RightForearmBend = -0.220f;
                    pose.LeftShoulder = 0.024f;
                    pose.RightShoulder = 0.020f;
                    pose.ChestFrontBack = 0.032f;
                    break;
                case 1:
                    pose.LeftArmLift = 0.265f;
                    pose.RightArmLift = 0.045f;
                    pose.LeftArmForward = -0.160f;
                    pose.RightArmForward = 0.020f;
                    pose.LeftForearmBend = -0.420f;
                    pose.RightForearmBend = -0.060f;
                    pose.LeftHandLift = 0.080f;
                    pose.LeftShoulder = 0.052f;
                    pose.RightShoulder = -0.010f;
                    pose.ChestSide = -0.034f;
                    pose.ChestTwist = -0.070f;
                    break;
                case 2:
                    pose.LeftArmLift = 0.045f;
                    pose.RightArmLift = 0.265f;
                    pose.LeftArmForward = 0.020f;
                    pose.RightArmForward = -0.160f;
                    pose.LeftForearmBend = -0.060f;
                    pose.RightForearmBend = -0.420f;
                    pose.RightHandLift = 0.080f;
                    pose.LeftShoulder = -0.010f;
                    pose.RightShoulder = 0.052f;
                    pose.ChestSide = 0.034f;
                    pose.ChestTwist = 0.070f;
                    break;
                case 3:
                    pose.LeftArmLift = 0.110f;
                    pose.RightArmLift = 0.200f;
                    pose.LeftArmForward = -0.050f;
                    pose.RightArmForward = -0.120f;
                    pose.LeftForearmBend = -0.180f;
                    pose.RightForearmBend = -0.340f;
                    pose.LeftHandLift = -0.035f;
                    pose.RightHandLift = 0.060f;
                    pose.LeftShoulder = 0.012f;
                    pose.RightShoulder = 0.038f;
                    pose.ChestSide = 0.024f;
                    pose.ChestTwist = 0.050f;
                    break;
            }

            _gestureTarget = pose;
            var delay = GestureInterval(_emotion);
            _nextGestureAt = Time.time + UnityEngine.Random.Range(delay.x, delay.y);
        }

        private void ApplySpeakingGesture()
        {
            var vocalAccent = Mathf.Clamp01(_smoothedSpeechLevel) * 0.022f;
            var weight = _speakingStateWeight * _gestureEnergy;
            AddMuscle("Left Arm Down-Up", _gestureCurrent.LeftArmLift * weight);
            AddMuscle("Right Arm Down-Up", _gestureCurrent.RightArmLift * weight);
            AddMuscle("Left Arm Front-Back", _gestureCurrent.LeftArmForward * weight);
            AddMuscle("Right Arm Front-Back", _gestureCurrent.RightArmForward * weight);
            AddMuscle("Left Forearm Stretch", _gestureCurrent.LeftForearmBend * weight);
            AddMuscle("Right Forearm Stretch", _gestureCurrent.RightForearmBend * weight);
            AddMuscle("Left Hand Down-Up", _gestureCurrent.LeftHandLift * weight);
            AddMuscle("Right Hand Down-Up", _gestureCurrent.RightHandLift * weight);
            AddMuscle("Left Shoulder Down-Up", (_gestureCurrent.LeftShoulder + vocalAccent) * weight);
            AddMuscle("Right Shoulder Down-Up", (_gestureCurrent.RightShoulder + vocalAccent) * weight);
            AddMuscle("Chest Left-Right", _gestureCurrent.ChestSide * weight);
            AddMuscle("Chest Twist Left-Right", _gestureCurrent.ChestTwist * weight);
            AddMuscle("Spine Twist Left-Right", _gestureCurrent.ChestTwist * -0.42f * weight);
            AddMuscle("Chest Front-Back", (_gestureCurrent.ChestFrontBack + vocalAccent * 0.55f) * weight);
        }

        private static float EmotionGestureEnergy(AvatarEmotion emotion)
        {
            switch (emotion)
            {
                case AvatarEmotion.Bored:
                    return 0.46f;
                case AvatarEmotion.Indifferent:
                    return 0.52f;
                case AvatarEmotion.Sighing:
                    return 0.56f;
                case AvatarEmotion.Disdainful:
                    return 0.72f;
                case AvatarEmotion.Calm:
                    return 0.74f;
                case AvatarEmotion.Sarcastic:
                    return 0.92f;
                case AvatarEmotion.Chuckling:
                    return 1.08f;
                case AvatarEmotion.Confident:
                    return 1.16f;
                default:
                    return 0.78f;
            }
        }

        private static Vector2 GestureInterval(AvatarEmotion emotion)
        {
            switch (emotion)
            {
                case AvatarEmotion.Bored:
                case AvatarEmotion.Indifferent:
                    return new Vector2(2.7f, 4.0f);
                case AvatarEmotion.Confident:
                    return new Vector2(1.25f, 2.25f);
                case AvatarEmotion.Chuckling:
                    return new Vector2(1.0f, 1.75f);
                default:
                    return new Vector2(1.55f, 2.85f);
            }
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

using UnityEngine;

namespace Berangaria.Avatar.Runtime
{
    [DisallowMultipleComponent]
    public sealed class AvatarDemoController : MonoBehaviour
    {
        [SerializeField] private AvatarRig rig;
        [SerializeField] private bool cycleStates = true;
        [SerializeField, Min(2f)] private float secondsPerState = 4.5f;
        [SerializeField] private bool showOverlay = true;

        private float _cycleStartedAt;
        private AvatarState _manualState = AvatarState.Idle;
        private bool _externalControl;
        private bool _externalConnected;

        public bool CycleStates => cycleStates;

        public void Configure(AvatarRig avatarRig)
        {
            rig = avatarRig;
        }

        public void SetExternalControl(bool enabled, bool connected)
        {
            _externalControl = enabled;
            _externalConnected = connected;
            if (enabled)
            {
                cycleStates = false;
            }
        }

        private void Start()
        {
            if (rig == null)
            {
                rig = GetComponent<AvatarRig>();
            }

            _cycleStartedAt = Time.time;
            ApplyState(AvatarState.Idle);
        }

        private void Update()
        {
            HandleKeyboard();

            if (_externalControl || !cycleStates || rig == null)
            {
                return;
            }

            var stateCount = 4;
            var index = Mathf.FloorToInt((Time.time - _cycleStartedAt) / secondsPerState) % stateCount;
            ApplyState((AvatarState)index);
        }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.F1)) showOverlay = !showOverlay;
            if (_externalControl)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                cycleStates = !cycleStates;
                _cycleStartedAt = Time.time;
                if (!cycleStates)
                {
                    ApplyState(_manualState);
                }
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) SetManualState(AvatarState.Idle);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetManualState(AvatarState.Listening);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetManualState(AvatarState.Thinking);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetManualState(AvatarState.Speaking);
        }

        private void SetManualState(AvatarState state)
        {
            cycleStates = false;
            _manualState = state;
            ApplyState(state);
        }

        private void ApplyState(AvatarState state)
        {
            if (rig != null && rig.State != state)
            {
                rig.SetState(state);
            }
        }

        private void OnGUI()
        {
            if (!showOverlay || rig == null)
            {
                return;
            }

            const float width = 350f;
            const float height = 126f;
            var rect = new Rect(18f, 18f, width, height);
            GUI.Box(rect, GUIContent.none);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.88f, 0.91f, 0.97f) },
            };

            GUI.Label(new Rect(32f, 28f, width - 28f, 25f), "Berangaria Avatar Runtime v0.1", titleStyle);
            GUI.Label(
                new Rect(32f, 56f, width - 28f, 22f),
                $"State: {rig.State}   Speech: {rig.SpeechLevel:0.00}   Control: {ControlLabel()}",
                labelStyle);
            GUI.Label(new Rect(32f, 80f, width - 28f, 20f), "1 Idle   2 Listening   3 Thinking   4 Speaking", labelStyle);
            GUI.Label(new Rect(32f, 101f, width - 28f, 20f), "Space: demo cycle   F1: hide panel", labelStyle);
        }

        private string ControlLabel()
        {
            if (!_externalControl)
            {
                return cycleStates ? "DEMO" : "MANUAL";
            }

            return _externalConnected ? "PYTHON ONLINE" : "PYTHON OFFLINE";
        }
    }
}

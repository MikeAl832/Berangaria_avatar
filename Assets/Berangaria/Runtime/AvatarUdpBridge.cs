using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Berangaria.Avatar.Runtime
{
    [DisallowMultipleComponent]
    public sealed class AvatarUdpBridge : MonoBehaviour
    {
        [Serializable]
        private sealed class BridgeMessage
        {
            public int v;
            public long seq;
            public string state;
            public float speech;
            public string emotion;
        }

        [SerializeField] private AvatarRig rig;
        [SerializeField] private AvatarDemoController demoController;
        [SerializeField, Range(1024, 65535)] private int port = 17891;
        [SerializeField, Min(1f)] private float connectionTimeoutSeconds = 5f;

        private readonly object _messageLock = new object();
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private string _latestJson;
        private bool _hasMessage;
        private volatile bool _running;
        private bool _connected;
        private bool _hasLoggedState;
        private bool _speechObserved;
        private float _lastMessageAt = float.NegativeInfinity;
        private long _lastSequence;
        private AvatarState _lastLoggedState;
        private AvatarEmotion _lastLoggedEmotion;
        private bool _hasLoggedEmotion;

        public bool IsConnected => _connected;
        public int Port => port;

        public void Configure(AvatarRig avatarRig, AvatarDemoController controller)
        {
            rig = avatarRig;
            demoController = controller;
        }

        private void Start()
        {
            if (rig == null)
            {
                rig = GetComponent<AvatarRig>();
            }

            if (demoController == null)
            {
                demoController = GetComponent<AvatarDemoController>();
            }

            try
            {
                _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                _udpClient.Client.ReceiveTimeout = 500;
                _running = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "Berangaria Avatar UDP",
                };
                _receiveThread.Start();
                Debug.Log($"BERANGARIA_AVATAR_BRIDGE_READY endpoint=127.0.0.1:{port}");
            }
            catch (SocketException exception)
            {
                Debug.LogError($"Avatar UDP bridge could not bind 127.0.0.1:{port}: {exception.Message}");
                enabled = false;
            }
        }

        private void Update()
        {
            string json = null;
            lock (_messageLock)
            {
                if (_hasMessage)
                {
                    json = _latestJson;
                    _hasMessage = false;
                }
            }

            if (json != null)
            {
                ApplyMessage(json);
            }

            if (_connected && Time.unscaledTime - _lastMessageAt > connectionTimeoutSeconds)
            {
                _connected = false;
                rig.SetState(AvatarState.Idle);
                rig.SetSpeechLevel(0f);
                rig.SetEmotion(AvatarEmotion.Neutral);
                demoController?.SetExternalControl(enabled: true, connected: false);
                Debug.LogWarning("BERANGARIA_AVATAR_BRIDGE_TIMEOUT");
            }
        }

        private void ApplyMessage(string json)
        {
            BridgeMessage message;
            try
            {
                message = JsonUtility.FromJson<BridgeMessage>(json);
            }
            catch (ArgumentException)
            {
                return;
            }

            if (message == null || message.v != 1 || message.seq <= _lastSequence)
            {
                return;
            }

            if (!Enum.TryParse(message.state, ignoreCase: true, out AvatarState state))
            {
                return;
            }

            if (float.IsNaN(message.speech) || float.IsInfinity(message.speech))
            {
                return;
            }

            _lastSequence = message.seq;
            _lastMessageAt = Time.unscaledTime;
            rig.SetState(state);
            rig.SetSpeechLevel(Mathf.Clamp01(message.speech));
            if (!string.IsNullOrWhiteSpace(message.emotion)
                && Enum.TryParse(message.emotion, ignoreCase: true, out AvatarEmotion emotion))
            {
                rig.SetEmotion(emotion);
                if (!_hasLoggedEmotion || emotion != _lastLoggedEmotion)
                {
                    _hasLoggedEmotion = true;
                    _lastLoggedEmotion = emotion;
                    Debug.Log($"BERANGARIA_AVATAR_BRIDGE_EMOTION emotion={emotion}");
                }
            }
            demoController?.SetExternalControl(enabled: true, connected: true);

            if (!_hasLoggedState || state != _lastLoggedState)
            {
                _hasLoggedState = true;
                _lastLoggedState = state;
                Debug.Log($"BERANGARIA_AVATAR_BRIDGE_STATE state={state}");
            }

            if (state != AvatarState.Speaking)
            {
                _speechObserved = false;
            }
            else if (!_speechObserved && message.speech > 0.01f)
            {
                _speechObserved = true;
                Debug.Log($"BERANGARIA_AVATAR_BRIDGE_SPEECH level={message.speech:0.000}");
            }

            if (!_connected)
            {
                _connected = true;
                Debug.Log("BERANGARIA_AVATAR_BRIDGE_CONNECTED");
            }
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Loopback, 0);
            while (_running)
            {
                try
                {
                    var payload = _udpClient.Receive(ref remote);
                    if (!IPAddress.IsLoopback(remote.Address) || payload.Length == 0 || payload.Length > 512)
                    {
                        continue;
                    }

                    var json = Encoding.UTF8.GetString(payload);
                    lock (_messageLock)
                    {
                        _latestJson = json;
                        _hasMessage = true;
                    }
                }
                catch (SocketException exception)
                {
                    if (!_running)
                    {
                        return;
                    }

                    if (exception.SocketErrorCode != SocketError.TimedOut)
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _udpClient?.Close();
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(1000);
            }

            _udpClient = null;
            _receiveThread = null;
        }
    }
}

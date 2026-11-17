using ExtractionShooter.Core.Input;
using ExtractionShooter.Gameplay.Player;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.UI.Debugging
{
    /// <summary>
    /// Development-only connection panel (IMGUI). Shows Host / Server / Client
    /// controls while the cursor is unlocked, and a health readout while playing.
    /// Replace with real UI before release.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugNetworkHud : MonoBehaviour
    {
        private const float PanelWidth = 260f;
        private const float ButtonHeight = 30f;

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private string _defaultAddress = "localhost";

        private string _address;

        private void Awake()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
            _address = _defaultAddress;
        }

        private void Update()
        {
            // Without a connection there is nothing to play: keep the cursor free so the panel is usable
            // (the lock state can survive from an earlier session when domain reload is off).
            if (_networkManager != null && CursorController.IsLocked &&
                !_networkManager.ServerManager.Started && !_networkManager.ClientManager.Started)
            {
                CursorController.SetLocked(false);
            }
        }

        private void OnGUI()
        {
            if (_networkManager == null) return;

            if (CursorController.IsLocked)
            {
                DrawHealth(new Rect(10f, 10f, PanelWidth, 24f));
                return;
            }

            // Bottom-left, and the box wraps only its content, so it stays clear of the hideout and inventory screens.
            const float areaHeight = 300f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - areaHeight - 10f, PanelWidth, areaHeight));
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUI.skin.box);
            DrawConnectionPanel();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawConnectionPanel()
        {
            bool serverStarted = _networkManager.ServerManager.Started;
            bool clientStarted = _networkManager.ClientManager.Started;

            GUILayout.Label($"Server: {(serverStarted ? "running" : "off")}   Client: {(clientStarted ? "connected" : "off")}");

            if (!serverStarted && !clientStarted)
            {
                if (GUILayout.Button("Host (server + client)", GUILayout.Height(ButtonHeight)))
                {
                    _networkManager.ServerManager.StartConnection();
                    _networkManager.ClientManager.StartConnection();
                }

                if (GUILayout.Button("Server only", GUILayout.Height(ButtonHeight)))
                    _networkManager.ServerManager.StartConnection();

                GUILayout.Space(8f);
                GUILayout.Label("Address");
                _address = GUILayout.TextField(_address);

                if (GUILayout.Button("Client", GUILayout.Height(ButtonHeight)))
                    _networkManager.ClientManager.StartConnection(_address);

                return;
            }

            if (PlayerCharacter.Local != null && GUILayout.Button("Resume", GUILayout.Height(ButtonHeight)))
                CursorController.SetLocked(true);

            if (GUILayout.Button("Disconnect", GUILayout.Height(ButtonHeight)))
            {
                if (clientStarted) _networkManager.ClientManager.StopConnection();
                if (serverStarted) _networkManager.ServerManager.StopConnection(true);
            }
        }

        private static void DrawHealth(Rect rect)
        {
            PlayerCharacter local = PlayerCharacter.Local;
            if (local == null) return;

            string text = local.Health.IsAlive
                ? $"HP {local.Health.Current} / {local.Health.Max}"
                : "Dead - returning to the hideout...";
            GUI.Label(rect, text);
        }
    }
}

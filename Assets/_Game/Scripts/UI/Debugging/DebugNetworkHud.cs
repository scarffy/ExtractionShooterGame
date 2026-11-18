using ExtractionShooter.Core.Input;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Session;
using FishNet.Managing;
using UnityEngine;

namespace ExtractionShooter.UI.Debugging
{
    /// <summary>
    /// Development-only overlay (IMGUI, editor and development builds; does nothing in release builds).
    /// Connecting, pausing and leaving belong to the main / pause menu (<see cref="Menu.GameMenu"/>); this only shows
    /// the connection state, a health readout while playing and, for the host in the hideout, a credits grant for
    /// testing. Hidden whenever a game menu is open, so it never covers one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugNetworkHud : MonoBehaviour
    {
        private const float PanelWidth = 220f;
        private const float ButtonHeight = 26f;

        [SerializeField] private NetworkManager _networkManager;

        private void Awake()
        {
            if (_networkManager == null) _networkManager = GetComponent<NetworkManager>();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (_networkManager == null || MenuState.IsOpen) return;

            bool serverStarted = _networkManager.ServerManager.Started;
            bool clientStarted = _networkManager.ClientManager.Started;
            if (!serverStarted && !clientStarted) return;

            if (CursorController.IsLocked)
            {
                DrawHealth(new Rect(10f, 10f, PanelWidth, 24f));
                return;
            }

            // Bottom-left, and the box wraps only its content, so it stays clear of the hideout and inventory screens.
            const float areaHeight = 120f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - areaHeight - 10f, PanelWidth, areaHeight));
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Dev · Server: {(serverStarted ? "on" : "off")}  Client: {(clientStarted ? "on" : "off")}");

            // Host testing: credits to buy gear without grinding raids.
            PlayerSession session = PlayerSession.Local;
            if (serverStarted && session != null && session.State == SessionState.Hideout && session.Trading != null &&
                GUILayout.Button("Dev: +10,000 cr", GUILayout.Height(ButtonHeight)))
                session.Trading.ServerDevGrant(10000);

            GUILayout.EndVertical();
            GUILayout.EndArea();
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
#endif
    }
}

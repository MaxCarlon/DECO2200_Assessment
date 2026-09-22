using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Levelstep
{
    /// <summary>
    /// Fair-day "next player" reset. Pressing P on the laptop keyboard reloads the active scene so the
    /// game behaves like a fresh load-in: the player returns to the load-in spot, the difficulty menu
    /// reopens, RouteTimer resets, stamina/meter/HUD are rebuilt. As a focus-independent fallback, holding
    /// both hand Menu buttons for 2 seconds does the same thing. The balance-board sensor survives the
    /// reload via BalanceBoardBridge's DontDestroyOnLoad (its serial connection + calibration are
    /// untouched), so the board stays live without reconnecting or recalibrating between players.
    /// </summary>
    public class RouteSessionReset : MonoBehaviour
    {
        const float MenuHoldSeconds = 2f;
        const float PollInterval = 0.05f;
        float m_MenusHeld;
        float m_NextButtonPoll = -1f;
        bool m_LeftHeld;
        bool m_RightHeld;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
                Reload("P pressed");

            TickControllerHold();
        }

        void TickControllerHold()
        {
            if (Time.unscaledTime >= m_NextButtonPoll)
            {
                m_NextButtonPoll = Time.unscaledTime + PollInterval;
                m_LeftHeld = MenuHeld(XRNode.LeftHand);
                m_RightHeld = MenuHeld(XRNode.RightHand);
            }
            bool both = m_LeftHeld && m_RightHeld;
            m_MenusHeld = both ? m_MenusHeld + Time.unscaledDeltaTime : 0f;
            if (both && m_MenusHeld >= MenuHoldSeconds)
            {
                m_MenusHeld = 0f;
                Reload("both hand Menu buttons held " + MenuHoldSeconds + "s");
            }
        }

        static bool MenuHeld(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool held) && held;
        }

        void Reload(string cause)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            {
                Debug.LogWarning("[Reset] Active scene has no build index - reload skipped. Add the scene to Build Settings.");
                return;
            }
            PlayerPrefs.DeleteKey(ClimbStaminaController.DifficultyPlayerPrefsKey);
            Debug.Log("[Reset] " + cause + " - reloading '" + scene.name + "' (build index " + scene.buildIndex +
                      "), difficulty reset to default.");
            SceneManager.LoadScene(scene.buildIndex);
        }
    }
}
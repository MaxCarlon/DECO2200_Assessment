using UnityEngine;
using UnityEngine.InputSystem;

namespace Levelstep
{
    /// <summary>
    /// Swaps the live balance meter between the chain (default) and loop boards. Press K to apply the
    /// chain board's settings, L to apply the loop board's. The human-readable active board name is
    /// shown on the controller's debug overlay (T) and written to the console on each swap.
    /// </summary>
    public class BalanceMeterPresetSwitcher : MonoBehaviour
    {
        [Tooltip("Chain board settings, active by default (pressed with K).")]
        public BalanceMeterControllerChain chain;

        [Tooltip("Loop board settings, activated with L.")]
        public BalanceMeterControllerLoop loop;

        string m_Active;
        bool m_AppliedStart;

        void Update()
        {
            if (!m_AppliedStart)
            {
                m_AppliedStart = true;
                ApplyChain();
            }

            if (Keyboard.current == null) return;
            if (Keyboard.current.kKey.wasPressedThisFrame)
                ApplyChain();
            if (Keyboard.current.lKey.wasPressedThisFrame)
                ApplyLoop();
        }

        public string ActivePreset
        {
            get
            {
                if (string.IsNullOrEmpty(m_Active)) m_Active = "chain";
                return m_Active;
            }
        }

        public void ApplyChain()
        {
            if (chain == null) return;
            chain.Apply();
            SetActive("chain", chain.controller);
        }

        public void ApplyLoop()
        {
            if (loop == null) return;
            loop.Apply();
            SetActive("loop", loop.controller);
        }

        void SetActive(string name, BalanceMeterController controller)
        {
            m_Active = name;
            var c = controller != null ? controller : BalanceMeterController.Instance;
            if (c != null) c.ActivePreset = name;
            Debug.Log("[Balance] Active preset: " + name + " (K = chain, L = loop)");
        }
    }
}
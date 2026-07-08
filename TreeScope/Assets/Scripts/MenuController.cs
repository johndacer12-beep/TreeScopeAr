using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggles the MenuPanel open and closed when the MenuIcon button is clicked.
///
/// Inspector wiring:
///   Menu Panel  → menuPanel  (the MenuPanel GameObject under Canvas)
///   Menu Button → menuButton (the MenuIcon Button in NavPanel)
/// </summary>
public class MenuController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The MenuPanel GameObject to show/hide.")]
    [SerializeField] private GameObject menuPanel;

    [Tooltip("The Menu Button that toggles the panel.")]
    [SerializeField] private Button menuButton;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Make sure the menu starts closed
        if (menuPanel != null)
            menuPanel.SetActive(false);

        // Wire up the toggle listener
        menuButton?.onClick.AddListener(ToggleMenu);
    }

    private void OnDestroy()
    {
        menuButton?.onClick.RemoveListener(ToggleMenu);
    }

    // ── Toggle ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Flips the MenuPanel between open and closed.
    /// </summary>
    public void ToggleMenu()
    {
        if (menuPanel == null) return;

        bool isOpen = menuPanel.activeSelf;
        menuPanel.SetActive(!isOpen);

        Debug.Log($"[MenuController] MenuPanel is now {(!isOpen ? "open" : "closed")}.");
    }

    /// <summary>
    /// Closes the MenuPanel explicitly (useful for back-button or overlay tap).
    /// </summary>
    public void CloseMenu()
    {
        if (menuPanel != null)
            menuPanel.SetActive(false);
    }
}

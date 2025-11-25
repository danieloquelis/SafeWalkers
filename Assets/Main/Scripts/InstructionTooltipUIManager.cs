using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls an instruction tooltip icon that reflects the currently selected gesture.
/// The selection index comes from the same PlayerPrefs key used by GestureDropDown
/// ("GestureDropDownSelection"), where:
/// 0 = Phone, 1 = Help1, 2 = Help2, 3 = ThumbsDown.
/// </summary>
public class InstructionTooltipUIManager : MonoBehaviour
{
    [Header("Sprites (by gesture)")]
    [Tooltip("Sprite used when the selected gesture is Phone (index 0).")]
    [SerializeField] private Sprite phoneSprite;

    [Tooltip("Sprite used when the selected gesture is Help1 (index 1).")]
    [SerializeField] private Sprite help1Sprite;

    [Tooltip("Sprite used when the selected gesture is Help2 (index 2).")]
    [SerializeField] private Sprite help2Sprite;

    [Tooltip("Sprite used when the selected gesture is ThumbsDown (index 3).")]
    [SerializeField] private Sprite thumbsDownSprite;

    [Header("Icon Target")]
    [Tooltip("UI Image that will display the chosen gesture icon.")]
    [SerializeField] private Image iconImage;

    [Header("Player Prefs")]
    [Tooltip("PlayerPrefs key used by GestureDropDown to store the selected gesture index.")]
    [SerializeField] private string gestureSelectionPrefsKey = "GestureDropDownSelection";

    private void Awake()
    {
        if (iconImage == null)
        {
            iconImage = GetComponentInChildren<Image>();
        }

        UpdateIconFromPrefs();
    }

    private void OnEnable()
    {
        // Ensure icon is up-to-date whenever this tooltip is enabled.
        UpdateIconFromPrefs();
    }

    /// <summary>
    /// Public method to refresh the icon manually (e.g., after the gesture selection changes).
    /// </summary>
    public void RefreshIcon()
    {
        UpdateIconFromPrefs();
    }

    /// <summary>
    /// Public method for a close button to hide this tooltip.
    /// Hook this to the Close button's OnClick.
    /// </summary>
    public void OnCloseClicked()
    {
        gameObject.SetActive(false);
    }

    private void UpdateIconFromPrefs()
    {
        if (iconImage == null)
        {
            return;
        }

        int index = 0;
        if (!string.IsNullOrEmpty(gestureSelectionPrefsKey) && PlayerPrefs.HasKey(gestureSelectionPrefsKey))
        {
            index = PlayerPrefs.GetInt(gestureSelectionPrefsKey, 0);
        }

        Sprite chosen = GetSpriteForIndex(index);
        if (chosen != null)
        {
            iconImage.sprite = chosen;
            iconImage.enabled = true;
        }
        else
        {
            // If no sprite is set for this index, optionally hide the icon.
            iconImage.enabled = false;
        }
    }

    private Sprite GetSpriteForIndex(int index)
    {
        switch (index)
        {
            case 0:
                return phoneSprite;
            case 1:
                return help1Sprite;
            case 2:
                return help2Sprite;
            case 3:
                return thumbsDownSprite;
            default:
                return null;
        }
    }
}


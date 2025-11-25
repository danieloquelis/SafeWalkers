using System.Collections;
using System.Linq;
using Oculus.Interaction.Samples;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum GestureOption
{
    Phone = 0,
    Help1 = 1,
    Help2 = 2,
    ThumbsDown = 3
}

public class GestureDropDown : MonoBehaviour
{
    [Header("Meta Dropdown")] 
    [Tooltip("Meta/Oculus DropDownGroup that owns the gesture toggles.")]
    [SerializeField] private DropDownGroup dropDownGroup;

    [Header("Persistence")]
    [Tooltip("PlayerPrefs key used to store the selected gesture index. If it does not exist, no option is pre-selected.")]
    [SerializeField] private string playerPrefsKey = "GestureDropDownSelection";

    private ToggleGroup _toggleGroup;
    private Toggle[] _toggles;

    // Cached header defaults as configured in the editor (title, subtitle, icon).
    private string _defaultTitle;
    private string _defaultSubtitle;
    private Sprite _defaultIcon;

    private void Awake()
    {
        if (dropDownGroup == null)
        {
            dropDownGroup = GetComponent<DropDownGroup>();
        }

        CacheHeaderDefaults();
    }

    private IEnumerator Start()
    {
        // Wait one frame so PointableCanvasModule and DropDownGroup finish their own initialization.
        yield return null;

        // Clear any currently selected UI object so the dropdown does not start focused.
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        InitializeToggleRefs();

        if (_toggleGroup == null || _toggles == null || _toggles.Length == 0)
        {
            yield break;
        }

        // Allow an initial state where nothing is selected.
        _toggleGroup.allowSwitchOff = true;

        // Turn all toggles off first so there is no pre-selected option by default.
        foreach (var toggle in _toggles)
        {
            toggle.isOn = false;
        }

        // Try to restore a saved selection (if present).
        bool hasSavedSelection = false;
        int savedIndex = -1;

        if (!string.IsNullOrEmpty(playerPrefsKey) && PlayerPrefs.HasKey(playerPrefsKey))
        {
            savedIndex = PlayerPrefs.GetInt(playerPrefsKey, -1);
            if (savedIndex >= 0 && savedIndex < _toggles.Length)
            {
                hasSavedSelection = true;
            }
        }
        
        if (hasSavedSelection)
        {
            // If we have a saved gesture, pre-select it and keep the default "one selected" behavior.
            _toggleGroup.allowSwitchOff = false;
            _toggles[savedIndex].isOn = true;
        }
        else
        {
            // No saved selection -> ensure header does not show the first option.
            ResetHeaderVisuals();
        }

        // Subscribe to value changes so we can persist the user's choice.
        for (int i = 0; i < _toggles.Length; i++)
        {
            int index = i;
            Toggle toggle = _toggles[i];

            toggle.onValueChanged.AddListener(isOn =>
            {
                if (!isOn)
                {
                    return;
                }

                SaveSelection(index);

                // Once the user has explicitly chosen a gesture, enforce that one option stays selected.
                _toggleGroup.allowSwitchOff = false;
            });
        }
    }

    private void InitializeToggleRefs()
    {
        if (dropDownGroup == null)
        {
            dropDownGroup = GetComponent<DropDownGroup>();
        }

        if (dropDownGroup == null)
        {
            Debug.LogWarning("[GestureDropDown] DropDownGroup reference is missing.");
            return;
        }

        _toggleGroup = dropDownGroup.GetComponentInChildren<ToggleGroup>();
        if (_toggleGroup == null)
        {
            Debug.LogWarning("[GestureDropDown] ToggleGroup not found under DropDownGroup.");
            return;
        }

        _toggles = _toggleGroup
            .GetComponentsInChildren<Toggle>()
            .Where(t => t.group == _toggleGroup)
            .ToArray();

        if (_toggles == null || _toggles.Length == 0)
        {
            Debug.LogWarning("[GestureDropDown] No toggles found in the ToggleGroup.");
        }
    }

    private void ResetHeaderVisuals()
    {
        if (dropDownGroup == null)
        {
            return;
        }

        // Restore the header to whatever was configured in the editor
        // so it shows the default title/subtitle/icon, not the first option.
        if (dropDownGroup.Title != null && dropDownGroup.Title.gameObject.activeSelf)
        {
            dropDownGroup.Title.text = _defaultTitle;
        } 

        if (dropDownGroup.Subtitle != null && dropDownGroup.Subtitle.gameObject.activeSelf)
        {
            dropDownGroup.Subtitle.text = _defaultSubtitle;
        }

        if (dropDownGroup.Icon != null && dropDownGroup.Icon.gameObject.activeSelf)
        {
            dropDownGroup.Icon.sprite = _defaultIcon;
        }
    }

    private void CacheHeaderDefaults()
    {
        if (dropDownGroup == null)
        {
            return;
        }

        if (dropDownGroup.Title != null)
        {
            _defaultTitle = dropDownGroup.Title.text;
        }

        if (dropDownGroup.Subtitle != null)
        {
            _defaultSubtitle = dropDownGroup.Subtitle.text;
        }

        if (dropDownGroup.Icon != null)
        {
            _defaultIcon = dropDownGroup.Icon.sprite;
        }
    }

    private void SaveSelection(int index)
    {
        if (string.IsNullOrEmpty(playerPrefsKey))
        {
            return;
        }

        // Map directly from index to enum (Phone, Help1, Help2, ThumbsDown),
        // but store the index so layout order and enum stay in sync.
        PlayerPrefs.SetInt(playerPrefsKey, index);
        PlayerPrefs.Save();
    }
}

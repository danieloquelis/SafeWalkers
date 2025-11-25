using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using LocalWake.Unity;

public class SetupUIManager : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private RectTransform setupPanel;

    [Header("Controls")]
    [SerializeField] private Toggle saveToggle;
    [SerializeField] private Toggle cancelToggle;

    [Header("Wake Word")]
    [SerializeField] private WakeWordRecorder wakeWordRecorder;
    [Tooltip("Profile id passed to WakeWordRecorder.SaveProfile. Leave empty to use its default.")]
    [SerializeField] private string wakeWordProfileId = "";

    [Header("Player Pref Keys")]
    [SerializeField] private string mainEmergencyContactKey = "MainEmergencyContactNumber";
    [SerializeField] private string secondaryEmergencyContactKey = "SecondaryEmergencyContactNumber";
    [SerializeField] private string emergencyGestureKey = "EmergencyGesture";
    [Tooltip("Flag that indicates initial onboarding is complete.")]
    [SerializeField] private string onboardedKey = "sOnboarded";

    [Header("Current Values (provided from UI)")]
    private string mainEmergencyContactNumber;
    private string secondaryEmergencyContactNumber;
    private string emergencyGesture;

    [Header("Contacts")]
    [Tooltip("Dropdown used for selecting the main emergency contact.")]
    [SerializeField] private ContactDropDownController mainContactDropdown;
    [Tooltip("Panel or root object for the 'New Contact' form UI.")]
    [SerializeField] private GameObject newContactFormPanel;

    [Header("Events")]
    [Tooltip("Invoked after preferences are saved and the profile is stored successfully.")]
    [SerializeField] private UnityEvent onSaveSuccessful;

    private void Awake()
    {
        if (saveToggle != null)
        {
            saveToggle.onValueChanged.AddListener(OnSaveToggleChanged);
        }

        if (cancelToggle != null)
        {
            cancelToggle.onValueChanged.AddListener(OnCancelToggleChanged);
        }
    }

    private void Start()
    {
        UpdateCancelVisibility();
    }

    /// <summary>
    /// Handler for the save toggle. When turned on, it acts as "Save" and then resets.
    /// </summary>
    /// <param name="isOn">Current toggle state.</param>
    private void OnSaveToggleChanged(bool isOn)
    {
        if (!isOn)
            return;

        OnSave();

        // Reset toggle so it can be used again later.
        saveToggle.isOn = false;
    }

    /// <summary>
    /// Public hook for the Save action. Stores all preferences, calls WakeWordRecorder.SaveProfile,
    /// marks onboarding as complete, hides the panel and fires onSaveSuccessful.
    /// </summary>
    public void OnSave()
    {
        SavePrefs();
        SaveWakeWordProfile();
        MarkOnboarded();

        onSaveSuccessful?.Invoke();
        HidePanel();
    }

    /// <summary>
    /// Handler for the cancel toggle. When turned on, it acts as "Cancel" and then resets.
    /// </summary>
    /// <param name="isOn">Current toggle state.</param>
    private void OnCancelToggleChanged(bool isOn)
    {
        if (!isOn)
            return;

        OnCancel();

        // Reset toggle so it can be used again later.
        cancelToggle.isOn = false;
    }

    /// <summary>
    /// Public hook for the Cancel action. Only available if onboarding was already completed.
    /// </summary>
    public void OnCancel()
    {
        HidePanel();
    }

    /// <summary>
    /// Public API to hide the setup panel (e.g. from other scripts).
    /// </summary>
    public void HidePanel()
    {
        if (setupPanel != null)
        {
            setupPanel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Public API to show the setup panel (if you ever need to re-open it).
    /// </summary>
    public void ShowPanel()
    {
        if (setupPanel != null)
        {
            setupPanel.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Called from dropdowns / input fields to update the current main contact.
    /// </summary>
    public void SetMainEmergencyContact(string number)
    {
        mainEmergencyContactNumber = number;
    }

    /// <summary>
    /// Called from dropdowns / input fields to update the current secondary contact.
    /// </summary>
    public void SetSecondaryEmergencyContact(string number)
    {
        secondaryEmergencyContactNumber = number;
    }

    /// <summary>
    /// Called from UI to set the emergency gesture identifier.
    /// </summary>
    public void SetEmergencyGesture(string gestureId)
    {
        emergencyGesture = gestureId;
    }

    /// <summary>
    /// If the main contact dropdown currently has no contacts, shows the new contact form UI.
    /// Otherwise, does nothing and lets the dropdown behave normally.
    /// Hook this from UI events when you want to offer creating a new contact.
    /// </summary>
    public void ShowNewContactFormIfNoContacts()
    {
        if (HasAnyContactsInPrefs())
        {
            // Contacts already exist; keep normal dropdown behavior.
            return;
        }

        if (newContactFormPanel != null)
        {
            newContactFormPanel.SetActive(true);
        }
    }

    private bool HasAnyContactsInPrefs()
    {
        // Reuse the same key as ContactDropDownController to check if there is at least one contact.
        const string contactsPrefsKey = "EmergencyContacts";

        if (!PlayerPrefs.HasKey(contactsPrefsKey))
        {
            return false;
        }

        string stored = PlayerPrefs.GetString(contactsPrefsKey);
        return !string.IsNullOrWhiteSpace(stored);
    }

    private void SavePrefs()
    {
        if (!string.IsNullOrEmpty(mainEmergencyContactKey))
        {
            PlayerPrefs.SetString(mainEmergencyContactKey, mainEmergencyContactNumber ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(secondaryEmergencyContactKey))
        {
            PlayerPrefs.SetString(secondaryEmergencyContactKey, secondaryEmergencyContactNumber ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(emergencyGestureKey))
        {
            PlayerPrefs.SetString(emergencyGestureKey, emergencyGesture ?? string.Empty);
        }

        PlayerPrefs.Save();
    }

    private void SaveWakeWordProfile()
    {
        if (wakeWordRecorder == null)
        {
            Debug.LogWarning("[SetupUIManager] WakeWordRecorder reference is missing; skipping SaveProfile.");
            return;
        }

        if (string.IsNullOrEmpty(wakeWordProfileId))
        {
            wakeWordRecorder.SaveProfile();
        }
        else
        {
            wakeWordRecorder.SaveProfile(wakeWordProfileId);
        }
    }

    private void MarkOnboarded()
    {
        if (!string.IsNullOrEmpty(onboardedKey))
        {
            PlayerPrefs.SetInt(onboardedKey, 1);
            PlayerPrefs.Save();
        }

        UpdateCancelVisibility();
    }

    private void UpdateCancelVisibility()
    {
        if (cancelToggle == null)
            return;

        bool isOnboarded = false;
        if (!string.IsNullOrEmpty(onboardedKey) && PlayerPrefs.HasKey(onboardedKey))
        {
            isOnboarded = PlayerPrefs.GetInt(onboardedKey, 0) != 0;
        }

        cancelToggle.gameObject.SetActive(isOnboarded);
    }
}


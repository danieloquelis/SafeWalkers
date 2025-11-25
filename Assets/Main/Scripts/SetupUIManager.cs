using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using LocalWake.Unity;

public class SetupUIManager : MonoBehaviour
{
    [System.Serializable]
    public class StringEvent : UnityEvent<string> { }

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

    [Tooltip("Invoked when validation fails before saving. Receives a human-readable error message.")]
    [SerializeField] private StringEvent onValidationFailed;

    [Header("Main Scene")]
    [Tooltip("Optional reference to the MainController in the main scene. If assigned, it will be refreshed after a successful save so OnSceneReady can fire.")]
    [SerializeField] private MainController mainController;

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
        if (!ValidateRequiredFields(out string errorMessage))
        {
            onValidationFailed?.Invoke(errorMessage);
            return;
        }

        SavePrefs();
        SaveWakeWordProfile();
        MarkOnboarded();

        // Notify the main scene controller that preferences have changed so it can
        // re-check readiness and potentially fire OnSceneReady.
        if (mainController != null)
        {
            mainController.OnRefresh();
        }

        onSaveSuccessful?.Invoke();
        HidePanel();
    }

    /// <summary>
    /// Ensures that the main emergency contact and the emergency gesture have values
    /// before allowing the setup to be saved.
    /// This uses the "current form values" if present, otherwise falls back to any
    /// existing PlayerPrefs values so that previously configured data is accepted.
    /// </summary>
    private bool ValidateRequiredFields(out string errorMessage)
    {
        // MAIN CONTACT: prefer the current form value, otherwise fall back to saved prefs.
        string candidateMain = mainEmergencyContactNumber;
        if (string.IsNullOrWhiteSpace(candidateMain) &&
            !string.IsNullOrEmpty(mainEmergencyContactKey) &&
            PlayerPrefs.HasKey(mainEmergencyContactKey))
        {
            candidateMain = PlayerPrefs.GetString(mainEmergencyContactKey, string.Empty);
        }

        bool hasMain = !string.IsNullOrWhiteSpace(candidateMain);

        // GESTURE: prefer the current form value, otherwise fall back to either:
        //  - the legacy string key (EmergencyGesture), or
        //  - the gesture selection index stored by GestureDropDown ("GestureDropDownSelection").
        string candidateGesture = emergencyGesture;

        if (string.IsNullOrWhiteSpace(candidateGesture) &&
            !string.IsNullOrEmpty(emergencyGestureKey) &&
            PlayerPrefs.HasKey(emergencyGestureKey))
        {
            candidateGesture = PlayerPrefs.GetString(emergencyGestureKey, string.Empty);
        }

        bool hasGesture = !string.IsNullOrWhiteSpace(candidateGesture);

        if (!hasGesture)
        {
            const string gestureSelectionPrefsKey = "GestureDropDownSelection";
            if (PlayerPrefs.HasKey(gestureSelectionPrefsKey))
            {
                int index = PlayerPrefs.GetInt(gestureSelectionPrefsKey, -1);
                if (index >= 0 && index <= (int)HandPose.ThumbsDown)
                {
                    hasGesture = true;
                }
            }
        }

        if (hasMain && hasGesture)
        {
            errorMessage = null;
            return true;
        }

        if (!hasMain && !hasGesture)
        {
            errorMessage = "Main emergency contact and emergency gesture are required.";
        }
        else if (!hasMain)
        {
            errorMessage = "Main emergency contact is required.";
        }
        else
        {
            errorMessage = "Emergency gesture is required.";
        }

        return false;
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

        // Hide the setup panel while the new contact form is visible.
        HidePanel();
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
        // Only write values that are actually provided by the current form.
        // This avoids overwriting previously saved (and still valid) preferences with empty strings.
        if (!string.IsNullOrEmpty(mainEmergencyContactKey) &&
            !string.IsNullOrWhiteSpace(mainEmergencyContactNumber))
        {
            PlayerPrefs.SetString(mainEmergencyContactKey, mainEmergencyContactNumber);
        }

        if (!string.IsNullOrEmpty(secondaryEmergencyContactKey) &&
            !string.IsNullOrWhiteSpace(secondaryEmergencyContactNumber))
        {
            PlayerPrefs.SetString(secondaryEmergencyContactKey, secondaryEmergencyContactNumber);
        }

        if (!string.IsNullOrEmpty(emergencyGestureKey) &&
            !string.IsNullOrWhiteSpace(emergencyGesture))
        {
            PlayerPrefs.SetString(emergencyGestureKey, emergencyGesture);
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


using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public enum HandPose
{
    Phone = 0,
    Help1 = 1,
    Help2 = 2,
    ThumbsDown = 3
}

public class MainController : MonoBehaviour
{
    [Header("Setup UI")]
    [SerializeField] private RectTransform setupPanel;

    [Header("Scene Events")]
    [Tooltip("Invoked once when the scene is considered ready: main contact and emergency gesture are configured in PlayerPrefs.")]
    public UnityEvent OnSceneReady;

    [Header("Player Pref Keys")]
    [SerializeField] private string mainEmergencyContactKey = "MainEmergencyContactNumber";
    [SerializeField] private string emergencyGestureKey = "EmergencyGesture";
    [SerializeField] private string pairingIdPrefsKey = SafeWalkPairingController.DefaultPairingPrefsKey;

    [Header("Gesture Detection")]
    [Tooltip("PlayerPrefs key used by GestureDropDown to store the selected gesture index.")]
    [SerializeField] private string gestureSelectionPrefsKey = "GestureDropDownSelection";
    [Tooltip("Invoked when a detected hand pose matches the configured safe-mode gesture.")]
    [SerializeField] private UnityEvent onSafeModeEnabled;

    // Local cached state
    private bool _hasMainContact;
    private bool _hasEmergencyGesture;
    private bool _hasGestureSelection;
    private HandPose _savedHandPose;
    private bool _sceneReadyInvoked;

    private void Start()
    {
        OnRefresh();
    }

    /// <summary>
    /// Public method to re-fetch all relevant data from PlayerPrefs and refresh local state & UI.
    /// Call this after setup changes or when returning to the main scene.
    /// </summary>
    public void OnRefresh()
    {
        RefreshLocalStateFromPrefs();
        UpdateSetupVisibility();
        TryInvokeSceneReady();
    }

    private void RefreshLocalStateFromPrefs()
    {
        _hasMainContact = HasNonEmptyString(mainEmergencyContactKey);
        _hasEmergencyGesture = HasNonEmptyString(emergencyGestureKey);
        LoadGestureSelectionFromPrefs();
    }

    private void LoadGestureSelectionFromPrefs()
    {
        _hasGestureSelection = false;

        if (string.IsNullOrEmpty(gestureSelectionPrefsKey) || !PlayerPrefs.HasKey(gestureSelectionPrefsKey))
        {
            return;
        }

        int index = PlayerPrefs.GetInt(gestureSelectionPrefsKey, -1);
        if (index < 0 || index > (int)HandPose.ThumbsDown)
        {
            return;
        }

        _savedHandPose = (HandPose)index;
        _hasGestureSelection = true;
    }

    /// <summary>
    /// Shows the setup UI if the main emergency contact or the emergency gesture
    /// have not yet been configured (missing or empty PlayerPrefs values).
    /// </summary>
    private void UpdateSetupVisibility()
    {
        if (setupPanel == null)
        {
            Debug.LogWarning("[MainController] setupPanel is not assigned.");
            return;
        }

        // Show setup UI if either of these is not configured.
        // We consider the gesture "configured" when a gesture selection exists.
        bool shouldShowSetup = !_hasMainContact || !_hasGestureSelection;
        setupPanel.gameObject.SetActive(shouldShowSetup);
    }

    /// <summary>
    /// Checks whether the scene is ready (main contact and gesture selection are configured)
    /// and, if so, invokes <see cref="OnSceneReady"/> exactly once per scene lifetime.
    /// </summary>
    private void TryInvokeSceneReady()
    {
        if (_sceneReadyInvoked)
        {
            return;
        }

        // Scene is considered ready when we have a main contact AND a chosen gesture
        // (stored via GestureDropDownSelection).
        bool isSceneReady = _hasMainContact && _hasGestureSelection;
        if (!isSceneReady)
        {
            return;
        }

        _sceneReadyInvoked = true;
        Debug.Log("[MainController] Scene ready: main emergency contact and gesture selection are configured.");
        OnSceneReady?.Invoke();
    }

    private static bool HasNonEmptyString(string key)
    {
        if (string.IsNullOrEmpty(key) || !PlayerPrefs.HasKey(key))
        {
            return false;
        }

        string value = PlayerPrefs.GetString(key, string.Empty);
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Called when a hand pose is detected. If it matches the configured gesture
    /// (loaded from GestureDropDownSelection), invokes onSafeModeEnabled.
    /// </summary>
    public void OnGestureDetected(HandPose pose)
    {
        if (!_hasGestureSelection)
        {
            return;
        }

        if (pose == _savedHandPose)
        {
            onSafeModeEnabled?.Invoke();
        }
    }

    /// <summary>
    /// Public helper to trigger the same safe-mode UnityEvent from other scripts or UI.
    /// This simply invokes the OnSafeModeEnabled event.
    /// </summary>
    public void OnSafeModeEnabled()
    {
        onSafeModeEnabled?.Invoke();
    }

    /// <summary>
    /// Helper overload for UnityEvents that pass an int instead of an enum.
    /// Use this from the Inspector: the int value should match the HandPose enum index.
    /// </summary>
    /// <param name="poseIndex">Integer index corresponding to HandPose (0=Phone, 1=Help1, 2=Help2, 3=ThumbsDown).</param>
    public void OnGestureDetectedFromInt(int poseIndex)
    {
        if (poseIndex < 0 || poseIndex > (int)HandPose.ThumbsDown)
        {
            return;
        }

        OnGestureDetected((HandPose)poseIndex);
    }

    /// <summary>
    /// Public method to disable safe mode by reloading the current scene.
    /// PlayerPrefs are left untouched; only the scene is restarted.
    /// </summary>
    public void DisableSafeMode()
    {
        var current = SceneManager.GetActiveScene();
        if (current.IsValid())
        {
            SceneManager.LoadScene(current.buildIndex);
        }
    }

    [Serializable]
    private class PairingPayload
    {
        public string pairingId;
    }

    /// <summary>
    /// Persists the pairing id extracted from the QR payload so the pairing controller
    /// can pick it up on the next Awake.
    /// </summary>
    /// <param name="payload">Raw JSON payload read from the QR code.</param>
    public void StorePairingPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            Debug.LogWarning("[MainController] Received empty QR payload.");
            return;
        }

        try
        {
            var parsed = JsonUtility.FromJson<PairingPayload>(payload);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.pairingId))
            {
                Debug.LogWarning($"[MainController] QR payload did not contain a pairingId. Raw={payload}");
                return;
            }

            string sanitized = parsed.pairingId.Trim();
            PlayerPrefs.SetString(pairingIdPrefsKey, sanitized);
            PlayerPrefs.Save();
            Debug.Log($"[MainController] Stored pairing id '{sanitized}' into PlayerPrefs key '{pairingIdPrefsKey}'.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MainController] Failed to parse QR payload: {ex}");
        }
    }
}



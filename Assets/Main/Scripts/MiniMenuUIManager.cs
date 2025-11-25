using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

/// <summary>
/// Manages a small UI menu that auto-hides when it has not received interaction
/// for a given idle timeout. The menu fades out smoothly and becomes hidden.
/// Any interaction (pointer or explicit NotifyInteraction call) resets the timer.
/// </summary>
public class MiniMenuUIManager : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerDownHandler
{
    [Header("UI")]
    [Tooltip("CanvasGroup controlling the mini menu root. Used for fading and interactability.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Timing")]
    [Tooltip("Seconds of inactivity before the menu starts to fade out.")]
    [SerializeField] private float idleSeconds = 5f;
    [Tooltip("Duration of the fade-out animation in seconds.")]
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Events")]
    [Tooltip("Invoked whenever the mini menu becomes visible.")]
    [SerializeField] private UnityEvent onMenuVisible;

    [Tooltip("Invoked right before the mini menu is hidden and deactivated.")]
    [SerializeField] private UnityEvent onMenuHidden;

    private float _lastInteractionTime;
    private bool _isFading;
    private float _fadeStartTime;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponentInChildren<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        ShowInstant();
        ResetIdleTimer();
    }

    private void Update()
    {
        if (canvasGroup == null)
            return;

        // If not yet idle, just wait.
        if (!_isFading)
        {
            if (Time.unscaledTime - _lastInteractionTime >= idleSeconds)
            {
                // Start fading out
                _isFading = true;
                _fadeStartTime = Time.unscaledTime;
            }
        }
        else
        {
            // Progress fade
            float t = (Time.unscaledTime - _fadeStartTime) / Mathf.Max(0.01f, fadeDuration);
            if (t >= 1f)
            {
                HideInstant();
                _isFading = false;
            }
            else
            {
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            }
        }
    }

    /// <summary>
    /// Call this from any UI element/event that represents user interaction
    /// with the mini menu (e.g., button clicks, pointer moves, etc.).
    /// </summary>
    public void NotifyInteraction()
    {
        ResetIdleTimer();
        CancelFadeIfAny();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        NotifyInteraction();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        NotifyInteraction();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        NotifyInteraction();
    }

    private void ResetIdleTimer()
    {
        _lastInteractionTime = Time.unscaledTime;
    }

    private void CancelFadeIfAny()
    {
        if (!_isFading || canvasGroup == null)
            return;

        _isFading = false;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void ShowInstant()
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        onMenuVisible?.Invoke();
    }

    private void HideInstant()
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // Notify listeners just before deactivating the GameObject.
        onMenuHidden?.Invoke();

        // Fully hide the menu GameObject once faded out.
        gameObject.SetActive(false);
    }
}


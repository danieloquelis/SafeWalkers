using UnityEngine;
using SafeWalkers.SafePlaceCalculator;

/// <summary>
/// Bridges SafeWalk mobile location updates with the SafePlace calculator and ORS routing.
/// </summary>
public class SafeRouteFinderManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SafePlaceCalculatorService safePlaceService;
    [SerializeField] private ORS_Routing orsRouting;

    [Header("Routing Config")]
    [SerializeField] private SafetyProfile safetyProfile = SafetyProfile.CrowdedIndoor;
    [SerializeField] private bool snapPlayerToRoute = true;

    private bool _calculationInProgress;
    private bool _routeStarted;
    private Vector2 _currentStartLatLon;

    private void Awake()
    {
        if (safePlaceService == null)
        {
            safePlaceService = GetComponent<SafePlaceCalculatorService>();
        }

        if (orsRouting == null)
        {
            orsRouting = GetComponent<ORS_Routing>();
        }
    }

    /// <summary>
    /// Entry point for SafeWalk mobile location updates (wire this to SafeWalkPairingController.OnLocationUpdated).
    /// </summary>
    public void HandleLocationPayload(SafeWalkLocationPayload payload)
    {
        if (payload == null)
            return;

        if (_routeStarted)
        {
            Debug.Log("[SafeRouteFinderManager] Route already active. Ignoring location update.");
            return;
        }

        if (_calculationInProgress)
        {
            Debug.Log("[SafeRouteFinderManager] Safe place calculation already running. Ignoring location update.");
            return;
        }

        if (safePlaceService == null)
        {
            Debug.LogError("[SafeRouteFinderManager] SafePlaceCalculatorService reference missing.");
            return;
        }

        _calculationInProgress = true;
        _currentStartLatLon = new Vector2((float)payload.latitude, (float)payload.longitude);

        safePlaceService.FindSafePlace(
            payload.latitude,
            payload.longitude,
            safetyProfile,
            HandleSafePlaceResult,
            HandleSafePlaceError);
    }

    /// <summary>
    /// Allows manual trigger by directly supplying coordinates (useful for testing).
    /// </summary>
    public void StartRouteFromCoordinates(double startLat, double startLng, double endLat, double endLng)
    {
        if (_routeStarted || _calculationInProgress)
        {
            Debug.Log("[SafeRouteFinderManager] Route request already active. Ignoring manual trigger.");
            return;
        }

        _currentStartLatLon = new Vector2((float)startLat, (float)startLng);
        StartRouteInternal(_currentStartLatLon, new Vector2((float)endLat, (float)endLng));
    }

    /// <summary>
    /// Resets internal state so the manager can process a new route request.
    /// </summary>
    public void ResetRouteState()
    {
        _calculationInProgress = false;
        _routeStarted = false;
    }

    private void HandleSafePlaceResult(SafePlaceCalculatorResult result)
    {
        _calculationInProgress = false;

        if (result?.best == null)
        {
            Debug.LogWarning("[SafeRouteFinderManager] Safe place result missing 'best' candidate.");
            return;
        }

        Vector2 destination = new Vector2((float)result.best.lat, (float)result.best.lng);
        bool started = StartRouteInternal(_currentStartLatLon, destination);

        if (started)
        {
            Debug.Log($"[SafeRouteFinderManager] Started route to {result.best.name}. Reason: {result.reasoning}");
        }
    }

    private void HandleSafePlaceError(string error)
    {
        _calculationInProgress = false;
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError("[SafeRouteFinderManager] Safe place lookup failed: " + error);
        }
    }

    private bool StartRouteInternal(Vector2 start, Vector2 end)
    {
        if (_routeStarted)
        {
            Debug.Log("[SafeRouteFinderManager] Route already started. Ignoring new request.");
            return false;
        }

        if (orsRouting == null)
        {
            Debug.LogError("[SafeRouteFinderManager] ORS_Routing reference missing.");
            return false;
        }

        bool started = orsRouting.BeginRoute(start, end, snapPlayerToRoute);
        if (started)
        {
            _routeStarted = true;
        }
        return started;
    }
}


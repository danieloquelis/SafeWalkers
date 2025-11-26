using UnityEngine;

namespace SafeWalkers.SafePlaceCalculator
{
    public class SafePlaceCalculatorExample : MonoBehaviour
    {
        [SerializeField] private SafePlaceCalculatorService service;
        [SerializeField] private double latitude = 37.7749;
        [SerializeField] private double longitude = -122.4194;
        [SerializeField] private SafetyProfile profile = SafetyProfile.CrowdedIndoor;

        private void Reset()
        {
            if (service == null)
            {
                service = GetComponent<SafePlaceCalculatorService>();
            }
        }

        [ContextMenu("Find Safe Place Now")]
        public void RunExample()
        {
            if (service == null)
            {
                Debug.LogWarning("[SafePlaceCalculatorExample] SafePlaceCalculatorService is not assigned.");
                return;
            }

            service.FindSafePlace(latitude, longitude, profile, HandleResult, HandleError);
        }

        private void HandleResult(SafePlaceCalculatorResult result)
        {
            if (result == null || result.best == null)
            {
                Debug.LogWarning("[SafePlaceCalculatorExample] No result returned.");
                return;
            }

            Debug.Log(
                $"[SafePlaceCalculatorExample] Best place: {result.best.name} ({result.best.walkingDurationText})");
            Debug.Log($"[SafePlaceCalculatorExample] Reasoning: {result.reasoning}");

            if (result.alternatives != null && result.alternatives.Count > 0)
            {
                Debug.Log("[SafePlaceCalculatorExample] Alternatives:");
                foreach (var alt in result.alternatives)
                {
                    Debug.Log($" - {alt.name} ({alt.walkingDurationText})");
                }
            }
        }

        private void HandleError(string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("[SafePlaceCalculatorExample] " + error);
            }
        }
    }
}


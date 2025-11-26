using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace SafeWalkers.SafePlaceCalculator
{
    public enum SafetyProfile
    {
        InstitutionalHelp,
        CrowdedIndoor,
        OpenSpace
    }

    [Serializable]
    public class PlaceCandidate
    {
        public string name;
        public string placeId;
        public double lat;
        public double lng;
        public double? rating;
        public int? userRatingsTotal;
        public bool? openNow;
        public string vicinity;
        public string[] types;
        public int walkingDurationSeconds;
        public string walkingDurationText;
        public double straightLineDistanceMeters;
    }

    [Serializable]
    public class SafePlaceCalculatorResult
    {
        public PlaceCandidate best;
        public List<PlaceCandidate> alternatives;
        public string reasoning;
        public bool usedFallback;
    }

    public class SafePlaceCalculatorService : MonoBehaviour
    {
        private const string PlacesEndpoint = "https://maps.googleapis.com/maps/api/place/nearbysearch/json";
        private const string DistanceMatrixEndpoint = "https://maps.googleapis.com/maps/api/distancematrix/json";
        private const string OpenAIChatEndpoint = "https://api.openai.com/v1/chat/completions";

        [Header("Filters")]
        [SerializeField] private int maxWalkingMinutes = 8;
        [SerializeField] private double minRating = 3.5f;
        [SerializeField] private int minRatingsCount = 30;

        [Header("Limits")]
        [SerializeField] private int maxPlacesFromPlacesApi = 20;
        [SerializeField] private int maxCandidatesForOpenAI = 8;

        [Header("Defaults")]
        [SerializeField] private SafetyProfile defaultProfile = SafetyProfile.CrowdedIndoor;
        [SerializeField] private bool verboseLogging;

        private string _googleApiKey;
        private string _openAiApiKey;
        private ScriptableObject _openAiConfigAsset;
        private Coroutine _activeRoutine;

        public bool IsRunning => _activeRoutine != null;

        public void FindSafePlace(
            double latitude,
            double longitude,
            Action<SafePlaceCalculatorResult> onCompleted,
            Action<string> onError = null)
        {
            FindSafePlace(latitude, longitude, defaultProfile, onCompleted, onError);
        }

        public void FindSafePlace(
            double latitude,
            double longitude,
            SafetyProfile profile,
            Action<SafePlaceCalculatorResult> onCompleted,
            Action<string> onError = null)
        {
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
            }

            _activeRoutine = StartCoroutine(FindSafePlaceRoutine(latitude, longitude, profile, result =>
            {
                _activeRoutine = null;
                onCompleted?.Invoke(result);
            }, error =>
            {
                _activeRoutine = null;
                onError?.Invoke(error);
            }));
        }

        private IEnumerator FindSafePlaceRoutine(
            double latitude,
            double longitude,
            SafetyProfile profile,
            Action<SafePlaceCalculatorResult> onCompleted,
            Action<string> onError)
        {
            bool hasGoogleKey = EnsureGoogleApiKey();
            bool hasOpenAiKey = EnsureOpenAiConfig();

            if (!hasGoogleKey)
            {
                HandleFallbackResult(
                    null,
                    latitude,
                    longitude,
                    "Google API key is missing. Falling back to player's current location.",
                    onCompleted);
                yield break;
            }

            var profileConfig = GetProfileConfig(profile);
            List<PlaceCandidate> rawCandidates = null;
            List<PlaceCandidate> filteredCandidates = null;

            yield return StartCoroutine(FetchNearbyPlaces(latitude, longitude, profileConfig, result =>
            {
                rawCandidates = result;
                filteredCandidates = FilterPlaceCandidates(rawCandidates);
            }, error =>
            {
                onError?.Invoke(error);
            }));

            if (filteredCandidates == null || filteredCandidates.Count == 0)
            {
                HandleFallbackResult(
                    rawCandidates,
                    latitude,
                    longitude,
                    "No nearby places met the baseline filters.",
                    onCompleted);
                yield break;
            }

            yield return StartCoroutine(FillWalkingDurations(latitude, longitude, filteredCandidates));

            var ordered = OrderByWalkingTime(filteredCandidates).ToList();
            var fastCandidates = ordered
                .Where(c => c.walkingDurationSeconds > 0 && c.walkingDurationSeconds <= maxWalkingMinutes * 60)
                .Take(Mathf.Max(1, maxCandidatesForOpenAI))
                .ToList();

            if (fastCandidates.Count == 0)
            {
                HandleFallbackResult(
                    ordered,
                    latitude,
                    longitude,
                    $"No places within {maxWalkingMinutes} walking minutes. Showing the closest option instead.",
                    onCompleted);
                yield break;
            }

            if (!hasOpenAiKey)
            {
                HandleFallbackResult(
                    fastCandidates,
                    latitude,
                    longitude,
                    "OpenAI key missing. Falling back to heuristic ranking.",
                    onCompleted);
                yield break;
            }

            SafePlaceCalculatorResult aiResult = null;
            yield return StartCoroutine(AskOpenAIForBestPlace(latitude, longitude, profile, fastCandidates, result =>
            {
                aiResult = result;
            }, error =>
            {
                onError?.Invoke(error);
            }));

            if (aiResult != null)
            {
                onCompleted?.Invoke(aiResult);
                yield break;
            }

            HandleFallbackResult(
                fastCandidates,
                latitude,
                longitude,
                "Failed to parse OpenAI response. Using heuristic ranking instead.",
                onCompleted);
        }

        private IEnumerator FetchNearbyPlaces(
            double latitude,
            double longitude,
            ProfileConfig profileConfig,
            Action<List<PlaceCandidate>> onCompleted,
            Action<string> onError)
        {
            string url = BuildPlacesUrl(latitude, longitude, profileConfig);
            if (verboseLogging)
            {
                Debug.Log($"[SafePlaceCalculator] Places URL: {url}");
            }

            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = $"Places API error: {request.error}";
                Debug.LogError($"[SafePlaceCalculator] {error}");
                onError?.Invoke(error);
                onCompleted?.Invoke(new List<PlaceCandidate>());
                yield break;
            }

            var response = JsonConvert.DeserializeObject<GooglePlacesResponse>(request.downloadHandler.text);
            if (!string.Equals(response?.status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                string error = $"Places API response status={response?.status} message={response?.error_message}";
                Debug.LogWarning($"[SafePlaceCalculator] {error}");
                onError?.Invoke(error);
                onCompleted?.Invoke(new List<PlaceCandidate>());
                yield break;
            }

            var candidates = ToCandidates(response, latitude, longitude)
                .Take(Mathf.Clamp(maxPlacesFromPlacesApi, 1, 60))
                .ToList();

            if (verboseLogging)
            {
                Debug.Log($"[SafePlaceCalculator] Retrieved {candidates.Count} candidates from Places API.");
            }

            onCompleted?.Invoke(candidates);
        }

        private IEnumerator FillWalkingDurations(
            double latitude,
            double longitude,
            List<PlaceCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                yield break;

            string destinations = string.Join("|", candidates.Select(c =>
                $"{c.lat.ToString(CultureInfo.InvariantCulture)},{c.lng.ToString(CultureInfo.InvariantCulture)}"));

            string url =
                $"{DistanceMatrixEndpoint}?origins={latitude.ToString(CultureInfo.InvariantCulture)}," +
                $"{longitude.ToString(CultureInfo.InvariantCulture)}" +
                $"&destinations={destinations}&mode=walking&key={_googleApiKey}";

            if (verboseLogging)
            {
                Debug.Log($"[SafePlaceCalculator] DistanceMatrix URL: {url}");
            }

            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SafePlaceCalculator] Distance Matrix error: {request.error}");
                yield break;
            }

            var response = JsonConvert.DeserializeObject<DistanceMatrixResponse>(request.downloadHandler.text);
            if (!string.Equals(response?.status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[SafePlaceCalculator] Distance Matrix status={response?.status} message={response?.error_message}");
                yield break;
            }

            var elements = response.rows?.FirstOrDefault()?.elements;
            if (elements == null || elements.Length == 0)
            {
                Debug.LogWarning("[SafePlaceCalculator] Distance Matrix returned no elements.");
                yield break;
            }

            for (int i = 0; i < candidates.Count && i < elements.Length; i++)
            {
                var element = elements[i];
                if (!string.Equals(element?.status, "OK", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (element.duration != null)
                {
                    candidates[i].walkingDurationSeconds = element.duration.value;
                    candidates[i].walkingDurationText = element.duration.text;
                }
            }
        }

        private IEnumerator AskOpenAIForBestPlace(
            double latitude,
            double longitude,
            SafetyProfile profile,
            List<PlaceCandidate> candidates,
            Action<SafePlaceCalculatorResult> onCompleted,
            Action<string> onError)
        {
            if (candidates == null || candidates.Count == 0)
            {
                onCompleted?.Invoke(null);
                yield break;
            }

            var payload = BuildOpenAiPayload(latitude, longitude, profile, candidates);
            var jsonBody = JsonConvert.SerializeObject(payload);

            using var request = new UnityWebRequest(OpenAIChatEndpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_openAiApiKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string error = $"OpenAI request failed: {request.error}";
                Debug.LogWarning($"[SafePlaceCalculator] {error}");
                onError?.Invoke(error);
                onCompleted?.Invoke(null);
                yield break;
            }

            var response = JsonConvert.DeserializeObject<ChatCompletionResponse>(request.downloadHandler.text);
            string content = response?.choices?.FirstOrDefault()?.message?.content;
            if (string.IsNullOrWhiteSpace(content))
            {
                onError?.Invoke("OpenAI response did not contain content.");
                onCompleted?.Invoke(null);
                yield break;
            }

            if (TryParseSelection(content, candidates, out SafePlaceCalculatorResult result, out string parsingError))
            {
                onCompleted?.Invoke(result);
                yield break;
            }

            if (verboseLogging)
            {
                Debug.LogWarning($"[SafePlaceCalculator] Failed to parse OpenAI JSON: {parsingError}\n{content}");
            }

            onError?.Invoke(parsingError);
            onCompleted?.Invoke(null);
        }

        private object BuildOpenAiPayload(
            double latitude,
            double longitude,
            SafetyProfile profile,
            List<PlaceCandidate> candidates)
        {
            var modelCandidates = candidates.Select(c => new CandidateForModel
            {
                name = c.name,
                placeId = c.placeId,
                lat = c.lat,
                lng = c.lng,
                rating = c.rating,
                userRatingsTotal = c.userRatingsTotal,
                walkingDurationSeconds = c.walkingDurationSeconds,
                walkingDuration = c.walkingDurationText,
                vicinity = c.vicinity,
                types = c.types
            }).ToList();

            var userPayload = new
            {
                userLocation = new { lat = latitude, lng = longitude },
                heuristic = GetProfileHeuristic(profile),
                candidates = modelCandidates
            };

            string instructions =
                "Return strict JSON with fields 'best' (single object), 'alternatives' (array) and 'reasoning' (string). " +
                "Each place entry must include name and placeId from the provided candidates. " +
                "Never claim a place is guaranteed safe; instead say it 'may feel safer'.";

            return new
            {
                model = "gpt-4.1-mini",
                messages = new[]
                {
                    new { role = "system", content = "You help a pedestrian pick safe-feeling nearby locations." },
                    new
                    {
                        role = "user",
                        content = $"{instructions}\nInput:\n{JsonConvert.SerializeObject(userPayload, Formatting.Indented)}"
                    }
                }
            };
        }

        private bool TryParseSelection(
            string content,
            List<PlaceCandidate> candidates,
            out SafePlaceCalculatorResult result,
            out string error)
        {
            result = null;
            error = null;

            string json = ExtractJson(content);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "OpenAI response was not JSON.";
                return false;
            }

            try
            {
                var selection = JsonConvert.DeserializeObject<OpenAISelection>(json);
                if (selection?.best == null)
                {
                    error = "OpenAI JSON missing 'best' field.";
                    return false;
                }

                var best = FindCandidate(candidates, selection.best.placeId, selection.best.name);
                if (best == null)
                {
                    error = "OpenAI selected place not found in candidates.";
                    return false;
                }

                var alternatives = new List<PlaceCandidate>();
                if (selection.alternatives != null)
                {
                    foreach (var alt in selection.alternatives)
                    {
                        var match = FindCandidate(candidates, alt.placeId, alt.name);
                        if (match != null && match != best && alternatives.All(c => c != match))
                        {
                            alternatives.Add(match);
                        }
                    }
                }

                result = new SafePlaceCalculatorResult
                {
                    best = best,
                    alternatives = alternatives,
                    reasoning = string.IsNullOrWhiteSpace(selection.reasoning)
                        ? "OpenAI did not return reasoning."
                        : selection.reasoning.Trim(),
                    usedFallback = false
                };
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to deserialize OpenAI JSON: {ex.Message}";
                return false;
            }
        }

        private static string ExtractJson(string content)
        {
            string trimmed = content.Trim();
            if (trimmed.StartsWith("```"))
            {
                int start = trimmed.IndexOf('{');
                int end = trimmed.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    return trimmed.Substring(start, end - start + 1);
                }
            }

            return trimmed;
        }

        private PlaceCandidate FindCandidate(List<PlaceCandidate> candidates, string placeId, string name)
        {
            if (!string.IsNullOrEmpty(placeId))
            {
                var byId = candidates.FirstOrDefault(c =>
                    !string.IsNullOrEmpty(c.placeId) &&
                    string.Equals(c.placeId, placeId, StringComparison.OrdinalIgnoreCase));
                if (byId != null)
                    return byId;
            }

            if (!string.IsNullOrEmpty(name))
            {
                return candidates.FirstOrDefault(c =>
                    string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private void HandleFallbackResult(
            IEnumerable<PlaceCandidate> pool,
            double latitude,
            double longitude,
            string reason,
            Action<SafePlaceCalculatorResult> onCompleted)
        {
            var ordered = pool?.ToList() ?? new List<PlaceCandidate>();
            if (ordered.Count == 0)
            {
                ordered.Add(new PlaceCandidate
                {
                    name = "Current Location",
                    placeId = "current-location",
                    lat = latitude,
                    lng = longitude,
                    walkingDurationSeconds = 0,
                    walkingDurationText = "0 min"
                });
            }

            var best = ordered[0];
            var alternatives = ordered.Skip(1).Take(2).ToList();

            onCompleted?.Invoke(new SafePlaceCalculatorResult
            {
                best = best,
                alternatives = alternatives,
                reasoning = reason,
                usedFallback = true
            });
        }

        private bool EnsureGoogleApiKey()
        {
            if (!string.IsNullOrWhiteSpace(_googleApiKey))
                return true;

            var keyAsset = Resources.Load<TextAsset>("google_maps_api_key");
            _googleApiKey = keyAsset?.text?.Trim();

            if (string.IsNullOrWhiteSpace(_googleApiKey))
            {
                Debug.LogError("[SafePlaceCalculator] google_maps_api_key.txt is missing or empty under Resources.");
                return false;
            }

            return true;
        }

        private bool EnsureOpenAiConfig()
        {
            if (!string.IsNullOrWhiteSpace(_openAiApiKey))
                return true;

            _openAiConfigAsset = Resources.Load<ScriptableObject>("OpenAIConfig");
            if (_openAiConfigAsset == null)
            {
                Debug.LogWarning("[SafePlaceCalculator] Resources/OpenAIConfig.asset is missing.");
                return false;
            }

            _openAiApiKey = ReadStringMember(_openAiConfigAsset, "apiKey");
            if (string.IsNullOrWhiteSpace(_openAiApiKey))
            {
                Debug.LogWarning("[SafePlaceCalculator] OpenAIConfig asset does not contain a valid apiKey.");
                return false;
            }

            _openAiApiKey = _openAiApiKey.Trim();
            return true;
        }

        private string BuildPlacesUrl(double latitude, double longitude, ProfileConfig profileConfig)
        {
            string url =
                $"{PlacesEndpoint}?location={latitude.ToString(CultureInfo.InvariantCulture)}," +
                $"{longitude.ToString(CultureInfo.InvariantCulture)}&rankby=distance&key={_googleApiKey}";

            if (profileConfig.types != null && profileConfig.types.Length > 0)
            {
                url += $"&type={profileConfig.types[0]}";
            }

            if (!string.IsNullOrEmpty(profileConfig.keyword))
            {
                url += $"&keyword={UnityWebRequest.EscapeURL(profileConfig.keyword)}";
            }

            return url;
        }

        private List<PlaceCandidate> FilterPlaceCandidates(List<PlaceCandidate> all)
        {
            if (all == null)
                return new List<PlaceCandidate>();

            return all
                .Where(c => c.rating == null || c.rating >= minRating)
                .Where(c => c.userRatingsTotal == null || c.userRatingsTotal >= minRatingsCount)
                .Where(c => c.openNow != false)
                .Take(maxPlacesFromPlacesApi)
                .ToList();
        }

        private IEnumerable<PlaceCandidate> OrderByWalkingTime(IEnumerable<PlaceCandidate> candidates)
        {
            if (candidates == null)
                return Enumerable.Empty<PlaceCandidate>();

            return candidates
                .OrderBy(c => c.walkingDurationSeconds <= 0 ? int.MaxValue : c.walkingDurationSeconds)
                .ThenByDescending(c => c.rating ?? 0)
                .ThenByDescending(c => c.userRatingsTotal ?? 0)
                .ThenBy(c => c.straightLineDistanceMeters);
        }

        private IEnumerable<PlaceCandidate> ToCandidates(GooglePlacesResponse response, double originLat, double originLng)
        {
            if (response?.results == null)
                yield break;

            foreach (var result in response.results)
            {
                if (result?.geometry?.location == null)
                    continue;

                var candidate = new PlaceCandidate
                {
                    name = result.name,
                    placeId = result.place_id,
                    lat = result.geometry.location.lat,
                    lng = result.geometry.location.lng,
                    rating = result.rating,
                    userRatingsTotal = result.user_ratings_total,
                    openNow = result.opening_hours?.open_now,
                    vicinity = result.vicinity,
                    types = result.types,
                    straightLineDistanceMeters = ComputeHaversineMeters(
                        originLat,
                        originLng,
                        result.geometry.location.lat,
                        result.geometry.location.lng)
                };

                yield return candidate;
            }
        }

        private static double ComputeHaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double EarthRadiusMeters = 6371000;
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLon = DegreesToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusMeters * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180f;

        private ProfileConfig GetProfileConfig(SafetyProfile profile)
        {
            switch (profile)
            {
                case SafetyProfile.InstitutionalHelp:
                    return new ProfileConfig(new[] { "police", "hospital" }, "emergency services");
                case SafetyProfile.OpenSpace:
                    return new ProfileConfig(new[] { "park" }, "public plaza");
                case SafetyProfile.CrowdedIndoor:
                default:
                    return new ProfileConfig(new[] { "shopping_mall", "supermarket" }, "shopping mall supermarket");
            }
        }

        private string GetProfileHeuristic(SafetyProfile profile)
        {
            switch (profile)
            {
                case SafetyProfile.InstitutionalHelp:
                    return "Prefer hospitals, clinics, or police stations where trained staff can help quickly.";
                case SafetyProfile.OpenSpace:
                    return "Prefer large open plazas or parks with visibility and potential foot traffic.";
                case SafetyProfile.CrowdedIndoor:
                default:
                    return "Prefer big, well-lit indoor spaces (malls, supermarkets) that are busy and within a short walk.";
            }
        }

        private readonly struct ProfileConfig
        {
            public readonly string[] types;
            public readonly string keyword;

            public ProfileConfig(string[] types, string keyword)
            {
                this.types = types ?? Array.Empty<string>();
                this.keyword = keyword;
            }
        }

        #region DTOs

        [Serializable]
        private class GooglePlacesResponse
        {
            public string status;
            public string error_message;
            public GooglePlaceResult[] results;
        }

        [Serializable]
        private class GooglePlaceResult
        {
            public string name;
            public string place_id;
            public GoogleGeometry geometry;
            public double? rating;
            public int? user_ratings_total;
            public string vicinity;
            public string[] types;
            public GoogleOpeningHours opening_hours;
        }

        [Serializable]
        private class GoogleGeometry
        {
            public GoogleLocation location;
        }

        [Serializable]
        private class GoogleLocation
        {
            public double lat;
            public double lng;
        }

        [Serializable]
        private class GoogleOpeningHours
        {
            public bool open_now;
        }

        [Serializable]
        private class DistanceMatrixResponse
        {
            public string status;
            public string error_message;
            public DistanceMatrixRow[] rows;
        }

        [Serializable]
        private class DistanceMatrixRow
        {
            public DistanceMatrixElement[] elements;
        }

        [Serializable]
        private class DistanceMatrixElement
        {
            public string status;
            public DistanceMatrixValue duration;
        }

        [Serializable]
        private class DistanceMatrixValue
        {
            public int value;
            public string text;
        }

        [Serializable]
        private class ChatCompletionResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice
        {
            public ChatMessage message;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OpenAISelection
        {
            public SelectedPlace best;
            public SelectedPlace[] alternatives;
            public string reasoning;
        }

        [Serializable]
        private class SelectedPlace
        {
            public string name;
            public string placeId;
        }

        [Serializable]
        private class CandidateForModel
        {
            public string name;
            public string placeId;
            public double lat;
            public double lng;
            public double? rating;
            public int? userRatingsTotal;
            public int walkingDurationSeconds;
            public string walkingDuration;
            public string vicinity;
            public string[] types;
        }

        #endregion

        private static string ReadStringMember(ScriptableObject asset, string memberName)
        {
            if (asset == null)
                return null;

            var type = asset.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                return field.GetValue(asset) as string;
            }

            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanRead && property.PropertyType == typeof(string))
            {
                return property.GetValue(asset) as string;
            }

            return null;
        }
    }
}


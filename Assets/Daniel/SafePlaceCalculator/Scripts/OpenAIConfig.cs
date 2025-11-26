using UnityEngine;

namespace SafeWalkers.Configuration
{
    [CreateAssetMenu(
        fileName = "OpenAIConfig",
        menuName = "SafeWalkers/OpenAI Config",
        order = 0)]
    public class OpenAIConfig : ScriptableObject
    {
        [Tooltip("API key with access to the Responses/Chat API.")]
        public string apiKey;

        [Tooltip("Realtime websocket URL (optional).")]
        public string realtimeConvWebsocketUrl = "wss://api.openai.com/v1/realtime";

        private static OpenAIConfig _cached;

        public static OpenAIConfig Load()
        {
            if (_cached != null)
            {
                return _cached;
            }

            _cached = Resources.Load<OpenAIConfig>("OpenAIConfig");
            if (_cached == null)
            {
                Debug.LogError("[OpenAIConfig] Resources/OpenAIConfig.asset is missing.");
            }

            return _cached;
        }
    }
}


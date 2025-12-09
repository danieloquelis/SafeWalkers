using System.Collections;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

public class ManuallyDevicePairingManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField pairingIdInputField;
    [SerializeField] private QRCodeManager qrCodeManager;

    public void OnPairingDevice()
    {
        string pairingId = pairingIdInputField.text.Trim();

        if (string.IsNullOrWhiteSpace(pairingId))
        {
            Debug.LogWarning("[ManuallyDevicePairingManager] Pairing ID is empty!");
            return;
        }

        Debug.Log($"[ManuallyDevicePairingManager] Manual pairing triggered with ID: {pairingId}");

        qrCodeManager.BeepSound?.Invoke();
        StartCoroutine(InvokeWithDelay(pairingId));
    }

    private IEnumerator InvokeWithDelay(string pairingId)
    {
        yield return new WaitForSeconds(1f);

        // Create the same JSON payload that the mobile app's QR code generates
        var pairingPayload = new
        {
            type = "safewalk_pairing",
            device = "metaquest",
            pairingId = pairingId
        };

        string jsonPayload = JsonConvert.SerializeObject(pairingPayload);
        Debug.Log($"[ManuallyDevicePairingManager] Sending payload: {jsonPayload}");

        qrCodeManager.OnQRDetected?.Invoke(jsonPayload);
    }
}

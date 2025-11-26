using UnityEngine;

/// <summary>
/// Scriptable object that stores the Quest ↔ mobile Pusher credentials locally.
/// Keep the generated *.asset file out of version control to avoid leaking secrets.
/// </summary>
[CreateAssetMenu(
	fileName = "SafeWalkPusherSettings",
	menuName = "SafeWalkers/Pusher Settings",
	order = 0)]
public class SafeWalkPusherSettings : ScriptableObject
{
	public string appId;
	public string apiKey;
	public string secret;
	public string cluster = "eu";
}


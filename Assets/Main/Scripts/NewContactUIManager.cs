using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Handles the "create new emergency contact" form.
/// Persists contacts into the same PlayerPrefs list used by ContactDropDownController ("EmergencyContacts").
/// </summary>
public class NewContactUIManager : MonoBehaviour
{
    [Header("Form Fields")]
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_InputField phoneInput;

    [Header("Storage")]
    [Tooltip("PlayerPrefs key used to store emergency contacts (same format as ContactDropDownController).")]
    [SerializeField] private string contactsPrefsKey = "EmergencyContacts";

    [Serializable]
    public class StringEvent : UnityEvent<string> { }

    [Tooltip("Invoked when a validation or save error occurs. Receives a human-readable error message.")]
    [SerializeField] private StringEvent onError;

    [Tooltip("Invoked after a contact is successfully saved.")]
    [SerializeField] private UnityEvent onContactSaved;

    /// <summary>
    /// Called by the UI (e.g. a button/toggle) to save the current contact.
    /// </summary>
    public void OnSaveContact()
    {
        string name = nameInput != null ? nameInput.text.Trim() : string.Empty;
        string phone = phoneInput != null ? phoneInput.text.Trim() : string.Empty;

        // Basic validation
        if (string.IsNullOrEmpty(phone))
        {
            RaiseError("Phone number is required.");
            return;
        }

        if (!IsPhoneValid(phone))
        {
            RaiseError("Phone number is invalid. It must include a country code (e.g. +123456789) and contain digits only.");
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            // Optional: allow empty name, but it's usually nicer to require it.
            RaiseError("Name is required.");
            return;
        }

        // Load existing contacts
        var existing = LoadContactsFromPrefs();

        // Check for duplicate by phone number
        if (existing.Any(c => string.Equals(c.phoneNumber, phone, StringComparison.OrdinalIgnoreCase)))
        {
            RaiseError("This phone number has already been registered.");
            return;
        }

        // Add new contact and persist
        existing.Add(new ContactEntry { name = name, phoneNumber = phone });
        SaveContactsToPrefs(existing);

        // Optionally clear inputs
        if (nameInput != null) nameInput.text = string.Empty;
        if (phoneInput != null) phoneInput.text = string.Empty;

        onContactSaved?.Invoke();
    }

    private void RaiseError(string message)
    {
        onError?.Invoke(message);
    }

    private bool IsPhoneValid(string phone)
    {
        // Require leading '+' as country code indicator
        if (!phone.StartsWith("+"))
            return false;

        if (phone.Length < 5)
            return false;

        // All remaining characters must be digits
        for (int i = 1; i < phone.Length; i++)
        {
            if (!char.IsDigit(phone[i]))
                return false;
        }

        return true;
    }

    #region Contacts Persistence

    private struct ContactEntry
    {
        public string name;
        public string phoneNumber;
    }

    private List<ContactEntry> LoadContactsFromPrefs()
    {
        var list = new List<ContactEntry>();

        if (!PlayerPrefs.HasKey(contactsPrefsKey))
            return list;

        string stored = PlayerPrefs.GetString(contactsPrefsKey);
        if (string.IsNullOrWhiteSpace(stored))
            return list;

        string[] entries = stored.Split(';');
        foreach (string raw in entries)
        {
            string trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            string name = trimmed;
            string number = trimmed;

            string[] parts = trimmed.Split(':');
            if (parts.Length >= 2)
            {
                name = parts[0].Trim();
                number = parts[1].Trim();
            }

            list.Add(new ContactEntry
            {
                name = name,
                phoneNumber = number
            });
        }

        return list;
    }

    private void SaveContactsToPrefs(List<ContactEntry> contacts)
    {
        var pieces = new List<string>(contacts.Count);
        foreach (var c in contacts)
        {
            // Store as "name:number"
            string safeName = (c.name ?? string.Empty).Replace(";", " ").Replace(":", " ");
            string safeNumber = (c.phoneNumber ?? string.Empty).Replace(";", " ").Replace(":", " ");
            pieces.Add($"{safeName}:{safeNumber}");
        }

        string serialized = string.Join(";", pieces);
        PlayerPrefs.SetString(contactsPrefsKey, serialized);
        PlayerPrefs.Save();
    }

    #endregion
}


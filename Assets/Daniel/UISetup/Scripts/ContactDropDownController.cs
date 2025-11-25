using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum ContactSelectionRole
{
    Main,
    Secondary
}

public class ContactDropDownController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject dropDownList;
    [SerializeField] private ContactDropDownButton contactDropDownButton;
    [SerializeField] private ContactDropDownItem contactDropDownItemPrefab;

    [Header("Contacts Source")]
    [Tooltip("Key used in PlayerPrefs to store emergency contacts (same format as EmergencyContactController).")]
    [SerializeField] private string contactsPrefsKey = "EmergencyContacts";

    [Tooltip("Default icon used if a contact does not have a specific Sprite assigned.")]
    [SerializeField] private Sprite defaultContactIcon;

    [Header("Selection Persistence")]
    [Tooltip("Whether this dropdown controls the main or the secondary contact selection.")]
    [SerializeField] private ContactSelectionRole selectionRole = ContactSelectionRole.Main;

    [Tooltip("PlayerPrefs key used to store the main selected contact phone number.")]
    [SerializeField] private string mainContactPrefsKey = "MainEmergencyContactNumber";

    [Tooltip("PlayerPrefs key used to store the secondary selected contact phone number.")]
    [SerializeField] private string secondaryContactPrefsKey = "SecondaryEmergencyContactNumber";

    [Header("Events")]
    [Tooltip("Invoked when the dropdown is clicked and there are no contacts yet. Hook your 'create contact' flow here.")]
    [SerializeField] private UnityEvent onCreateContactRequested;

    private readonly List<Contact> _contacts = new();
    private Contact _selectedContact;

    private void Start()
    {
        if (dropDownList != null)
        {
            // Start hidden; it will be shown when we have contacts.
            dropDownList.SetActive(false);
        }

        // Build from prefs on initialization so existing contacts appear immediately.
        RefreshFromPrefs();
    }

    /// <summary>
    /// Called from the button that opens the dropdown.
    /// If there are contacts, it simply toggles the list visibility.
    /// If there are no contacts, it triggers the "create contact" flow instead.
    /// </summary>
    public void OnDropDownClicked()
    {
        if (_contacts.Count == 0)
        {
            // No contacts yet -> request creation flow
            onCreateContactRequested?.Invoke();
            return;
        }

        if (dropDownList != null)
        {
            dropDownList.SetActive(!dropDownList.activeSelf);
        }
    }

    /// <summary>
    /// Called by DropDownItem when the user selects a contact.
    /// </summary>
    /// <param name="contact">The chosen contact.</param>
    public void OnItemSelected(Contact contact)
    {
        _selectedContact = contact;

        if (contactDropDownButton != null)
        {
            contactDropDownButton.SetContact(contact);
        }

        SaveSelectionToPrefs(contact);

        if (dropDownList != null)
        {
            dropDownList.SetActive(false);
        }
    }

    /// <summary>
    /// Returns the currently selected contact (can be null if none selected yet).
    /// </summary>
    public Contact GetSelectedContact()
    {
        return _selectedContact;
    }

    /// <summary>
    /// True if there is at least one contact loaded into this dropdown.
    /// </summary>
    public bool HasContacts => _contacts.Count > 0;

    /// <summary>
    /// Public method to rebuild the dropdown list from the current PlayerPrefs data.
    /// Call this after adding/removing contacts in prefs so the UI reflects the latest state.
    /// </summary>
    public void RefreshFromPrefs()
    {
        // Re-load contacts from the shared prefs key.
        LoadContactsFromPrefs();

        // If we now have contacts, rebuild the list and restore selection if possible.
        if (_contacts.Count > 0)
        {
            PopulateList();
            RestoreSelectionFromPrefs();

            // Ensure the dropdown list is visible now that we have items.
            if (dropDownList != null)
            {
                dropDownList.SetActive(true);
            }
        }
        else
        {
            // No contacts: clear the dropdown list and button display.
            if (dropDownList != null)
            {
                foreach (Transform child in dropDownList.transform)
                {
                    Destroy(child.gameObject);
                }
                dropDownList.SetActive(false);
            }

            _selectedContact = null;

            if (contactDropDownButton != null)
            {
                contactDropDownButton.SetContact(null);
            }
        }
    }

    private void LoadContactsFromPrefs()
    {
        _contacts.Clear();

        if (!PlayerPrefs.HasKey(contactsPrefsKey))
        {
            return;
        }

        string stored = PlayerPrefs.GetString(contactsPrefsKey);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return;
        }

        // Expected format is a ';' separated list.
        // Each entry can be:
        //  - "number"
        //  - "name:number"
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

            var contact = new Contact
            {
                name = name,
                phoneNumber = number,
                icon = defaultContactIcon
            };

            _contacts.Add(contact);
        }
    }

    private void PopulateList()
    {
        if (dropDownList == null || contactDropDownItemPrefab == null)
        {
            Debug.LogWarning("[DropDownController] Missing dropDownList or dropDownItemPrefab.");
            return;
        }

        Transform parent = dropDownList.transform;

        // Clear existing children
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }

        // Instantiate one item per contact
        foreach (Contact contact in _contacts)
        {
            ContactDropDownItem item = Instantiate(contactDropDownItemPrefab, parent);
            item.Initialize(contact, this);
        }
    }

    private string GetSelectionPrefsKey()
    {
        return selectionRole == ContactSelectionRole.Main
            ? mainContactPrefsKey
            : secondaryContactPrefsKey;
    }

    private void SaveSelectionToPrefs(Contact contact)
    {
        string key = GetSelectionPrefsKey();
        if (string.IsNullOrEmpty(key) || contact == null || string.IsNullOrWhiteSpace(contact.phoneNumber))
        {
            return;
        }

        PlayerPrefs.SetString(key, contact.phoneNumber);
        PlayerPrefs.Save();
    }

    private void RestoreSelectionFromPrefs()
    {
        if (_contacts.Count == 0)
        {
            return;
        }

        string key = GetSelectionPrefsKey();
        Contact found = null;

        if (!string.IsNullOrEmpty(key) && PlayerPrefs.HasKey(key))
        {
            string savedNumber = PlayerPrefs.GetString(key);
            if (!string.IsNullOrWhiteSpace(savedNumber))
            {
                found = _contacts.Find(c => c.phoneNumber == savedNumber);
            }
        }

        // If there is no saved selection or the saved one no longer exists:
        if (found == null)
        {
            // For the MAIN dropdown, default to the first contact and persist it.
            if (selectionRole == ContactSelectionRole.Main)
            {
                found = _contacts[0];
                SaveSelectionToPrefs(found);
            }
            else
            {
                // For SECONDARY, leave it unassigned so the button stays empty.
                _selectedContact = null;
                if (contactDropDownButton != null)
                {
                    contactDropDownButton.SetContact(null);
                }
                return;
            }
        }

        _selectedContact = found;

        if (contactDropDownButton != null)
        {
            contactDropDownButton.SetContact(found);
        }
    }
}

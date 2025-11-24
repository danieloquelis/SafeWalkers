using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ContactDropDownItem : MonoBehaviour
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private Image icon;

    private Contact _contact;
    private ContactDropDownController _controller;

    public void Initialize(Contact contact, ContactDropDownController controller)
    {
        _controller = controller;
        _contact = contact;
        SetItem(contact);
    }

    public void SetItem(Contact contact)
    {
        if (contact == null)
        {
            title.text = string.Empty;
            subtitle.text = string.Empty;
            icon.enabled = false;
            return;
        }

        title.text = contact.name;
        subtitle.text = contact.phoneNumber;

        if (contact.icon != null)
        {
            icon.sprite = contact.icon;
            icon.enabled = true;
        }
        else
        {
            icon.enabled = false;
        }
    }

    /// <summary>
    /// Hook this from the item's Button OnClick in the prefab.
    /// </summary>
    public void OnItemClicked()
    {
        if (_controller != null && _contact != null)
        {
            _controller.OnItemSelected(_contact);
        }
    }
}


using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ContactDropDownButton : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text subTitle;
    [SerializeField] private Image icon;

    /// <summary>
    /// Updates the button visuals to reflect the selected contact.
    /// </summary>
    /// <param name="contact">The contact to display; can be null to clear the button.</param>
    public void SetContact(Contact contact)
    {
        if (contact == null)
        {
            // When there is no contact selected, keep whatever placeholder or
            // instructional text was configured in the editor. Do not overwrite
            // the existing visuals so users still see guidance.
            return;
        }

        if (title != null)
        {
            title.text = contact.name;
        }

        if (subTitle != null)
        {
            subTitle.text = contact.phoneNumber;
            subTitle.gameObject.SetActive(true);
        }

        if (icon != null)
        {
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
    }
}


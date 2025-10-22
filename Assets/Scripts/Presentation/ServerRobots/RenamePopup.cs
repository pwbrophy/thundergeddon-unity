using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A very small modal for editing robot name + player.
// Enable this GameObject to show it; Disable to hide it.
public class RenamePopup : MonoBehaviour
{
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_Dropdown playerDropdown;
    [SerializeField] private Button okButton;
    [SerializeField] private Button cancelButton;

    private string _robotId;
    private Action<string, string> _onApply;

    void Awake()
    {
        gameObject.SetActive(false); // start hidden
    }

    // Show the popup, prefill values, and provide a callback to receive changes.
    public void Open(string robotId, string currentName, string currentPlayer, Action<string, string> onApply)
    {
        _robotId = robotId;
        _onApply = onApply;

        nameInput.text = currentName ?? "";

        // Ensure dropdown has at least these options
        EnsureOptions();
        var idx = playerDropdown.options.FindIndex(o => o.text == (string.IsNullOrEmpty(currentPlayer) ? "Unassigned" : currentPlayer));
        playerDropdown.value = Mathf.Max(0, idx);

        okButton.onClick.RemoveAllListeners();
        cancelButton.onClick.RemoveAllListeners();

        okButton.onClick.AddListener(() =>
        {
            var newName = string.IsNullOrWhiteSpace(nameInput.text) ? currentName : nameInput.text.Trim();
            var newPlayer = playerDropdown.options[playerDropdown.value].text;
            _onApply?.Invoke(newName, newPlayer);
            Close();
        });

        cancelButton.onClick.AddListener(Close);

        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    private void EnsureOptions()
    {
        if (playerDropdown.options.Count == 0)
        {
            playerDropdown.options.Add(new TMP_Dropdown.OptionData("Unassigned"));
            playerDropdown.options.Add(new TMP_Dropdown.OptionData("Player1"));
            playerDropdown.options.Add(new TMP_Dropdown.OptionData("Player2"));
        }
    }
}

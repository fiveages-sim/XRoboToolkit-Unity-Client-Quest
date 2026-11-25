using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Quest/Meta overlay keyboard bridge for TMP_InputField.
/// Manifest already declares oculus.software.overlay_keyboard; OVR Requires System Keyboard
/// must also be on. This component forces TouchScreenKeyboard open on select and syncs text,
/// which is more reliable than relying on TMP alone with XR ray clicks.
/// </summary>
[RequireComponent(typeof(TMP_InputField))]
public class QuestTmpKeyboard : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField] private TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;

    private TMP_InputField _inputField;
    private TouchScreenKeyboard _keyboard;
    private bool _selected;

    private void Awake()
    {
        _inputField = GetComponent<TMP_InputField>();
        // Avoid fighting the system overlay keyboard with TMP's built-in mobile keyboard path.
        _inputField.shouldHideSoftKeyboard = true;
    }

    public void OnSelect(BaseEventData eventData)
    {
        _selected = true;
        OpenKeyboard(_inputField.text);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        _selected = false;
        // Keep keyboard reference until done so LateUpdate can still flush final text.
    }

    private void OpenKeyboard(string text)
    {
        if (!TouchScreenKeyboard.isSupported && Application.platform != RuntimePlatform.Android)
        {
            return;
        }

        _keyboard = TouchScreenKeyboard.Open(
            text ?? string.Empty,
            keyboardType,
            false,
            false,
            false,
            false,
            "",
            64);
    }

    private void LateUpdate()
    {
        if (_keyboard == null)
        {
            return;
        }

        if (_inputField != null && _selected)
        {
            _inputField.SetTextWithoutNotify(_keyboard.text);
            _inputField.caretPosition = _keyboard.text.Length;
        }

        if (_keyboard.status == TouchScreenKeyboard.Status.Done ||
            _keyboard.status == TouchScreenKeyboard.Status.Canceled ||
            _keyboard.status == TouchScreenKeyboard.Status.LostFocus)
        {
            if (_inputField != null && _keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                _inputField.text = _keyboard.text;
            }

            _keyboard = null;
        }
    }
}

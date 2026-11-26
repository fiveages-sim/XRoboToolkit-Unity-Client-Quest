using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CameraSendInputDialog : MonoBehaviour
{
    public TMP_InputField TmpInput;
    //   public TMP_Dropdown renderModeDropdown;

    public Action<string> OnConfirmCall;

    public Button CloseBtn;
    public Button ConfirmBtn;

    private void Awake()
    {
        CloseBtn.onClick.AddListener(OnCloseBtn);
        ConfirmBtn.onClick.AddListener(OnConfirmBtn);

        if (TmpInput != null && TmpInput.GetComponent<QuestTmpKeyboard>() == null)
        {
            TmpInput.gameObject.AddComponent<QuestTmpKeyboard>();
        }
    }

    private void OnDestroy()
    {
        CloseBtn.onClick.RemoveListener(OnCloseBtn);
        ConfirmBtn.onClick.RemoveListener(OnConfirmBtn);
    }

    public void Show(Action<string> onConfirmCall, string defaultIp = null)
    {
        if (!string.IsNullOrEmpty(defaultIp))
        {
            TmpInput.text = defaultIp;
        }
        else
        {
            TmpInput.text = PlayerPrefs.GetString("CameraSendInputDialog", "");
        }

        OnConfirmCall = onConfirmCall;
        gameObject.SetActive(true);
    }

    private void OnConfirmBtn()
    {
        string ip = (TmpInput != null ? TmpInput.text : string.Empty).Trim().Replace(" ", string.Empty);
        if (TmpInput != null)
        {
            TmpInput.text = ip;
        }

        PlayerPrefs.SetString("CameraSendInputDialog", ip);
        if (OnConfirmCall != null)
        {
            OnConfirmCall(ip);
        }

        gameObject.SetActive(false);
    }

    private void OnCloseBtn()
    {
        gameObject.SetActive(false);
    }
}
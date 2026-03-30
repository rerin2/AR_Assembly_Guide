using UnityEngine;
using TMPro; // TextMeshPro 사용을 위해 필수

public class MenuUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject mainUIPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Input Fields (TMP)")]
    [SerializeField] private TMP_InputField serverIpInput; 
    [SerializeField] private TMP_InputField tickrateInput;

    [Header("Scripts Reference")]
    // 기존 ARImageSender 스크립트를 연결하여 재연결 명령을 내리기 위함
    [SerializeField] private ARImageSender arImageSender; 

    private void Start()
    {
        // 앱이 시작될 때 저장된 값을 불러오고, 설정창은 닫아둡니다.
        LoadSavedSettings();
        
        mainUIPanel.SetActive(true);
        settingsPanel.SetActive(false);
    }

    // 1. 톱니바퀴 버튼 (Gear_btn)
    public void OpenSettings()
    {
        LoadSavedSettings(); // 창을 열 때마다 저장된 값을 다시 띄움
        mainUIPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    // 2. X 버튼 (Close_btn)
    public void CloseSettingsOnly()
    {
        // 저장 없이 창만 닫고 메인으로 돌아감
        settingsPanel.SetActive(false);
        mainUIPanel.SetActive(true);
    }

    // 3. Save & Close 버튼 (Save_and_close_btn)
    public void SaveAndClose()
    {
        SaveInputData(); // 1. 입력값 저장
        arImageSender.ApplySettingsAndReconnect(); // 2. ARImageSender에 재연결 지시
        CloseSettingsOnly(); // 3. 창 닫기
    }

    // 4. Reconnect 버튼 (Rc_btn)
    public void ReconnectServer()
    {
        SaveInputData(); // 현재 적힌 값으로 저장 후 
        arImageSender.ApplySettingsAndReconnect(); // 재연결 지시 (창은 안 닫음)
    }

    // --- 내부 로직 ---

    private void SaveInputData()
    {
        // IP와 Port가 하나의 입력창으로 합쳐졌으므로 한 번에 저장 (예: 192.168.0.5:8000)
        PlayerPrefs.SetString("SavedIP", serverIpInput.text);
        
        if (float.TryParse(tickrateInput.text, out float parsedRate))
        {
            PlayerPrefs.SetFloat("SavedRate", parsedRate);
        }
        else
        {
            Debug.LogWarning("틱레이트는 숫자만 입력해야 합니다!");
        }
        
        PlayerPrefs.Save(); // 디스크에 확정 저장
    }

    private void LoadSavedSettings()
    {
        // 이전에 저장해둔 값이 있다면 입력창에 자동으로 채워줌
        if (PlayerPrefs.HasKey("SavedIP"))
        {
            serverIpInput.text = PlayerPrefs.GetString("SavedIP");
        }
        if (PlayerPrefs.HasKey("SavedRate"))
        {
            tickrateInput.text = PlayerPrefs.GetFloat("SavedRate").ToString();
        }
    }
}
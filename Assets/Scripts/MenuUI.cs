using UnityEngine;
using TMPro; // TextMeshPro 사용을 위해 필수


public class MenuUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject mainUIPanel;
    [SerializeField] private GameObject settingsPanel;
    
    // 앱 종료 확인 팝업
    [SerializeField] private GameObject exitConfirmPanel;

    [Header("Input Fields (TMP)")]
    [SerializeField] private TMP_InputField serverIpInput; 
    [SerializeField] private TMP_InputField tickrateInput;

    [Header("Scripts Reference")]
    // 기존 ARImageSender 스크립트를 연결하여 재연결 명령을 내리기 위함
    [SerializeField] private ARImageSender arImageSender; 

    private void Start()
    {
        Screen.fullScreen = true;
        Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        
        // 앱이 시작될 때 저장된 값을 불러오고, 설정창은 닫아둡니다.
        LoadSavedSettings();
        
        mainUIPanel.SetActive(true);
        settingsPanel.SetActive(false);
        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);
    }
    
    public void OpenExitConfirmPanel()
    {
        if (exitConfirmPanel != null)
        {
            exitConfirmPanel.SetActive(true);
            // 팝업이 뜰 때 다른 UI 조작을 막고 싶다면 여기에 추가 로직을 넣을 수 있습니다.
        }
    }
    
    // // 뒤로가기 버튼을 눌렀을 때의 동작 흐름
    // private void HandleBackButton()
    // {
    //     // 1순위: 설정 창이 열려있다면 -> 창을 닫고 메인으로.
    //     if (settingsPanel.activeSelf)
    //     {
    //         CloseSettingsOnly();
    //     }
    //     // 2순위: 앱 종료 팝업이 이미 떠있다면 -> 팝업을 닫는다.
    //     else if (exitConfirmPanel != null && exitConfirmPanel.activeSelf)
    //     {
    //         CancelExitApp();
    //     }
    //     // 3순위: 열린 창이 아무것도 없다면(메인 화면) -> 앱 종료 팝업을 띄운다.
    //     else
    //     {
    //         if (exitConfirmPanel != null) exitConfirmPanel.SetActive(true);
    //     }
    // }

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
    
    // "예" 버튼
    public void ConfirmExitApp()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    // "아니요" 버튼
    public void CancelExitApp()
    {
        if (exitConfirmPanel != null)
        {
            exitConfirmPanel.SetActive(false);
        }
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
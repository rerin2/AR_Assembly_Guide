using System.Collections;
using UnityEngine;
using NativeWebSocket;
using System.Text;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

public class ARImageSender : MonoBehaviour
{
    [Header("Default Network Settings (최초 1회만 사용됨)")]
    public string defaultIp = "180.68.109.231";
    public string defaultPort = "8000";
    
    [Header("Capture Settings")]
    public float defaultSendRate = 0.01f; 
    [Range(10, 100)]
    public int jpgQuality = 100;   
    
    [Header("AR References")]
    public ARCameraManager cameraManager;
    public ARObjectPlacer objectPlacer;
    
    [Header("Image Processing Settings")]
    [Range(0.1f, 1.0f)]
    public float resolutionScale = 0.5f; 
    public bool rotateClockwise90 = true; 
    public bool mirrorCorrection = true; 
    
    private WebSocket websocket;
    private float timer;
    private string debugMessage = "초기화 중...";
    private bool isBusy;

    public int scaledWidth;
    public int scaledHeight;

    // 내부적으로 사용할 진짜 연결 주소와 전송 주기
    private string currentServerUrl;
    private float currentSendRate;

    // --- 화면 UI 입력을 위한 변수들 ---
    private string inputIp;
    private string inputPort;
    private string inputSendRate;
    
    private GUIStyle style;
    private GUIStyle inputStyle;
    private GUIStyle buttonStyle;
    
    void Start()
    {
        // 1. 기기에 저장된 이전 설정 불러오기 (없으면 기본값 사용)
        inputIp = PlayerPrefs.GetString("SavedIP", defaultIp);
        inputPort = PlayerPrefs.GetString("SavedPort", defaultPort);
        currentSendRate = PlayerPrefs.GetFloat("SavedRate", defaultSendRate);
        inputSendRate = currentSendRate.ToString();

        // 2. 입력받은 IP와 포트를 합쳐서 완전한 웹소켓 주소 조립
        currentServerUrl = $"ws://{inputIp}:{inputPort}/ws";

        // 3. 불러온 설정으로 최초 연결
        ConnectToServer();
    }

    private async void ConnectToServer()
    {
        if (websocket != null)
        {
            if (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.Connecting)
            {
                await websocket.Close();
            }
        }

        debugMessage = $"서버 연결 시도 중...\n({currentServerUrl})";
        websocket = new WebSocket(currentServerUrl);

        websocket.OnOpen += () => debugMessage = "✅ 접속 성공!";
        
        websocket.OnError += (e) => 
        {
            debugMessage = "❌ 에러: " + e;
            Debug.LogError("WebSocket Error: " + e);
        };
        
        websocket.OnClose += (e) => 
        {
            debugMessage = $"⚠️ 연결 종료 (Code: {e})";
            Debug.Log("WebSocket Closed: " + e);
        };
        
        websocket.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            if (objectPlacer != null)
            {
                objectPlacer.OnDataReceived(message);
            }
            isBusy = false;
        };
        
        await websocket.Connect();
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            if (websocket != null)
            {
                websocket.DispatchMessageQueue();
            }
        #endif

        if (websocket != null && websocket.State == WebSocketState.Open && !isBusy)
        {
            timer += Time.deltaTime;
            if (timer >= currentSendRate)
            {
                timer = 0f;
                StartCoroutine(CaptureAndSend());
            }
        }
    }

    IEnumerator CaptureAndSend()
    {
        isBusy = true;
        yield return null; 

        if (cameraManager == null)
        {
            debugMessage = "❌ ARCameraManager가 연결되지 않았습니다.";
            isBusy = false;
            yield break;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            isBusy = false;
            yield break;
        }

        try
        {
            scaledWidth = Mathf.RoundToInt(image.width * resolutionScale);
            scaledHeight = Mathf.RoundToInt(image.height * resolutionScale);

            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(scaledWidth, scaledHeight),
                outputFormat = TextureFormat.RGBA32, 
                transformation = XRCpuImage.Transformation.None
            };

            int size = image.GetConvertedDataSize(conversionParams);
            using (NativeArray<byte> buffer = new NativeArray<byte>(size, Allocator.Temp))
            {
                image.Convert(conversionParams, buffer);

                Texture2D tempTexture = new Texture2D(scaledWidth, scaledHeight, TextureFormat.RGBA32, false);
                tempTexture.LoadRawTextureData(buffer);
                tempTexture.Apply();

                Texture2D finalTexture = tempTexture;

                if (rotateClockwise90)
                {
                    finalTexture = RotateAndMirrorTexture(tempTexture, mirrorCorrection);
                    Destroy(tempTexture); 
                }

                byte[] imageBytes = finalTexture.EncodeToJPG(jpgQuality);
                string base64String = System.Convert.ToBase64String(imageBytes);

                websocket.SendText(base64String); 
                debugMessage = $"✅ 전송 중.. ({scaledWidth}x{scaledHeight})\n틱레이트: {currentSendRate}초";

                Destroy(finalTexture);
            }
        }
        catch (System.Exception e)
        {
            debugMessage = "전송 실패: " + e.Message;
            isBusy = false;
        }
        finally
        {
            image.Dispose(); 
        }
    }
    
    // 카메라 화면이 뒤집히고 거울모드로 나오는 문제가 있어 코드로 원래 상태로 출력되게 함 
    private Texture2D RotateAndMirrorTexture(Texture2D original, bool applyMirror)
    {
        Color32[] originalPixels = original.GetPixels32();
        int w = original.width;
        int h = original.height;
    
        Color32[] newPixels = new Color32[originalPixels.Length];
        int newW = h;
        int newH = w;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int rotatedX = y;
                int rotatedY = w - 1 - x;
                int finalX = applyMirror ? (newW - 1 - rotatedX) : rotatedX;
                int finalY = rotatedY;
                newPixels[finalY * newW + finalX] = originalPixels[y * w + x];
            }
        }

        Texture2D resultTexture = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        resultTexture.SetPixels32(newPixels);
        resultTexture.Apply();

        return resultTexture;
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 35 };
            style.normal.textColor = Color.yellow;
            style.wordWrap = true;

            inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 35 };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 35 };
        }

        // 1. IP 주소 (가로로 배치)
        GUI.Label(new Rect(20, 20, 80, 60), "IP:", style);
        inputIp = GUI.TextField(new Rect(100, 20, 400, 60), inputIp, inputStyle);

        // 2. 포트 번호 (IP 바로 옆에 배치)
        GUI.Label(new Rect(520, 20, 100, 60), "포트:", style);
        inputPort = GUI.TextField(new Rect(620, 20, 200, 60), inputPort, inputStyle);

        // 3. 전송 주기
        GUI.Label(new Rect(20, 100, 150, 60), "주기(초):", style);
        inputSendRate = GUI.TextField(new Rect(180, 100, 150, 60), inputSendRate, inputStyle);

        // 4. 저장 및 재연결 버튼
        if (GUI.Button(new Rect(350, 100, 470, 60), "💾 저장 및 재연결", buttonStyle))
        {
            ApplySettingsAndReconnect();
        }

        // 5. 상태 메시지 출력
        GUI.Label(new Rect(20, 180, Screen.width - 40, 500), debugMessage, style);
    }

    private void ApplySettingsAndReconnect()
    {
        // 1. 기기에 현재 입력된 값 저장 (앱을 꺼도 유지됨!)
        PlayerPrefs.SetString("SavedIP", inputIp);
        PlayerPrefs.SetString("SavedPort", inputPort);

        if (float.TryParse(inputSendRate, out float parsedRate))
        {
            currentSendRate = parsedRate;
            PlayerPrefs.SetFloat("SavedRate", currentSendRate);
        }
        else
        {
            debugMessage = "⚠️ 전송 주기는 숫자만 입력해주세요!";
            return; 
        }

        // 디스크에 확실히 저장
        PlayerPrefs.Save(); 

        // 2. 주소 조립 ("ws://" 와 "/ws"는 코드가 알아서 붙여줌)
        currentServerUrl = $"ws://{inputIp}:{inputPort}/ws";

        // 3. 재연결 수행
        isBusy = false;
        timer = 0f;
        ConnectToServer();
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
}
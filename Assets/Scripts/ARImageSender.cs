using System.Collections;
using UnityEngine;
using NativeWebSocket;
using System.Text;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

public class ARImageSender : MonoBehaviour
{
    [Header("Network Settings")]
    public string serverUrl = "ws://180.68.109.231:8000/ws";
    
    [Header("Capture Settings")]
    public float sendRate = 0.1f; // ~초마다 전송
    [Range(10, 100)]
    public int jpgQuality = 100;   // 화질
    
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

    // --- [추가] 화면 UI 입력을 위한 변수들 ---
    private string inputUrl;
    private string inputSendRate;
    private GUIStyle style;
    private GUIStyle inputStyle;
    private GUIStyle buttonStyle;
    // -----------------------------------------
    
    void Start()
    {
        // UI 입력칸에 현재 설정된 기본값 채워넣기
        inputUrl = serverUrl;
        inputSendRate = sendRate.ToString();

        // 앱 시작 시 최초 1회 연결 시도
        ConnectToServer();
    }

    // --- [추가/수정] 연결 로직을 별도 함수로 분리 ---
    private async void ConnectToServer()
    {
        // 이미 연결된 소켓이 있다면 안전하게 닫기
        if (websocket != null)
        {
            if (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.Connecting)
            {
                await websocket.Close();
            }
        }

        debugMessage = $"서버 연결 시도 중...\n({serverUrl})";
        websocket = new WebSocket(serverUrl);

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
            if (timer >= sendRate)
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
                debugMessage = $"✅ 전송 중.. ({scaledWidth}x{scaledHeight})\n틱레이트: {sendRate}초";

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

    // --- [수정] 화면에 UI (텍스트 입력창 & 버튼) 그리기 ---
    void OnGUI()
    {
        // 폰트 크기 및 색상 초기화 (최초 1회)
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 40 };
            style.normal.textColor = Color.yellow;
            style.wordWrap = true;

            inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 40 };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 40 };
        }

        // 1. IP 주소 입력 칸
        GUI.Label(new Rect(20, 20, 200, 60), "서버 주소:", style);
        inputUrl = GUI.TextField(new Rect(230, 20, 700, 60), inputUrl, inputStyle);

        // 2. 전송 속도 입력 칸
        GUI.Label(new Rect(20, 100, 200, 60), "전송(초):", style);
        inputSendRate = GUI.TextField(new Rect(230, 100, 200, 60), inputSendRate, inputStyle);

        // 3. 재연결 및 적용 버튼
        if (GUI.Button(new Rect(450, 100, 480, 60), "🚀 설정 적용 및 재연결", buttonStyle))
        {
            ApplySettingsAndReconnect();
        }

        // 4. 상태 메시지 (위치를 아래로 내림)
        GUI.Label(new Rect(20, 180, Screen.width - 40, 500), debugMessage, style);
    }

    // 버튼을 눌렀을 때 실행되는 함수
    private void ApplySettingsAndReconnect()
    {
        // 입력받은 주소 적용
        serverUrl = inputUrl;

        // 입력받은 글자를 숫자로 변환 시도
        if (float.TryParse(inputSendRate, out float parsedRate))
        {
            sendRate = parsedRate;
        }
        else
        {
            debugMessage = "⚠️ 전송 주기는 숫자만 입력해주세요! (예: 0.5)";
            return; // 숫자가 아니면 멈춤
        }

        // 상태 초기화 후 재연결
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
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
        // 기기에 저장된 이전 설정 불러오기 (없으면 기본값 사용)
        inputIp = PlayerPrefs.GetString("SavedIP", defaultIp);
        inputPort = PlayerPrefs.GetString("SavedPort", defaultPort);
        currentSendRate = PlayerPrefs.GetFloat("SavedRate", defaultSendRate);
        inputSendRate = currentSendRate.ToString();

        // 입력받은 IP와 포트를 합쳐서 완전한 웹소켓 주소 조립
        currentServerUrl = $"ws://{inputIp}:{inputPort}/ws";

        // 불러온 설정으로 최초 연결
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

        websocket.OnOpen += () => debugMessage = " 접속 성공!";
        
        websocket.OnError += (e) => 
        {
            debugMessage = " 에러: " + e;
            Debug.LogError("WebSocket Error: " + e);
        };
        
        websocket.OnClose += (e) => 
        {
            debugMessage = $" 연결 종료 (Code: {e})";
            Debug.Log("WebSocket Closed: " + e);
        };
        
        // 엣지로부터 메시지가 도착했을 때 자동으로 실행됨
        websocket.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            if (objectPlacer != null)
            {
                objectPlacer.OnDataReceived(message);   // 데이터 처리 
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
            debugMessage = " ARCameraManager가 연결되지 않았습니다.";
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
            // 해상도 축소 
            scaledWidth = Mathf.RoundToInt(image.width * resolutionScale);
            scaledHeight = Mathf.RoundToInt(image.height * resolutionScale);
            
            // 카메라에서 바로 오는 데이터 규칙
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height), // 원본 이미지 전체 영역을 사용
                outputDimensions = new Vector2Int(scaledWidth, scaledHeight),// 위에서 계산한 축소된 해상도로 출력
                outputFormat = TextureFormat.RGBA32, // 표준 컬러 포맷
                transformation = XRCpuImage.Transformation.None // 회전하지 않음 
            };
            
            // 버퍼 크기 
            int size = image.GetConvertedDataSize(conversionParams);
            
            // using 문을 사용하여 NativeArray(C++ 수준의 빠른 메모리)를 임시(Allocator.Temp)로 빌림
            // using 블록이 끝나면 가비지 컬렉터(GC)를 기다리지 않고 즉시 메모리를 반환하여 렉을 방지
            using (NativeArray<byte> buffer = new NativeArray<byte>(size, Allocator.Temp))
            {
                image.Convert(conversionParams, buffer);    // 이미지로 변환
                
                // 텍스처 생성 및 데이터 버퍼 주입 후 픽셀 데이터를 GPU에 적용
                Texture2D tempTexture = new Texture2D(scaledWidth, scaledHeight, TextureFormat.RGBA32, false);
                tempTexture.LoadRawTextureData(buffer);
                tempTexture.Apply();

                Texture2D finalTexture = tempTexture;
                
                // 카메라에서 나오는 영상이 90도로 돌아가는 문제 및 거울모드로 나오는 문제 있어 바로잡는 부분 
                if (rotateClockwise90)
                {
                    finalTexture = RotateAndMirrorTexture(tempTexture, mirrorCorrection);
                    Destroy(tempTexture); 
                }
                
                // jpg로 인코딩 후 Base64로 인코딩 후 엣지서버로 전송
                byte[] imageBytes = finalTexture.EncodeToJPG(jpgQuality);
                string base64String = System.Convert.ToBase64String(imageBytes);

                websocket.SendText(base64String); 
                debugMessage = $"✅ 전송 중.. ({scaledWidth}x{scaledHeight})\n틱레이트: {currentSendRate}초";
                
                // 텍스처 제거 
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
        // 기기에 현재 입력된 값 저장 (앱을 꺼도 유지됨!)
        PlayerPrefs.SetString("SavedIP", inputIp);
        PlayerPrefs.SetString("SavedPort", inputPort);

        if (float.TryParse(inputSendRate, out float parsedRate))
        {
            currentSendRate = parsedRate;
            PlayerPrefs.SetFloat("SavedRate", currentSendRate);
        }
        else
        {
            debugMessage = " 전송 주기는 숫자만 입력해주세요!";
            return; 
        }

        // 디스크에 확실히 저장
        PlayerPrefs.Save(); 

        // 주소 조립 ("ws://" 와 "/ws"는 코드가 알아서 붙여줌)
        currentServerUrl = $"ws://{inputIp}:{inputPort}/ws";

        // 재연결 수행
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
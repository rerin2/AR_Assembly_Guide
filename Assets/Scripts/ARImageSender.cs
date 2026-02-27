using System.Collections;
using UnityEngine;
using NativeWebSocket;
using System.Text;
using UnityEngine.XR.ARFoundation; // 추가됨
using UnityEngine.XR.ARSubsystems; // 추가됨
using Unity.Collections;           // 추가됨

public class ARImageSender : MonoBehaviour
{
    [Header("Network Settings")]
    public string serverUrl = "ws://180.68.109.231:8000/ws";
    
    [Header("Capture Settings")]
    public float sendRate = 1.0f; // ~초마다 전송
    [Range(10, 100)]
    public int jpgQuality = 50;   // 화질
    
    [Header("AR References")]
    public ARCameraManager cameraManager; // [새로 추가됨] 인스펙터에서 AR Camera를 연결하세요!
    public ARObjectPlacer objectPlacer;
    
    [Header("Image Processing Settings")]
    [Range(0.1f, 1.0f)]
    public float resolutionScale = 0.5f; // 0.5면 가로세로 크기가 절반으로 줄어듦 (전송 속도 최적화)
    public bool rotateClockwise90 = true; // 세로 모드를 위한 90도 시계방향 회전 켬/끔
    public bool mirrorCorrection = true; // 거울모드(좌우반전) 보정 켬/끔
    
    private WebSocket websocket;
    private float timer;
    private string debugMessage = "초기화 중...";
    private bool isBusy;

    public int scaledWidth;
    public int scaledHeight;
    
    async void Start()
    {
        debugMessage = "서버 연결 시도 중...";
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
            // 서버에서 결과 수신
            string message = Encoding.UTF8.GetString(bytes);
            
            if (objectPlacer != null)
            {
                objectPlacer.OnDataReceived(message);
            }

            // 응답을 받았으므로 다시 전송할 수 있도록 상태 해제
            isBusy = false;
        };
        
        // 서버 연결 시도
        await websocket.Connect();
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            websocket.DispatchMessageQueue();
        #endif

        // 연결되어 있고, 현재 처리 중인 작업(전송~수신 대기)이 없을 때만 타이머 작동
        if (websocket.State == WebSocketState.Open && !isBusy)
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
    yield return null; // 한 프레임 대기 (메인 스레드 부하 분산)

    if (cameraManager == null)
    {
        debugMessage = "❌ ARCameraManager가 연결되지 않았습니다.";
        isBusy = false;
        yield break;
    }

    // 1. 카메라 센서에서 순수 원본 프레임 가져오기
    if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
    {
        isBusy = false;
        yield break;
    }

    try
    {
        // 2. 해상도 동적 조절 (인스펙터에서 설정한 resolutionScale 비율 적용)
        scaledWidth = Mathf.RoundToInt(image.width * resolutionScale);
        scaledHeight = Mathf.RoundToInt(image.height * resolutionScale);

        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, image.width, image.height),
            outputDimensions = new Vector2Int(scaledWidth, scaledHeight),
            // [중요] 픽셀 단위 회전 연산을 고속으로 처리하기 위해 RGB24 대신 RGBA32 포맷 사용
            outputFormat = TextureFormat.RGBA32, 
            transformation = XRCpuImage.Transformation.None
        };

        // 3. 변환 및 텍스처 생성
        int size = image.GetConvertedDataSize(conversionParams);
        using (NativeArray<byte> buffer = new NativeArray<byte>(size, Allocator.Temp))
        {
            image.Convert(conversionParams, buffer);

            // 원본(가로) 임시 텍스처
            Texture2D tempTexture = new Texture2D(scaledWidth, scaledHeight, TextureFormat.RGBA32, false);
            tempTexture.LoadRawTextureData(buffer);
            tempTexture.Apply();

            Texture2D finalTexture = tempTexture;

            // 4. 세로 모드 회전이 켜져있다면 90도 회전 적용
            if (rotateClockwise90)
            {
                finalTexture = RotateAndMirrorTexture(tempTexture, mirrorCorrection);
                Destroy(tempTexture); // 다 쓴 원본 임시 텍스처는 즉시 삭제 (메모리 누수 방지)
            }

            // 5. JPG 압축 및 Base64 변환
            byte[] imageBytes = finalTexture.EncodeToJPG(jpgQuality);
            string base64String = System.Convert.ToBase64String(imageBytes);

            // 6. 서버로 전송
            websocket.SendText(base64String); 
            debugMessage = $"✅ 전송 중.. ({scaledWidth}x{scaledHeight})";

            // [매우 중요] 최종 전송에 사용된 텍스처도 메모리 해제
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
        image.Dispose(); // CpuImage 메모리 해제
    }
}

// [수정된 헬퍼 함수] 90도 회전과 동시에 거울모드(좌우반전) 보정을 수행
    private Texture2D RotateAndMirrorTexture(Texture2D original, bool applyMirror)
    {
        Color32[] originalPixels = original.GetPixels32();
        int w = original.width;
        int h = original.height;
    
        // 회전 후에는 가로/세로 크기가 뒤바뀜
        Color32[] newPixels = new Color32[originalPixels.Length];
        int newW = h;
        int newH = w;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 1. 기본 90도 시계방향 회전 좌표 계산
                int rotatedX = y;
                int rotatedY = w - 1 - x;

                // 2. 거울모드 보정이 켜져있으면 X좌표를 반전시킴 (좌우 반전)
                int finalX = applyMirror ? (newW - 1 - rotatedX) : rotatedX;
                int finalY = rotatedY;

                // 계산된 최종 위치에 픽셀 데이터 할당
                newPixels[finalY * newW + finalX] = originalPixels[y * w + x];
            }
        }

        // 가로 세로 크기가 뒤바뀐 새로운 텍스처 생성
        Texture2D resultTexture = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        resultTexture.SetPixels32(newPixels);
        resultTexture.Apply();

        return resultTexture;
    }

    private GUIStyle style;
    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle();
            style.fontSize = 40; 
            style.normal.textColor = Color.yellow; 
            style.wordWrap = true; 
        }
        GUI.Label(new Rect(50, 50, Screen.width - 100, 500), debugMessage, style);
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null) await websocket.Close();
    }
}
using System.Collections;
using System.Collections.Generic; // Dictionary 사용을 위해 추가
using UnityEngine;
using NativeWebSocket;

public class ARImageSender : MonoBehaviour
{
    // 본인의 PC IP 주소로 꼭 변경하세요! (포트 8000)
    // public string serverUrl = "ws://192.168.0.25:8000/ws"; 
    public string serverUrl = "ws://180.68.109.231:8000/ws";
    public float sendRate = 0.1f; // 0.1초마다 전송
    public int jpgQuality = 50;   // 화질 50%
    
    private WebSocket websocket;
    private Texture2D texture2D;
    private float timer;
    private string debugMessage = "초기화 중...";

    // 전송할 해상도 고정 (AI 처리에 최적화)
    private int targetWidth = 1080;
    private int targetHeight = 2340;
    
    // [Inspector에서 연결] 고화질 원본 (1080 x 2340)
    public RenderTexture sourceRenderTexture; 
    
    // [내부용] 서버 전송용 작아진 텍스처
    private RenderTexture smallRenderTexture;

    async void Start()
    {
        // [중요 수정 1] 연결보다 변수 초기화를 먼저 함 (Null 에러 방지)
        // texture2D = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);

        debugMessage = "서버 연결 시도 중...";
        websocket = new WebSocket(serverUrl);
        
        // [핵심 수정] 헤더(Header)를 추가하여 봇 차단을 우회합니다.
        var headers = new Dictionary<string, string>
        {
            { "User-Agent", "UnityPlayer/6000.3.3f1" }, // 유니티 버전 등 아무거나
            { "Origin", "ws://180.68.109.231:8000" } // 서버 주소와 동일하게
        };

        websocket.OnOpen += () => debugMessage = "✅ 서버 연결 성공!";
        websocket.OnError += (e) => debugMessage = "❌ 소켓 에러: " + e;
        websocket.OnClose += (e) => debugMessage += "⚠️ 연결 끊김" + e;
        
        // 전송용 작은 텍스처 생성 (메모리에만 존재)
        smallRenderTexture = new RenderTexture(630, 1365, 24);
        texture2D = new Texture2D(630, 1365, TextureFormat.RGB24, false);

        // 서버 연결 시도
        await websocket.Connect();
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            websocket.DispatchMessageQueue();
        #endif

        // [중요] 연결이 확실히 됐을 때만 전송 시작
        if (websocket.State == WebSocketState.Open)
        {
            timer += Time.deltaTime;
            if (timer >= sendRate)
            {
                timer = 0f;
                StartCoroutine(CaptureAndSend());
            }
        }
    }

    // [추가] Inspector에서 아까 만든 AR_Clean_Texture를 연결해줘야 함
    // public RenderTexture targetRenderTexture; 

    IEnumerator CaptureAndSend()
    {
        yield return new WaitForEndOfFrame();

        try
        {
            // 1. 거대한 원본(source)을 작은 텍스처(small)로 축소 복사 (Resizing)
            // Graphics.Blit이 알아서 고속으로 리사이징 해줍니다.
            Graphics.Blit(sourceRenderTexture, smallRenderTexture);

            // 2. 작은 텍스처에서 픽셀 읽기
            RenderTexture.active = smallRenderTexture;
            texture2D.ReadPixels(new Rect(0, 0, smallRenderTexture.width, smallRenderTexture.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = null;

            // 3. JPG 변환 및 전송
            byte[] imageBytes = texture2D.EncodeToJPG(jpgQuality);
            websocket.Send(imageBytes);

            debugMessage = $"✅ 전송 중.. ({imageBytes.Length} bytes)";
        }
        catch (System.Exception e)
        {
            debugMessage = "전송 실패: " + e.Message;
        }
    }

    // 스마트폰 화면에 상태 메시지 띄우기 (디버깅용)
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 50;
        style.normal.textColor = Color.red;
        style.fontStyle = FontStyle.Bold;
        
        // 화면 상단에 메시지 출력
        GUI.Label(new Rect(50, 50, 1000, 200), debugMessage, style);
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null) await websocket.Close();
    }
}
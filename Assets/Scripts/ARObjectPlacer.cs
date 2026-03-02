using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[System.Serializable]
public class DetectionResponse
{
    public string status;
    public DetectionResult[] results;
}

[System.Serializable]
public class DetectionResult
{
    public string label;
    public int[] box; // [x1, y1, x2, y2]
    public float conf;
}

public class AIData
{
    public string label;
    public float normX;
    public float normY;
}

public class ARObjectPlacer : MonoBehaviour
{
    [Header("AR Settings")]
    public ARRaycastManager raycastManager;
    public GameObject objectPrefab;
    private GameObject spawnedObject;
    
    [Header("References")]
    public ARImageSender imageSenderScript;

    [Header("Image Settings")]
    private float imageWidth;  
    private float imageHeight; 

    private Queue<AIData> dataQueue = new Queue<AIData>();

    // [UI 추가 변수들]
    private string debugMessage = "";
    private float messageTimer = 0f;
    private GUIStyle style;
    
    void Start()
    {
        if (imageSenderScript != null)
        {
            // 회전(거울모드 등) 옵션이 켜져 있다면 가로/세로가 반대로 전송되었으므로
            // 여기서도 똑같이 가로/세로를 뒤집어서 세팅해줍니다.
            if (imageSenderScript.rotateClockwise90)
            {
                imageWidth = imageSenderScript.scaledWidth;
                imageHeight = imageSenderScript.scaledHeight;
            }
            else
            {
                imageWidth = imageSenderScript.scaledWidth;
                imageHeight = imageSenderScript.scaledHeight;
            }
            Debug.Log($"✅ AR 좌표계팅 완료: 기준 해상도 {imageWidth} x {imageHeight}");
        }
        else
        {
            Debug.LogError("❌ ARObjectPlacer에 ARImageSender가 연결되지 않았습니다!");
        }
    }

    public void OnDataReceived(string jsonString)
    {
        try
        {
            DetectionResponse response = JsonUtility.FromJson<DetectionResponse>(jsonString);

            if (response != null && response.status == "success" && response.results != null)
            {
                string displayString = ""; // 화면에 띄울 메시지

                foreach (var result in response.results)
                {
                    float centerX = (result.box[0] + result.box[2]) / 2f;
                    float centerY = (result.box[1] + result.box[3]) / 2f;

                    // UI에 표시할 텍스트 추가 (소수점 1자리까지만 깔끔하게)
                    displayString += $"[{result.label}] X:{centerX:F1}, Y:{centerY:F1}\n";

                    float normX = centerX / imageWidth;
                    float normY = centerY / imageHeight;

                    AIData data = new AIData { label = result.label, normX = normX, normY = normY };
                    lock (dataQueue)
                    {
                        dataQueue.Enqueue(data);
                    }
                }

                // 여러 객체가 잡혔을 수도 있으니 완성된 텍스트를 업데이트하고 타이머 3초 세팅
                if (!string.IsNullOrEmpty(displayString))
                {
                    debugMessage = displayString;
                    messageTimer = 3f;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("데이터 분석 실패: " + e.Message + " | 받은 데이터: " + jsonString);
        }
    }

    void Update()
    {
        // [UI 타이머 로직] 3초가 지나면 텍스트 지우기
        if (messageTimer > 0)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0)
            {
                debugMessage = ""; // 텍스트 초기화해서 화면에서 숨김
            }
        }

        // 기존 큐 처리 로직
        if (dataQueue.Count > 0)
        {
            AIData data = null;
            lock (dataQueue)
            {
                data = dataQueue.Dequeue();
            }

            if (data != null)
            {
                PlaceObjectAt(data.normX, data.normY);
            }
        }
    }

    void PlaceObjectAt(float normX, float normY)
    {
        float screenX = normX * Screen.width;
        float screenY = (1.0f - normY) * Screen.height;
        
        Vector2 screenPosition = new Vector2(screenX, screenY);
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        
        if (raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;

            if (spawnedObject == null)
            {
                spawnedObject = Instantiate(objectPrefab, hitPose.position, hitPose.rotation);
                Debug.Log("🎉 AR 물체 최초 생성 완료!");
            }
            else
            {
                spawnedObject.transform.position = Vector3.Lerp(spawnedObject.transform.position, hitPose.position, Time.deltaTime * 10f);
                spawnedObject.transform.rotation = hitPose.rotation;    
            }
        }
    }

    // [UI 출력 로직]
    void OnGUI()
    {
        // 메시지가 비어있으면 아예 그리지 않음
        if (string.IsNullOrEmpty(debugMessage)) return;

        if (style == null)
        {
            style = new GUIStyle();
            style.fontSize = 40; 
            style.normal.textColor = Color.yellow; 
            style.wordWrap = true; 
            style.alignment = TextAnchor.UpperRight; // 우측 상단 정렬!
        }
        
        // 우측 상단 배치를 위한 좌표 계산
        // 화면 오른쪽 끝에서 50px 띄우고, 위에서 50px 띄움
        float boxWidth = 600f;
        float boxHeight = 500f;
        Rect rect = new Rect(Screen.width - boxWidth - 50, 50, boxWidth, boxHeight);

        GUI.Label(rect, debugMessage, style);
    }
}
using UnityEngine;
using System;

[RequireComponent(typeof(AudioSource))]
public class LegoGuideManager : MonoBehaviour
{
    [Header("Audio Clips: Step Guides")]
    public AudioClip startAudio;          // 안녕 우리 같이 레고 만들어 볼까?
    public AudioClip[] stepInstructions;  // 0: 1단계, 1: 2단계, 2: 3단계 설명 음성
    public AudioClip completionAudio;     // 우리 빨간 물고기를 완성했어!

    [Header("Audio Clips: Feedback")]
    public AudioClip successFeedback;     // 잘했어
    public AudioClip failFeedback;        // 잘못 조립한 것 같아 다시 해볼까?
    public AudioClip resetFeedback;       // 우리 처음부터 다시 해볼까?

    private AudioSource _audioSource;
    private int _lastStep = -1;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
    }
    
    // void Start()
    // {
    //     Debug.Log("🔊 오디오 테스트: 시작 음성을 재생합니다.");
    //     
    //     // startAudio에 파일이 잘 들어가 있다면 재생
    //     if (startAudio != null)
    //     {
    //         PlayVoice(startAudio);
    //     }
    //     else
    //     {
    //         Debug.LogWarning("시작 오디오 파일이 인스펙터에 비어있습니다!");
    //     }
    // }

    // 엣지 서버(WebSocket)에서 데이터를 받았을 때 호출할 메인 함수
    public void OnDataReceived(string jsonData)
    {
        // 실제로는 JsonUtility를 써서 파싱해야 함 (간단히 구조체/클래스 정의 필요)
        LegoData data = JsonUtility.FromJson<LegoData>(jsonData);
        ProcessLegoLogic(data);
    }

    private void ProcessLegoLogic(LegoData data)
    {
        // 1. 상태 및 결과 판정에 따른 음성 출력
        switch (data.message)
        {
            case "success":
                PlayVoice(successFeedback);
                // [애니메이션 재생 지점] 성공 이펙트나 다음 단계 미리보기 애니메이션 실행
                break;

            case "fail":
                PlayVoice(failFeedback);
                // [애니메이션 재생 지점] 잘못된 부분 강조 애니메이션
                break;

            case "reset":
                PlayVoice(resetFeedback);
                _lastStep = -1;
                break;

            case "complete":
                PlayVoice(completionAudio);
                // [애니메이션 재생 지점] 전체 완성 축하 애니메이션
                return;
        }

        // 2. 단계별 가이드 음성 출력 (단계가 변경되었거나 다시 가이드해야 할 때)
        if (data.status == "step_start" || data.message == "backtrack")
        {
            PlayStepInstruction(data.current_step);
        }
    }

    private void PlayStepInstruction(int step)
    {
        // 배열 인덱스는 0부터 시작하므로 step-1 사용
        int index = step - 1;
        if (index >= 0 && index < stepInstructions.Length)
        {
            PlayVoice(stepInstructions[index]);
            // [애니메이션 재생 지점] 해당 단계의 조립 방법 애니메이션 실행 (예: 가상 브릭 이동)
            Debug.Log($"{step}단계 조립 가이드 재생 중...");
        }
    }

    private void PlayVoice(AudioClip clip)
    {
        if (clip == null) return;
        
        // 이전 음성이 나오고 있다면 끊고 새 음성 재생
        _audioSource.Stop();
        _audioSource.PlayOneShot(clip);
    }
}

[Serializable]
public class LegoData
{
    public string scenario;
    public string status;
    public int current_step;
    public string message;
}
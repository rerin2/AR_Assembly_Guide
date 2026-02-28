using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class MarkerTrackingController : MonoBehaviour
{
    
    [SerializeField] private ARTrackedImageManager arTrackedImageManager;
    [SerializeField] private GameObject _legoAnchorPrefab; // 마커 위에 띄울 앵커(부모) 오브젝트
    
    private GameObject _spawnedAnchor;
    
    private void OnEnable()
    {
        arTrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
    }
    private void OnDisable()
    {
        arTrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
    }
    
    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        // 1. 마커가 처음 인식되었을 때
        foreach (var trackedImage in eventArgs.added)
        {
            if (_spawnedAnchor == null)
            {
                // [핵심 변경점] Instantiate 할 때 trackedImage.transform을 부모로 지정!
                _spawnedAnchor = Instantiate(_legoAnchorPrefab, trackedImage.transform);
                
                // 부모 기준 로컬 좌표와 회전을 0으로 초기화 (마커와 완벽히 겹치게 설정)
                _spawnedAnchor.transform.localPosition = Vector3.zero;
                _spawnedAnchor.transform.localRotation = Quaternion.identity;
                
                Debug.Log($"{trackedImage.referenceImage.name} 마커 인식 완료! 마커에 오브젝트 부착됨.");
            }
        }
        
        // 2. 마커의 상태가 업데이트될 때
        foreach (var trackedImage in eventArgs.updated)
        {
            if (_spawnedAnchor != null)
            {
                // 위치/회전을 수동으로 맞출 필요가 없어졌습니다. (부모가 움직이면 자식은 알아서 따라감)
                
                // 마커가 카메라 밖으로 나가거나 추적을 놓치면 오브젝트를 숨기고, 다시 찾으면 보여줌
                if (trackedImage.trackingState == TrackingState.Tracking)
                {
                    _spawnedAnchor.SetActive(true);
                }
                else
                {
                    _spawnedAnchor.SetActive(false);
                }
            }
        }
    }
    
}

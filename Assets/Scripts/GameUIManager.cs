using TMPro;
using UnityEngine;

public class GameUIManager : MonoBehaviour
{
    [Header("References")]
    // 플레이어와 제스처 컨트롤러 참조
    public PlayerController playerController;
    public GestureController gestureController;
    
    [Header("UI Elements")]
    // UI 텍스트 요소들
    public TextMeshProUGUI rollText;
    public TextMeshProUGUI yawText;
    public TextMeshProUGUI pitchText;
    public TextMeshProUGUI fingerCountText;
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI jumpStatusText; // 점프 상태 표시를 위한 새 텍스트 요소
    
    // 업데이트 주기 제어용 변수
    private float updateInterval = 0.1f;
    private float timeSinceLastUpdate = 0f;

    void Start()
    {
        // 참조가 없으면 자동으로 찾기
        if (playerController == null)
            playerController = FindObjectOfType<PlayerController>();
            
        if (gestureController == null)
            gestureController = FindObjectOfType<GestureController>();
            
        // jumpStatusText가 null인 경우 동적으로 생성
        if (jumpStatusText == null)
        {
            CreateJumpStatusText();
        }
            
        // 초기 텍스트 설정
        UpdateUITexts();
    }
    
    // 점프 상태 텍스트 동적 생성
    private void CreateJumpStatusText()
    {
        // 이미 speedText가 있다면 그 아래에 배치
        if (speedText != null && speedText.transform.parent != null)
        {
            GameObject jumpTextObj = new GameObject("JumpStatusText");
            jumpTextObj.transform.SetParent(speedText.transform.parent, false);
            
            // UI 요소 추가
            jumpStatusText = jumpTextObj.AddComponent<TextMeshProUGUI>();
            
            // 스타일 설정
            jumpStatusText.fontSize = speedText.fontSize;
            jumpStatusText.color = Color.green;
            jumpStatusText.fontStyle = FontStyles.Bold;
            
            // 위치 설정 (speedText 기준 아래쪽)
            RectTransform speedRT = speedText.GetComponent<RectTransform>();
            RectTransform jumpRT = jumpStatusText.GetComponent<RectTransform>();
            
            if (speedRT != null && jumpRT != null)
            {
                jumpRT.anchorMin = speedRT.anchorMin;
                jumpRT.anchorMax = speedRT.anchorMax;
                jumpRT.pivot = speedRT.pivot;
                jumpRT.sizeDelta = speedRT.sizeDelta;
                jumpRT.anchoredPosition = new Vector2(
                    speedRT.anchoredPosition.x,
                    speedRT.anchoredPosition.y - speedRT.sizeDelta.y - 10f);
            }
            
            // 초기 상태 설정
            jumpStatusText.text = "";
        }
    }

    void Update()
    {
        // 일정 시간마다 UI 업데이트 (매 프레임마다 하지 않아도 됨)
        timeSinceLastUpdate += Time.deltaTime;
        if (timeSinceLastUpdate >= updateInterval)
        {
            UpdateUITexts();
            timeSinceLastUpdate = 0f;
        }
    }
    
    void UpdateUITexts()
    {
        if (playerController != null)
        {
            // 플레이어 컨트롤러의 Roll 값 표시
            if (rollText != null)
                rollText.text = $"Roll: {playerController.currentRoll:F1}°";
                
            // 플레이어 컨트롤러의 Yaw 값 표시
            if (yawText != null)
                yawText.text = $"Yaw: {playerController.currentYaw:F1}°";

            if (pitchText != null)
                pitchText.text = $"Pitch: {playerController.currentPitch:F1}°";
                
            // 현재 속도 표시
            if (speedText != null)
                speedText.text = $"Speed: {playerController.CalculateSpeedFromRoll():F2}";
        }
        
        if (gestureController != null)
        {
            // 제스처 컨트롤러의 손가락 개수 표시 (손가락 개수가 -1이면 5로 표시)
            if (fingerCountText != null)
                fingerCountText.text = $"Fingers: {(gestureController.LastFingerCount == -1 ? 4 : gestureController.LastFingerCount)}";
            
            // 점프 상태 표시 - 높이 정보 추가
            if (jumpStatusText != null)
            {
                // 점프 중일 때 "JUMP: X.X" 형식으로 표시
                if (gestureController.isJumpInProgress)
                {
                    // 점프 높이 가져오기
                    float jumpHeight = gestureController.GetJumpHeight();
                    
                    // 점프 상태와 높이 표시
                    jumpStatusText.text = $"JUMP: {jumpHeight:F1}";
                    
                }
                else
                {
                    jumpStatusText.text = "";
                }
            }
        }
    }
}
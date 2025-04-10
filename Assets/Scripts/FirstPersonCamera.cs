using UnityEngine;

public class FirstPersonCamera : MonoBehaviour
{
    [Header("카메라 설정")]
    [Tooltip("플레이어 캐릭터 (따라갈 대상)")]
    public Transform playerTransform;
    
    [Tooltip("카메라의 Y축 오프셋 (머리 위치)")]
    public float heightOffset = 1.6f;
    
    [Tooltip("카메라의 Z축 오프셋 (앞뒤 위치)")]
    public float forwardOffset = 0.1f;
    
    [Header("카메라 부드러움 설정")]
    [Tooltip("위치 이동의 부드러움 (낮을수록 더 부드러움)")]
    public float positionSmoothness = 10f;
    
    [Tooltip("회전 이동의 부드러움 (낮을수록 더 부드러움)")]
    public float rotationSmoothness = 5f;
    
    [Header("점프 설정")]
    [Tooltip("점프 중 카메라 Y축 추가 오프셋")]
    public float jumpBounceMagnitude = 0.2f;
    
    [Tooltip("카메라 흔들림 감소 계수 (높을수록 흔들림 감소)")]
    public float dampingFactor = 0.2f;
    
    // 내부 변수
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float cameraVelocityY = 0f;
    private PlayerController playerController;
    private GestureController gestureController;
    private Vector3 lastPlayerPosition;
    private float verticalVelocity = 0f;
    
    void Start()
    {
        // 플레이어 컨트롤러 찾기
        if (playerTransform != null)
        {
            playerController = playerTransform.GetComponent<PlayerController>();
            gestureController = FindObjectOfType<GestureController>();
        }
        
        // 초기 위치 및 회전 설정
        if (playerTransform != null)
        {
            transform.position = CalculateTargetPosition();
            transform.rotation = playerTransform.rotation;
            lastPlayerPosition = playerTransform.position;
        }
        
    }
    
    void LateUpdate()
    {
        if (playerTransform == null)
            return;
        
        // 플레이어의 현재 상태 확인
        bool isJumping = (gestureController != null && gestureController.isJumpInProgress);
        bool isGrounded = (playerController != null && playerController.IsGrounded());
        
        // 목표 위치 계산
        targetPosition = CalculateTargetPosition();
        
        // 플레이어 이동 속도 계산
        Vector3 playerVelocity = (playerTransform.position - lastPlayerPosition) / Time.deltaTime;
        lastPlayerPosition = playerTransform.position;
        
        // 점프 중이거나 착지 시 카메라 바운스 효과 적용
        if (isJumping || !isGrounded)
        {
            // 플레이어의 Y축 속도 사용
            verticalVelocity = playerVelocity.y;
            
            // 바운스 효과의 감쇠
            cameraVelocityY = Mathf.Lerp(cameraVelocityY, verticalVelocity * jumpBounceMagnitude, Time.deltaTime * dampingFactor);
            
            // Y축 오프셋 추가
            targetPosition.y += cameraVelocityY;
        }
        else
        {
            // 착지 후 점차 바운스 효과 감소
            cameraVelocityY = Mathf.Lerp(cameraVelocityY, 0f, Time.deltaTime * dampingFactor * 3f);
            targetPosition.y += cameraVelocityY;
        }
        
        // 목표 회전 계산 (플레이어 방향 따라가기)
        targetRotation = playerTransform.rotation;
        
        // 부드러운 이동과 회전 적용
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * positionSmoothness);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothness);
    }
    
    // 카메라의 목표 위치 계산
    private Vector3 CalculateTargetPosition()
    {
        if (playerTransform == null)
            return transform.position;
        
        // 플레이어 위치에서 높이 오프셋 적용
        Vector3 position = playerTransform.position;
        position.y += heightOffset;
        
        // 플레이어 전방 방향으로 약간 오프셋 적용 (눈 위치)
        position += playerTransform.forward * forwardOffset;
        
        return position;
    }
    
    // 개발 중 디버그용 시각화
    void OnDrawGizmosSelected()
    {
        if (playerTransform == null)
            return;
        
        Gizmos.color = Color.green;
        
        // 카메라 위치 시각화
        Vector3 targetPos = playerTransform.position + Vector3.up * heightOffset 
                           + playerTransform.forward * forwardOffset;
        
        Gizmos.DrawWireSphere(targetPos, 0.1f);
        Gizmos.DrawLine(playerTransform.position, targetPos);
    }
}
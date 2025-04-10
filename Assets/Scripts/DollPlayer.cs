using UnityEngine;
using System.Collections.Concurrent;
using System;

public class PlayerController : MonoBehaviour
{
    [Header("Jump Settings")]
    [Tooltip("기본 점프 힘. 이 값에 multiplier가 곱해져 최종 점프 힘이 결정됩니다.")]
    public float baseJumpForce = 1.0f;
    public Rigidbody rb; // 인스펙터에서 할당하거나 Awake()에서 GetComponent<Rigidbody>()로 설정

    [Header("Movement Settings")]
    [Tooltip("아바타의 이동 속도.")]
    public float Speed = 1.3f;
    [Tooltip("원형 이동 시의 반지름.")]
    public float circleRadius = 150.0f;
    [Tooltip("회전 속도 (라디안/초).")]
    public float rotationSpeed = 0.8f;
    [Tooltip("회전 시 부드러운 보간을 위한 계수.")]
    public float rotationSmoothness = 5f;

    [Header("Gyroscope Settings")]
    [Tooltip("자이로 Z 입력 임계값. 이 값보다 작으면 노이즈로 간주합니다.")]
    public float gyroZThreshold = 0.08f;
    [Tooltip("자이로 Z 입력 스무딩 계수 (0~1, 높을수록 스무딩 효과 큼).")]
    public float gyroSmoothness = 0.5f;

    [Header("State & Timer Settings")]
    [Tooltip("원형 상태로 전환되기 전에 기다리는 시간.")]
    public float circleStateGracePeriod = 1.0f;
    [Tooltip("직진 상태 유지 최대 시간. 이 시간 후 강제 원형 이동 전환.")]
    public float forcedStraightDuration = 1.0f;

    [Header("Debounce Timers (Private)")]
    [SerializeField, Tooltip("중립 상태로 판단하기 위한 유지 시간 카운트.")]
    private float neutralCountdown = 0f;
    [SerializeField, Tooltip("방향 전환 신호가 유지된 시간 카운트.")]
    private float directionCountdown = 0f;
    [SerializeField, Tooltip("방향 전환을 인정하기 위한 최소 중립 시간.")]
    private readonly float requiredNeutralTime = 0.4f;
    [SerializeField, Tooltip("방향 전환 신호를 인정하기 위한 최소 유지 시간.")]
    private readonly float requiredDirectionTime = 0.4f;

    [Header("Watch Yaw Control Settings")]
    [Tooltip("Yaw 각도 제어 사용 여부")]
    public bool useYawControl = true;
    [Tooltip("Yaw 각도 데이터 스무딩 계수 (0~1)")]
    public float yawSmoothness = 0.2f;
    [Tooltip("Yaw 각도 변화 무시 임계값")]
    public float yawNoiseThreshold = 0.5f;
    [Tooltip("회전 방향 변경을 위한 최소 유지 시간")]
    public float yawDirectionThreshold = 0.3f;
    [Tooltip("중립 상태(원형 이동)로 간주하는 Yaw 각도 범위")]
    public float neutralYawThreshold = 24f;
    [Tooltip("직선 이동 중 원형 이동으로 자동 복귀 비활성화")]
    public bool disableAutoReturnToCircle = false; // 새로운 설정 추가

    [Header("Yaw Rotation Speed")]
    [Tooltip("Yaw 기반 기본 회전 속도 (초당 각도)")]
    public float baseYawRotationSpeed = 45f;
    [Tooltip("최대 회전 속도 (초당 각도)")]
    public float maxYawRotationSpeed = 90f;

    [Header("Roll-based Speed Control")]
    [Tooltip("롤(손목각도) 기반 속도 제어 활성화")]
    public bool useRollSpeedControl = true;
    [Tooltip("기본 걷기 속도 (롤 각도가 중립일 때)")]
    public float baseWalkSpeed = 1.3f;
    [Tooltip("최대 걷기 속도 (롤 각도가 최대일 때)")]
    public float maxWalkSpeed = 2.0f;
    [Tooltip("최소 걷기 속도 (롤 각도가 최소일 때)")]
    public float minWalkSpeed = 0.2f;
    [Tooltip("속도 변화에 영향을 주는 롤 각도 임계값")]
    public float rollSpeedThreshold = 15.0f;
    [Tooltip("롤 각도 변화 무시 임계값")]
    public float rollNoiseThreshold = 1.0f;
    [Header("Camera Control")]
    public Camera thirdPersonCamera; // 기존 3인칭 카메라
    public Camera firstPersonCamera; // 1인칭 카메라
    private bool isFirstPerson = false; // 현재 시점 상태
    [Header("Starting Options")]
    [Tooltip("게임 시작 시 자동으로 이동 시작할지 여부")]
    public bool startMovingAutomatically = false;
    private bool hasStartedMoving = false;
    // 기존 private 변수들
    private float angle = 0f;
    private bool wDown;
    private Animator animator;
    public bool isMove = true;
    private Quaternion targetRotation;
    private float circleStateStartTime = 0f;
    private float storedScale = 1f;
    private float currentGyroZ = 0f;
    private float smoothedGyroZ = 0f;
    private float lastDataReceivedTime = 0f;
    private float dataTimeoutDuration = 1.0f; // 1초 동안 데이터가 없으면 리셋
    public bool disableRotationControl = false; // GestureController에서 회전 제어를 비활성화할 때 사용
    public float currentRoll = 0f;
    private float smoothedRoll = 0f;
    private float rollSmoothness = 0.3f;

    private Vector3 straightDirection;

    private float directionMaintainTime = 0f;
    // baseline은 자이로 센서 특성상 0으로 고정
    private float baselineGyroZ = 0f;
    private bool forceCircleAfterTime = true;
    private int lastMovementDirection = 0;
    private bool hasReachedNeutral = false;
    public bool isBackwalkingMode = false;
    // 쿨다운 관련 변수들
    private float cooldownTimer = 0f;
    // 같은 방향으로 연속 입력에 대한 쿨다운
    private float cooldownDuration = 0.4f;
    private bool isInCooldown = false;
    private bool isSlowingDown = false;
    private float slowingDownStartTime = 0f;
    private float slowingDownDuration = 0.6f;
    public enum MovementState
    {
        Circle,
        Straight
    }
    public MovementState currentState { get; private set; } = MovementState.Circle;

    private float lastGyroZMagnitude = 0f;
    // 스레드 안전 큐 (이벤트에서 수신한 값을 저장)
    private ConcurrentQueue<float> gyroQueue = new ConcurrentQueue<float>();

    // 새로운 GyroY 관련 변수 (DOT 센서용)
    private float currentGyroY = 0f;
    private float smoothedGyroY = 0f;
    public float groundCheckDistance = 1.1f;
    private float yawStableTimer = 0f;
    private float confirmedYaw = 0f; // 확정된 yaw 값
    private const float yawStableHoldTime = 0.1f;
    private const float yawStableThreshold = 0.5f; // 두 값의 차이가 이보다 작으면 "안정"으로 간주

    // Yaw 관련 내부 변수
    public float currentYaw = 0f;
    private float smoothedYaw = 0f;
    private float lastYaw = 0f;
    private float yawDirectionTimer = 0f;
    private bool isYawDirectionActive = false;
    private Vector3 yawBasedDirection = Vector3.zero;
    private YawRotationState currentYawState = YawRotationState.Neutral;
    private float currentYawIntensity = 0f;
    private ConcurrentQueue<float> yawQueue = new ConcurrentQueue<float>();
    private bool isCollectingYaw = false;
    private float yawCollectStartTime = 0f;
    private const float yawCollectDuration = 0.2f;
    private float maxAbsYaw = 0f;
    private float lastYawUpdateTime = 0f;     // 마지막 Yaw 강도 업데이트 시간
    private float yawIntensityUpdateCooldown = 0.5f; // 강도 업데이트 쿨다운
    private bool yawDirectionOverride = false; // 방향 강제 유지 플래그
    private float yawNeutralTimer = 0f;
    private const float yawNeutralHoldTime = 0.2f; // 중립 상태가 유지되어야 하는 최소 시간
    private bool isRunning = false; // 달리기 애니메이션 상태 추적

    
    // Yaw 회전 상태 열거형
    private enum YawRotationState
    {
        Left,
        Neutral,
        Right
    }

    public bool IsGrounded()
    {
        // 여러 지점에서 레이캐스트를 발사하여 더 정확하게 체크
        Vector3 center = transform.position;
        Vector3 forward = center + transform.forward * 0.3f;
        Vector3 backward = center - transform.forward * 0.3f;
        Vector3 left = center - transform.right * 0.3f;
        Vector3 right = center + transform.right * 0.3f;

        // 레이캐스트로 지면 체크
        return Physics.Raycast(center, Vector3.down, groundCheckDistance) ||
            Physics.Raycast(forward, Vector3.down, groundCheckDistance) ||
            Physics.Raycast(backward, Vector3.down, groundCheckDistance) ||
            Physics.Raycast(left, Vector3.down, groundCheckDistance) ||
            Physics.Raycast(right, Vector3.down, groundCheckDistance);
    }
    public Vector3 GetCurrentMoveDirection()
    {
        return straightDirection.normalized;
    }
    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        targetRotation = transform.rotation;
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
        }
        // 점프 전까지 물리 계산을 제어하기 위해 isKinematic을 사용 (필요에 따라)
        rb.isKinematic = true;
    }
    // 현재 원 회전 각도를 반환하는 메소드
    public float GetCurrentAngle()
    {
        return angle;
    }
    // 카메라 전환 메서드
    public void ToggleCamera()
    {

        if (firstPersonCamera != null && thirdPersonCamera != null)
        {
            firstPersonCamera.enabled = isFirstPerson;
            thirdPersonCamera.enabled = !isFirstPerson;
            
            Debug.Log($"카메라 전환: {(isFirstPerson ? "1인칭" : "3인칭")} 시점");
        }
    }

    public void ForceRotation(Quaternion rotation)
    {
        transform.rotation = rotation;
        targetRotation = rotation;
    }
    // 속도를 서서히 줄이기 시작하는 메서드
    public void StartSlowingDown()
    {
        isSlowingDown = true;
        slowingDownStartTime = Time.time;
    }
    // 이동 방향 강제 설정 메서드
    public void SetMoveDirection(Vector3 direction)
    {
        straightDirection = direction.normalized;
    }
    void Start()
    {
        // 시작부터 두 카메라 모두 활성화
        if (firstPersonCamera != null && thirdPersonCamera != null)
        {
            firstPersonCamera.enabled = true;
            thirdPersonCamera.enabled = true;
        }
        
        // 시작할 때 항상 정지 상태로 설정
        isMove = false;
        hasStartedMoving = false;
        
        // 애니메이션 시작 시 정지 상태로 설정 (추가된 부분)
        if (animator != null)
        {
            animator.speed = 0; // 애니메이션 재생 속도를 0으로 설정하여 정지
        }
        
        Debug.Log("스페이스바를 눌러 캐릭터 이동을 시작하세요.");
        
        // 초기화 - 데이터는 아직 구독하지 않음
        currentGyroZ = 0f;
        baselineGyroZ = 0f; // 초기 기준은 0으로 고정
    }

    // 데이터 수신 시작 메서드 추가
    private void StartDataSubscription()
    {
        // Roll 데이터 구독 추가
        DataTransBehavior.OnNewRoll += (value) => {
            UpdateRollData(value);
            //Debug.Log($"Roll 값 수신: {value:F1}°");
        };
        
        // Watch의 Yaw 데이터 구독
        DataTransBehavior.OnNewYaw += (value) => {
            yawQueue.Enqueue(value);
            //Debug.Log($"Yaw 값 수신: {value}");
        };
        
        // 핸드 제스처 데이터 구독 (필요한 경우)
        // DataTransBehavior.OnNewHandGesture += (fingerCount, palmOrientation) => {
        //     // 손가락 데이터 처리
        // };
        
        Debug.Log("웨어러블 센서 데이터 수신을 시작합니다.");
    }
    // 추가: 주기적으로 원형 이동 시도 메소드
    public void TryCircleMovement()
    {
        // 이미 원형 이동 중이면 무시
        if (currentState == MovementState.Circle)
            return;

        directionMaintainTime += Time.deltaTime;

        // 일정 시간 이상 직진하고 있으면 원형 이동으로 전환
        if (forceCircleAfterTime && directionMaintainTime >= forcedStraightDuration)
        {
            SwitchToCircleMovement();
        }
    }
    void Update()
    {
        // 스페이스바 입력 감지 - 아직 이동을 시작하지 않았을 때
        if (!hasStartedMoving && Input.GetKeyDown(KeyCode.Space))
        {
            isMove = true;
            hasStartedMoving = true;

            // 애니메이션 재생 시작 (추가된 부분)
            if (animator != null)
            {
                animator.speed = 1.0f; // 일반 속도로 애니메이션 재생 시작
            }

            // 데이터 수신 시작
            StartDataSubscription();
            Debug.Log("캐릭터 이동을 시작합니다!");
        }
        // 이미 시작한 상태에서 스페이스바를 누르면 일시정지/재개
        else if (hasStartedMoving && Input.GetKeyDown(KeyCode.Space))
        {
            isMove = !isMove; // 이동 상태 토글
            
            // 애니메이션 일시정지/재개 토글 (추가된 부분)
            if (animator != null)
            {
                // isMove가 true면 애니메이션 재생, false면 정지
                animator.speed = isMove ? 1.0f : 0.0f;
            }
            
            Debug.Log(isMove ? "이동을 재개합니다." : "이동을 일시정지합니다.");
        }

        if (Time.time - lastDataReceivedTime > dataTimeoutDuration)
        {
            // 일정 시간 데이터가 없으면 gyro 값 리셋
            currentGyroZ = 0f;
            smoothedGyroZ = 0f;
        }
        // 메인 스레드에서 큐에 저장한 자이로 데이터를 처리
        while (gyroQueue.TryDequeue(out float newValue))
        {
            UpdateGyroData(newValue);
        }
        // Yaw 큐에서 데이터 처리
        bool processedYaw = false;
        while (yawQueue.TryDequeue(out float newYawValue))
        {
            UpdateYawData(newYawValue);
            processedYaw = true;
        }
        // 중요: 큐에서 처리된 Yaw가 없더라도 매 프레임 중립 상태 체크
        if (!processedYaw && useYawControl && Time.time - lastYawUpdateTime < 1.0f)
        {
            ProcessNeutralYaw();
        }
        wDown = Input.GetButton("Walk");

        switch (currentState)
        {
            case MovementState.Circle:
                if (isMove) // isMove일 때만 원 운동 처리
                    MoveInCircle();
                break;
            case MovementState.Straight:
                if (isMove) // isMove일 때만 직선 이동 처리
                    MoveInStraightLine();
                break;
        }
        if (Input.GetKeyDown(KeyCode.C))
        {
            ToggleCamera();
        }
         // Update 메서드 내의 이동 처리 부분 수정
        if (isMove)
        {
            // 점프 중인지 확인
            GestureController gestureController = FindObjectOfType<GestureController>();
            bool isJumping = (gestureController != null && gestureController.isJumpInProgress);
            
            // 기본 이동 속도 설정
            float currentSpeedMultiplier = 0.8f; // 기본 속도 계수
            
            // 점프 중에는 기본 속도 사용, 아니면 롤 기반 속도 계산
            float rollBasedSpeed;
            if (isJumping)
            {
                rollBasedSpeed = baseWalkSpeed; // 점프 중에는 기본 속도 사용
            }
            else 
            {
                rollBasedSpeed = CalculateSpeedFromRoll(); // 백워킹에서도 롤 기반 속도 적용
            }
            
            // 감속 중일 때는 속도를 점진적으로 감소
            if (isSlowingDown)
            {
                float slowingProgress = (Time.time - slowingDownStartTime) / slowingDownDuration;
                if (slowingProgress >= 1.0f)
                {
                    isSlowingDown = false;
                    currentSpeedMultiplier = 0f; // 완전 정지
                }
                else
                {
                    currentSpeedMultiplier *= (1.0f - slowingProgress);
                }
            }

            // 백워킹 모드에서의 이동 방향 설정
            Vector3 moveDirection = straightDirection;
            if (isBackwalkingMode && currentState == MovementState.Straight)
            {
                moveDirection = -transform.forward;
            }

            // 원 회전에 대한 반경 스케일 계수 추가
            float circleScaleFactor = 1.0f;
            if (currentState == MovementState.Circle)
            {
                // 원하는 반경으로 조정 (circleRadius 값을 직접 적용)
                // 값이 클수록 원 회전의 반경이 커짐
                circleScaleFactor = circleRadius / 100.0f; // 100은 기본값
            }

            // 계산된 속도로 이동 (스케일 계수 적용)
            transform.position += moveDirection * rollBasedSpeed * currentSpeedMultiplier * circleScaleFactor * Time.deltaTime;
        }
        // 추가: GyroY값에 따른 스케일 조정
        float computedScale = 1f; // 기본 스케일은 1로 시작

        if (currentGyroY > 100f)
        {
            // currentGyroY가 100에서 +600이면 effectiveValue는 0 ~ 500
            float effectiveValue = Mathf.Clamp(currentGyroY - 100f, 0f, 500f);
            // 선형 변화: 1f에서 0.01f까지 감소
            computedScale = 1f - 0.99f * (effectiveValue / 500f);
        }
        else if (currentGyroY < -100f)
        {
            float effectiveValue = Mathf.Clamp(Mathf.Abs(currentGyroY) - 100f, 0f, 500f);
            // 선형 변화: 1f에서 3f까지 증가
            computedScale = 1f + 2f * (effectiveValue / 500f);
        }
        else
        {
            // 중립 상태 (-100 ~ 100 사이)에서는 항상 기본 스케일 (1.0f) 사용
            computedScale = 1.0f;
        }

        // 최소 0.01배, 최대 3배 유지
        computedScale = Mathf.Clamp(computedScale, 0.01f, 3f);

        // 작은 변화 무시 설정
        float scaleChangeThreshold = 0.05f; // 5% 이하 변화는 무시 (이 값은 조절 가능)
        float scaleDifference = Mathf.Abs(computedScale - transform.localScale.x);

        // 일정 임계값보다 변화가 큰 경우에만 스케일 업데이트
        if (scaleDifference > scaleChangeThreshold)
        {
            // 부드럽게 변화하도록 스무딩 처리 (스무딩 계수 2f 예시)
            float smoothScale = Mathf.Lerp(transform.localScale.x, computedScale, 2f * Time.deltaTime);
            transform.localScale = Vector3.one * smoothScale;
        }
        // 애니메이션 속도 업데이트
        UpdateAnimationSpeed(); 
    }

    // UpdateAnimationSpeed 메서드 수정
    private void UpdateAnimationSpeed()
    {
        if (animator != null && useRollSpeedControl)
        {
            // 점프 중인지 확인 - 점프 중이면 롤 각도 기반 애니메이션 변경 무시
            GestureController gestureController = FindObjectOfType<GestureController>();
            bool isJumping = (gestureController != null && gestureController.isJumpInProgress);
            
            if (isJumping)
            {
                // 점프 중에는 기본 애니메이션 속도 사용 (1.0으로 고정)
                animator.speed = 1.0f;
                return; // 여기서 리턴하여 아래 코드를 실행하지 않음
            }
            
            // 백워킹 모드일 때 애니메이션 처리
            if (isBackwalkingMode)
            {
                // 백워킹에서는 백런 임계치가 90도
                float backRunThreshold = 80.0f;
                
                // 애니메이션 속도 계산 (롤 기반)
                float speedRatio = CalculateSpeedFromRoll() / baseWalkSpeed;
                speedRatio = Mathf.Clamp(speedRatio, 0.5f, 1.7f);
                animator.speed = speedRatio;
                
                // 백워킹에서 백런 전환은 GestureController에서 처리하므로 여기서는 속도만 조절
                // (애니메이션 전환은 MaintainBackwalkingState에서 담당)
            }
            else
            {
                // 기존 코드: 일반 모드에서 Roll 각도에 따른 애니메이션 전환
                float runThreshold = 80.0f;
                
                if (currentRoll >= runThreshold)
                {
                    // 이미 달리기 상태가 아니라면 애니메이션 전환
                    if (!isRunning)
                    {
                        // Run 애니메이션으로 전환
                        animator.SetTrigger("RunTrigger");
                        isRunning = true;
                        Debug.Log($"Roll 각도 {currentRoll:F1}° 감지: 달리기 모드로 전환");
                    }
                    
                    // 달리기 애니메이션의 속도는 항상 1.0으로 고정 (변경하지 않음)
                    animator.speed = 1.0f;
                }
                else
                {
                    // 달리기 상태였다면 걷기로 전환
                    if (isRunning)
                    {
                        // Walk 애니메이션으로 부드럽게 전환
                        animator.CrossFade("Walk", 0.35f);
                        isRunning = false;
                        Debug.Log($"Roll 각도 {currentRoll:F1}° 감지: 걷기 모드로 전환");
                    }
                    
                    // 걷기 애니메이션의 속도 조절 (기존과 동일)
                    float speedRatio = CalculateSpeedFromRoll() / baseWalkSpeed;
                    speedRatio = Mathf.Clamp(speedRatio, 0.5f, 1.7f);
                    animator.speed = speedRatio;
                }
            }
        }
    }

    public void Jump(float multiplier, float gyroYValue)
    {
        if (!IsGrounded())
        {
            Debug.Log("점프 불가: 땅에 닿아있지 않음");
            return;
        }

        // 물리 속성 확실히 설정
        rb.isKinematic = false;
        rb.useGravity = true;

        // 고정된 최소/최대 높이 설정 (아래 수치를 사용하면 현재 코드에서 1~4f 사이의 피크높이가 관측댐)
        float minJumpHeight = 1.0f;  // -60에서의 높이
        float maxJumpHeight = 4.0f;  // -300에서의 높이 (원하는 최대값으로 조정)

        // 자이로 값에 따른 높이 계산 (더 정밀한 제어)
        float jumpHeight;
        float clampedGyroY = Mathf.Clamp(gyroYValue, -300f, -60f);
        float t = Mathf.InverseLerp(-60f, -300f, clampedGyroY);

        // 선형 보간으로 높이 결정 (baseJumpForce와 multiplier는 무시)
        jumpHeight = Mathf.Lerp(minJumpHeight, maxJumpHeight, t);

        // 로그 추가
        Debug.Log($"점프 높이 계산: 자이로Y={gyroYValue:F2}, t={t:F2}, 최종 높이={jumpHeight:F2}");

        // 중력을 고려한 점프 속도 계산 (같은 공식 사용)
        float jumpSpeed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);

        // velocity 직접 설정
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpSpeed, rb.linearVelocity.z);

        Debug.Log($"점프 실행: 높이 {jumpHeight:F2}, 속도 {jumpSpeed:F2}, 중력 {Physics.gravity.y:F2}");
    }
    private void UpdateRollData(float newRollValue)
    {
        // 이전 값 저장
        float previousRoll = currentRoll;

        // 스무딩 적용
        smoothedRoll = Mathf.Lerp(smoothedRoll, newRollValue, rollSmoothness);
        
        // 노이즈 제거
        if (Mathf.Abs(smoothedRoll - previousRoll) < rollNoiseThreshold)
        {
            // 값 유지
            smoothedRoll = previousRoll;
        }

        currentRoll = smoothedRoll;

        // 매우 작은 값은 0으로 처리
        if (Mathf.Abs(currentRoll) < 0.5f)
        {
            currentRoll = 0f;
        }
        this.currentRoll = smoothedRoll;
        // Roll 각도가 변경되면 애니메이션 상태도 업데이트
        // 이전 값과 현재 값이 달리기 임계치(90도)를 기준으로 달라졌다면 업데이트
        float runThreshold = 90.0f;
        bool wasOverThreshold = previousRoll >= runThreshold;
        bool isOverThreshold = currentRoll >= runThreshold;
        
        if (wasOverThreshold != isOverThreshold)
        {
            // 임계값을 넘었거나 내려갔을 때 애니메이션 업데이트 
            UpdateAnimationSpeed();
        }
        
        // 현재 Roll 각도와 속도를 로그로 출력 (디버깅)
        //Debug.Log($"Roll 각도 업데이트: {currentRoll:F1}°, 계산된 속도: {CalculateSpeedFromRoll():F2}, 달리기: {isOverThreshold}");
    }

    // Roll 각도에 따른 속도 계산 메서드 수정
    public float CalculateSpeedFromRoll()
    {
        // GestureController가 점프 중이면 기본 속도 반환 (Roll 각도 무시)
        GestureController gestureController = FindObjectOfType<GestureController>();
        if (gestureController != null && gestureController.isJumpInProgress)
        {
            return baseWalkSpeed; // 점프 중에는 기본 속도 사용
        }

        if (!useRollSpeedControl)
            return baseWalkSpeed;

        // 백워킹 모드에서는 다른 각도-속도 매핑 사용
        if (isBackwalkingMode)
        {
            // 백워킹용 각도-속도 매핑 (수정된 부분)
            // Roll +90도 이상일 때 최대 속도, -80도 이하일 때 최소 속도
            float[] backwalkRollAngles = { -80f,-50f,-20f, 0f, 30f, 60f, 80f };
            float[] backwalkSpeeds = { 0.2f,0.5f,1.0f, 1.3f, 1.7f, 1.9f, 2.0f };
            
            // 현재 롤 각도
            float roll = currentRoll;
            
            // 롤 각도가 최소값보다 작으면 최소 속도 반환
            if (roll <= backwalkRollAngles[0])
                return backwalkSpeeds[0];
            
            // 롤 각도가 최대값보다 크면 최대 속도 반환 (백런 모드)
            if (roll >= backwalkRollAngles[backwalkRollAngles.Length - 1])
                return backwalkSpeeds[backwalkSpeeds.Length - 1];
            
            // 현재 롤 각도가 속한 구간 찾기
            int i = 0;
            while (i < backwalkRollAngles.Length - 1 && roll > backwalkRollAngles[i + 1])
                i++;
            
            // 해당 구간에서 선형 보간으로 속도 계산
            float t = (roll - backwalkRollAngles[i]) / (backwalkRollAngles[i + 1] - backwalkRollAngles[i]);
            return Mathf.Lerp(backwalkSpeeds[i], backwalkSpeeds[i + 1], t);
        }
        else
        {
            // 기존 일반 모드 각도-속도 매핑 (기존 코드 유지)
            float[] rollAngles = { -80f, -50f, -20f, 0f, 30f, 60f, 80f };
            float[] speeds = { 0.2f, 0.5f, 1.0f, 1.3f, 1.7f, 1.9f, 2.0f };
            
            // 현재 롤 각도
            float roll = currentRoll;
            
            // 롤 각도가 최소값보다 작으면 최소 속도 반환
            if (roll <= rollAngles[0])
                return speeds[0];
            
            // 롤 각도가 최대값보다 크면 최대 속도 반환
            if (roll >= rollAngles[rollAngles.Length - 1])
                return speeds[speeds.Length - 1];
            
            // 현재 롤 각도가 속한 구간 찾기
            int i = 0;
            while (i < rollAngles.Length - 1 && roll > rollAngles[i + 1])
                i++;
            
            // 해당 구간에서 선형 보간으로 속도 계산
            float t = (roll - rollAngles[i]) / (rollAngles[i + 1] - rollAngles[i]);
            return Mathf.Lerp(speeds[i], speeds[i + 1], t);
        }
    }

    // 특정 높이로 점프하기 위한 새로운 메서드 추가
    public void JumpWithHeight(float targetHeight)
    {
        if (!IsGrounded())
        {
            Debug.Log("점프 불가: 땅에 닿아있지 않음");
            return;
        }

        // 물리 속성 설정
        rb.isKinematic = false;
        rb.useGravity = true;

        // 원하는 높이에 도달하기 위한 초기 속도 계산
        // 공식: v = √(2gh), 여기서 g는 중력가속도, h는 목표 높이
        float jumpSpeed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * targetHeight);

        // velocity 직접 설정
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpSpeed, rb.linearVelocity.z);

        //Debug.Log($"높이 기반 점프 실행: 목표 높이 {targetHeight:F2}, 초기 속도 {jumpSpeed:F2}");
    }

    // 점프 후 안정화를 위한 코루틴 추가
    private System.Collections.IEnumerator ResetGyroAfterJump()
    {
        // 0.5초 후에 한 번 더 초기화(점프 중 혹시 들어올 수 있는 자이로 값을 무시)
        yield return new WaitForSeconds(0.5f);
        currentGyroY = 0f;
        smoothedGyroY = 0f;
    }
    // GyroZ 데이터 처리 (기존)
    private void UpdateGyroData(float newGyroValue)
    {
        lastDataReceivedTime = Time.time; // 데이터 수신 시간 업데이트

        smoothedGyroZ = Mathf.Lerp(smoothedGyroZ, newGyroValue, gyroSmoothness);
        currentGyroZ = smoothedGyroZ;

        if (Mathf.Abs(currentGyroZ) < 0.3f)
        {
            currentGyroZ = 0f;
        }

        // 원회전 상태에서만 방향 변화 체크
        if (currentState == MovementState.Circle && !isInCooldown)
        {
            CheckForMovementChange();
        }

        if (isInCooldown)
        {
            cooldownTimer += Time.deltaTime;
            if (cooldownTimer >= cooldownDuration && Mathf.Abs(currentGyroZ) < gyroZThreshold * 0.5f)
            {
                isInCooldown = false;
                cooldownTimer = 0f;
            }
        }
    }
    // GyroY 데이터 처리 (DOT 센서용, 디버깅용)
    private void UpdateGyroYData(float newGyroYValue)
    {
        // 이전 값 저장
        float previousGyroY = currentGyroY;

        // 스무딩 계수는 동일하게 사용
        smoothedGyroY = Mathf.Lerp(smoothedGyroY, newGyroYValue, gyroSmoothness);

        // 노이즈 제거를 위한 더 높은 임계값 적용
        float gyroYNoiseThreshold = 0.5f; // 작은 변화 무시

        // 이전 값과의 차이가 임계값보다 작으면 값 유지
        if (Mathf.Abs(smoothedGyroY - previousGyroY) < gyroYNoiseThreshold)
        {
            // 값 유지 (이전 값 사용)
            smoothedGyroY = previousGyroY;
        }

        currentGyroY = smoothedGyroY;

        // 매우 작은 값은 0으로 처리
        if (Mathf.Abs(currentGyroY) < 0.3f) // 임계값 0.03f에서 0.3f로 증가
        {
            currentGyroY = 0f;
        }

        // DOT 센서 사용 중 방향 전환 방지를 위한 코드 추가
        if (Mathf.Abs(currentGyroY) > 100f) // DOT 센서 활성 상태일 때
        {
            // 일시적으로 방향 전환 기능 비활성화 (쿨다운 활성화)
            isInCooldown = true;
            cooldownTimer = 0f; // 쿨다운 타이머 리셋
        }
    }

    // UpdateYawData 메서드 수정
    private void UpdateYawData(float newYawValue)
    {
        // 이전 Yaw 값 저장
        lastYaw = currentYaw;
        
        // 스무딩 적용 (급격한 변화 방지)
        smoothedYaw = Mathf.Lerp(smoothedYaw, newYawValue, yawSmoothness);
        
        // 노이즈 임계값보다 작은 변화는 무시
        if (Mathf.Abs(smoothedYaw - currentYaw) < yawNoiseThreshold)
            return;
        
        // Yaw 값 업데이트
        currentYaw = smoothedYaw;
        
        // 아주 작은 값은 0으로 처리
        if (Mathf.Abs(currentYaw) < 1.0f)
        {
            currentYaw = 0f;
        }
        this.currentYaw = smoothedYaw;
        // 매 프레임마다 중립 상태 체크 (중요: 여기서 직접 호출)
        ProcessNeutralYaw();
        
        // 중립 범위가 아닐 때만 회전 상태 업데이트
        if (Mathf.Abs(currentYaw) > neutralYawThreshold)
        {
            UpdateYawRotationState(currentYaw);
        }
        
        // 마지막 Yaw 업데이트 시간 기록
        lastYawUpdateTime = Time.time;
    }

    
    // 새로운 메서드: Yaw 상태 리셋
    private void ResetYawState()
    {
        isCollectingYaw = false;
        currentYawState = YawRotationState.Neutral;
        currentYawIntensity = 0f;
        isYawDirectionActive = false;
        yawDirectionTimer = 0f;
        maxAbsYaw = 0f;
        yawDirectionOverride = false;

        // 추가: 중립 상태에서는 yaw 관련 값들을 초기화해서 반영되도록 함
        confirmedYaw = 0f;
        currentYaw = 0f;
        yawStableTimer = 0f;
        
        Debug.Log("[Yaw 상태 초기화] 중립 상태로 복귀");
    }

    // UpdateYawRotationState 메서드 수정 - Yaw 값에 따른 좌우 방향 매핑 수정
    private void UpdateYawRotationState(float yawValue)
    {
        YawRotationState previousState = currentYawState;
        YawRotationState newYawState;
        
        // Yaw 값에 따른 상태 결정 - 백워킹 모드에서는 좌우 반전
        if (Mathf.Abs(yawValue) <= neutralYawThreshold)
        {
            newYawState = YawRotationState.Neutral;
        }
        else if ((yawValue < 0 && !isBackwalkingMode) || (yawValue > 0 && isBackwalkingMode))
        {
            // 일반 모드: 음수 Yaw = 왼쪽
            // 백워킹 모드: 양수 Yaw = 왼쪽 (반전)
            newYawState = YawRotationState.Right;
        }
        else
        {
            // 일반 모드: 양수 Yaw = 오른쪽
            // 백워킹 모드: 음수 Yaw = 오른쪽 (반전)
            newYawState = YawRotationState.Left;
        }
        
        // 상태 변경 감지
        bool stateChanged = newYawState != previousState;
        
        // 방향이 변경되었거나 중립 상태로 돌아왔을 때만 처리
        if (stateChanged)
        {
            if (newYawState == YawRotationState.Neutral)
            {
                // 중립 상태로 돌아왔을 때 처리
                Debug.Log($"[상태 전환] {previousState} → 중립");
                ResetYawState(); // 모든 상태 초기화 (방향 유지 플래그 포함)
            }
            else if (previousState == YawRotationState.Neutral) 
            {
                // 중립 → 방향 전환 (새로운 방향 설정 시작)
                Debug.Log($"[상태 전환] 중립 → {newYawState} (백워킹: {isBackwalkingMode})");
                StartYawCollection(yawValue, newYawState);
            }
            else if (previousState != YawRotationState.Neutral && newYawState != YawRotationState.Neutral)
            {
                // 좌 → 우 또는 우 → 좌로 직접 전환 (중립 없이)
                // 이 경우 방향 강제 전환 허용 (중립을 거치지 않고 즉시 방향 전환)
                Debug.Log($"[방향 직접 전환] {previousState} → {newYawState} (백워킹: {isBackwalkingMode})");
                yawDirectionOverride = false; // 강제 유지 해제
                StartYawCollection(yawValue, newYawState);
            }
        }
        else if (newYawState != YawRotationState.Neutral)
        {
            // 동일한 방향에서 강도 업데이트만 수행
            UpdateYawIntensity(yawValue);
        }
        
        currentYawState = newYawState;
        
        // 회전이 활성화되어 있지 않고, 중립 상태가 아니며, 수집 중이 아닐 때만 타이머 증가
        if (!isYawDirectionActive && !isCollectingYaw && currentYawState != YawRotationState.Neutral)
        {
            yawDirectionTimer += Time.deltaTime;
            
            if (yawDirectionTimer >= yawDirectionThreshold)
            {
                isYawDirectionActive = true;
                ApplyYawDirection(currentYawState == YawRotationState.Left);
            }
        }
    }


    
    // 새로운 메서드: Yaw 수집 시작
    private void StartYawCollection(float yawValue, YawRotationState newState)
    {
        isCollectingYaw = true;
        yawCollectStartTime = Time.time;
        maxAbsYaw = Mathf.Abs(yawValue);
        
        // 방향 변경 시 상태 초기화
        yawDirectionTimer = 0f;
        isYawDirectionActive = false;
        
        Debug.Log($"[Yaw 수집 시작] {newState}, 초기값: {yawValue:F1}° (절대값: {maxAbsYaw:F1}°)");
        
        // 수집 코루틴 재시작
        StopCoroutine("CollectMaxYawValue");
        StartCoroutine(CollectMaxYawValue());
    }
    
    // 새로운 메서드: 같은 방향에서 강도 업데이트
    private void UpdateYawIntensity(float yawValue)
    {
        float absYaw = Mathf.Abs(yawValue);

        // 이전에 저장된 최대값과 현재 값 중 큰 값을 사용 (방향 고정 모드일 때)
        if (isYawDirectionActive && yawDirectionOverride)
        {
            // 방향 고정 모드에서는 이전 최대값보다 현재값이 클 때만 업데이트
            if (absYaw > maxAbsYaw)
            {
                maxAbsYaw = absYaw;
                
                // 최대값이 업데이트되었음을 로그로 표시
                Debug.Log($"[Yaw 값 증가] 최대값 업데이트: {maxAbsYaw:F1}°");
            }
        }
        else
        {
            // 방향 고정 모드가 아니거나 새로 방향이 설정될 때는 현재 값 사용
            maxAbsYaw = absYaw;
        }

        // 강도 계산에 사용할 값 선택 (항상 maxAbsYaw 사용)
        float yawForCalculation = maxAbsYaw;

        // 강도 계산
        float newIntensity;
        if (yawForCalculation <= 25f)
            newIntensity = 0.5f;
        else if (yawForCalculation >= 40f)
            newIntensity = 1.0f;
        else
            newIntensity = 0.5f + 0.5f * ((yawForCalculation - 25f) / 15f);

        // 강도 차이가 충분하면 업데이트
        if (Mathf.Abs(newIntensity - currentYawIntensity) > 0.05f)
        {
            float previousIntensity = currentYawIntensity;
            currentYawIntensity = newIntensity;
            
            // 이미 방향 활성화되어 있다면 새 강도로 즉시 적용
            if (isYawDirectionActive)
            {
                ApplyYawDirection(currentYawState == YawRotationState.Left);
            }
        }
    }
    // ProcessNeutralYaw 메서드 개선 - 인자 제거하고 현재 상태 사용
    private void ProcessNeutralYaw()
    {
        // 중립 범위 여부 확인 (절대값 기준)
        bool isInNeutralRange = Mathf.Abs(currentYaw) <= neutralYawThreshold;
        
        // 디버그 로그 추가 (항상 출력)
        //Debug.Log($"[중립 체크] Yaw: {currentYaw:F2}°, 범위내: {isInNeutralRange}, 타이머: {yawNeutralTimer:F2}/{yawNeutralHoldTime:F2}초");
        
        // 중립 범위 안에 있을 때 타이머 증가
        if (isInNeutralRange)
        {
            yawNeutralTimer += Time.deltaTime;
            
            // 중립 상태가 충분히 유지되면 원형 이동으로 전환
            if (yawNeutralTimer >= yawNeutralHoldTime)
            {
                Debug.Log($"[중립 유지 완료!] {yawNeutralHoldTime}초 이상 중립 상태가 유지되어 원형 이동으로 전환합니다.");
                
                // 상태 초기화
                isYawDirectionActive = false;
                yawDirectionOverride = false;
                currentYawState = YawRotationState.Neutral;
                
                // Yaw 관련 상태 리셋
                ResetYawState();
                
                // 직선 이동 중이고 중립 상태가 지속되면 원형 이동으로 전환
                if (currentState != MovementState.Circle)
                {
                    SwitchToCircleMovement();
                }
            }
        }
        else
        {
            // 중립 범위를 벗어나면 타이머 리셋
            yawNeutralTimer = 0f;
        }
    }
    // CollectMaxYawValue 코루틴 수정
    private System.Collections.IEnumerator CollectMaxYawValue()
    {
        // 중복 처리 방지를 위해 플래그 설정
        bool wasAlreadyActive = isYawDirectionActive;
        
        // 즉시 방향 적용 (대기 없이)
        isCollectingYaw = false;
        isYawDirectionActive = true;
        
        // 단일 방향 적용 (한 번만 실행)
        if (!wasAlreadyActive)
        {
            ApplyYawDirection(currentYawState == YawRotationState.Left);
            Debug.Log($"[방향 즉시 적용] {(currentYawState == YawRotationState.Left ? "왼쪽" : "오른쪽")} 방향, Yaw: {currentYaw:F1}°");
        }
        
        yield break; // 추가 처리 없이 종료
    }


    private void ApplyYawDirection(bool isLeft)
    {
        // 현재 진행 방향 기준 계산
        Vector3 currentForward = transform.forward;
        currentForward.y = 0;
        currentForward.Normalize();

        // 왼쪽/오른쪽 벡터 계산
        Vector3 rightDir = Vector3.Cross(Vector3.up, currentForward).normalized;
        Vector3 leftDir = -rightDir;

        // 회전 각도 계산 - 25도일 때 45도 회전, 40도 이상일 때 90도 회전
        float rotationAngleDegrees;
        
        // 방향 강도에 따른 회전 각도 계산
        if (maxAbsYaw <= 25f)
        {
            rotationAngleDegrees = 45f; // 25도 이하는 45도 회전
        }
        else if (maxAbsYaw >= 40f)
        {
            rotationAngleDegrees = 90f; // 40도 이상일 때 90도 회전
        }
        else
        {
            // 25~40도 사이에서는 45~90도로 선형 증가
            float t = (maxAbsYaw - 25f) / 15f; // 0~1 사이 값으로 정규화
            rotationAngleDegrees = Mathf.Lerp(45f, 90f, t);
        }
        
        // 회전 방향 - 백워킹 모드에서는 방향을 반전
        float finalRotationDegrees;
        if (isBackwalkingMode)
        {
            finalRotationDegrees = isLeft ? -rotationAngleDegrees : rotationAngleDegrees;
        }
        else
        {
            finalRotationDegrees = isLeft ? rotationAngleDegrees : -rotationAngleDegrees;
        }
        
        // 백워킹 모드 전환 감지를 위해 저장된 플래그 추가
        bool wasBackwalkingMode = false;
        bool backwalkingModeChanged = wasBackwalkingMode != isBackwalkingMode;
        wasBackwalkingMode = isBackwalkingMode;
        
        // ---------- 누적 회전 처리를 위한 핵심 변경 부분 시작 ----------
        
        // 이전 방향과 현재 방향이 같은 경우 (누적 회전)
        bool isSameDirection = (currentYawState == YawRotationState.Left && isLeft) || 
                            (currentYawState == YawRotationState.Right && !isLeft);
        
        Vector3 targetDirection;
        
        // 백워킹 모드가 변경된 경우에는 누적 회전을 적용하지 않음
        if (backwalkingModeChanged)
        {
            // 모드 전환 시에는 현재 바라보는 방향을 유지하고 추가 회전 없이 진행
            targetDirection = currentForward;
            yawDirectionOverride = false; // 누적 회전 플래그 리셋
            
            Debug.Log($"[백워킹 모드 전환] 누적 회전 리셋, 현재 방향 유지");
        }
        else if (isSameDirection && yawDirectionOverride)
        {
            // 같은 방향으로 계속 회전하는 경우, 이전 방향을 기준으로 추가 회전
            float previousRotation = Vector3.SignedAngle(straightDirection, currentForward, Vector3.up);
            
            // 회전량 계산 (이전 회전량 + 추가 회전량)
            // 이미 회전한 각도에 따라 추가 회전 비율을 조절할 수 있음
            // 예: 이미 많이 돌았으면 추가 회전량을 줄이는 등
            float additionalRotation = finalRotationDegrees * 0.5f; // 추가 회전은 기본 회전의 절반만
            
            // 최종 회전 각도 (누적)
            float cumulativeRotation = previousRotation + additionalRotation;
            
            // 최대 180도 제한 (선택적)
            cumulativeRotation = Mathf.Clamp(cumulativeRotation, -180f, 180f);
            
            // 누적된 회전 각도로 새 방향 계산
            Quaternion totalRotation = Quaternion.AngleAxis(cumulativeRotation, Vector3.up);
            targetDirection = totalRotation * Vector3.forward;
            
            // 로그 출력
            Debug.Log($"[누적 회전] 이전 회전={previousRotation:F1}°, 추가={additionalRotation:F1}°, 최종={cumulativeRotation:F1}°");
        }
        else
        {
            // 방향이 바뀌거나 처음 회전하는 경우 기존 방식대로 처리
            Quaternion rotationAmount = Quaternion.AngleAxis(finalRotationDegrees, Vector3.up);
            targetDirection = rotationAmount * currentForward;
        }
        
        // ---------- 누적 회전 처리를 위한 핵심 변경 부분 끝 ----------
        
        yawBasedDirection = targetDirection.normalized;

        // 현재 상태 확인
        bool wasInCircle = (currentState == MovementState.Circle);
        
        // 이전 방향과 새 방향이 충분히 다를 때만 방향 전환 (중복 방지)
        // 또는 백워킹 모드 전환 시에는 항상 방향 전환 적용
        if (Vector3.Angle(straightDirection, yawBasedDirection) > 5.0f || wasInCircle || backwalkingModeChanged)
        {
            // 백워킹 모드 전환 시에는 방향 강제 유지 모드를 비활성화 (추가 회전 방지)
            if (!backwalkingModeChanged)
            {
                // 일반적인 방향 전환에서만 방향 강제 유지 모드 활성화
                yawDirectionOverride = true;
            }
            
            // 백워킹 모드 고려하여 직선 이동으로 전환
            SwitchToStraightMovementWithYaw(yawBasedDirection);

            // 이동 방향 유지 시간 리셋
            directionMaintainTime = 0f;

            // 로그 출력 (백워킹 모드 전환 정보 추가)
            string modeInfo = backwalkingModeChanged ? "[백워킹 모드 전환] " : "";
            Debug.Log($"{modeInfo}[방향 전환] {(isLeft ? "왼쪽" : "오른쪽")}, Yaw: {currentYaw:F1}°, " + 
                    $"회전각: {finalRotationDegrees:F1}°, 강도: {currentYawIntensity:F2}");
        }
    }
    // 원형 이동으로 전환 (원본 함수 수정)
    private void SwitchToCircleMovement()
    {
        // 디버깅: 명확한 로그 추가
        Debug.Log($"[원형 이동 전환] 시도 - 현재 상태: {currentState}, Yaw 상태: {currentYawState}, 타이머: {yawNeutralTimer:F2}초");

        // Yaw 기반 회전이 활성화되어 있고 중립 상태가 아니면 원형 전환 금지
        if (useYawControl && isYawDirectionActive && currentYawState != YawRotationState.Neutral)
        {
            // 추가 로그 - 원형 전환 차단 이유 표시
            Debug.Log($"[원형 이동 전환 실패] Yaw {currentYawState} 방향으로 {currentYawIntensity:P0} 강도의 회전 중");
            return;
        }
        
        // 이미 원형 이동 중이면 무시
        if (currentState == MovementState.Circle)
        {
            Debug.Log("[원형 이동 전환 무시] 이미 원형 이동 상태");
            return;
        }

        // 상태 변경
        MovementState previousState = currentState;
        currentState = MovementState.Circle;
        circleStateStartTime = Time.time;

        // 백워킹 모드를 고려한 방향 설정
        Vector3 forwardDir = transform.forward;
        forwardDir.y = 0;
        forwardDir.Normalize();

        // 원회전 각도 계산은 백워킹 모드에 관계없이 동일하게 유지
        angle = Mathf.Atan2(forwardDir.x, forwardDir.z);

        // 원 위의 위치와 접선 방향 계산
        float x = Mathf.Sin(angle) * circleRadius;
        float z = Mathf.Cos(angle) * circleRadius;
        Vector3 tangentDirection = new Vector3(x, 0, z).normalized;

        // 백워킹 모드에서는 이동 방향만 반대로 - 기존 코드 유지
        straightDirection = isBackwalkingMode ? -tangentDirection : tangentDirection;

        // 바라보는 방향 설정 - 백워킹 모드에서도 목표 방향은 앞을 향하게 함
        // 이동 방향과 별개로 캐릭터는 항상 진행 방향의 접선을 바라봄
        targetRotation = Quaternion.LookRotation(tangentDirection);


        // 상태 변수 초기화
        hasReachedNeutral = false;
        isInCooldown = false;
        
        // 중립 타이머 초기화
        yawNeutralTimer = 0f;
        
        Debug.Log($"[원형 이동 전환 성공] {previousState} -> {currentState}, 각도: {angle * Mathf.Rad2Deg:F1}°");
    }

    // SwitchToStraightMovementWithYaw 메서드 수정
    private void SwitchToStraightMovementWithYaw(Vector3 direction)
    {
        // 상태 변경
        MovementState previousState = currentState;
        currentState = MovementState.Straight;
        directionMaintainTime = 0f;

        // 이동 방향 설정 - 백워킹 모드 고려
        if (isBackwalkingMode)
        {
            straightDirection = -direction; // 백워킹에서는 이동 방향 반대로
        }
        else
        {
            straightDirection = direction;
        }
        
        // 회전 방향은 항상 정방향 (캐릭터가 바라보는 방향)
        targetRotation = Quaternion.LookRotation(direction);

        // 쿨다운 적용
        isInCooldown = true;
        cooldownTimer = 0f;

        // 상태 변경 로그
        if (previousState == MovementState.Circle)
        {
            Debug.Log($"[원형→직선] Yaw 기반 직선 이동으로 전환 (백워킹: {isBackwalkingMode})");
        }
        else
        {
            Debug.Log($"[방향 갱신] Yaw 기반 직선 이동 방향 업데이트 (백워킹: {isBackwalkingMode})");
        }
    }

    // 직선 이동 처리 함수 수정
    private void MoveInStraightLine()
    {
        isMove = true;
        
        // Yaw 제어 사용 시 현재 yaw 값이 임계값을 넘었는지 체크
        bool isYawExceedingThreshold = false;
        if (useYawControl && (Mathf.Abs(currentYaw) >= 10f))
        {
            isYawExceedingThreshold = true;
        }
        
        // yaw 제어를 사용하지 않을 때만 방향 유지 시간을 누적합니다.
        if (!useYawControl)
        {
            directionMaintainTime += Time.deltaTime;
        }
        
        // yaw 제어 사용이 아닐 때만 시간이 지나면 자동으로 원형 이동으로 전환
        if (!useYawControl && forceCircleAfterTime && directionMaintainTime >= forcedStraightDuration)
        {
            SwitchToCircleMovement();
            return;
        }
        
        // 백워킹 모드에서 Yaw 제어가 활성화된 경우의 처리
        if (useYawControl && isYawDirectionActive && currentYawState != YawRotationState.Neutral)
        {
            if (isBackwalkingMode)
            {
                // 백워킹 모드에서는 yawBasedDirection과 반대 방향으로 이동
                straightDirection = -yawBasedDirection;
                
                // 하지만 캐릭터는 여전히 yawBasedDirection을 바라봄
                targetRotation = Quaternion.LookRotation(yawBasedDirection);
            }
            else
            {
                // 기존 일반 모드 처리
                straightDirection = yawBasedDirection;
                targetRotation = Quaternion.LookRotation(straightDirection);
            }
        }

        
        // 부드러운 회전을 위해 매 프레임 현재 회전에서 targetRotation으로 천천히 회전
        if (!disableRotationControl)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSmoothness * 20f * Time.deltaTime);
        }
        
        // GyroZ 기반 방향 전환: yaw 제어를 사용하지 않을 때만 처리
        if (!useYawControl)
        {
            // 자이로 Z를 통한 중립 상태 체크 및 하향/상향 전환 로직
            float relativeGyroZ = currentGyroZ;
            bool isPotentialNeutral = Mathf.Abs(relativeGyroZ) < gyroZThreshold * 0.5f;
            if (isPotentialNeutral)
            {
                neutralCountdown += Time.deltaTime;
                if (neutralCountdown >= requiredNeutralTime)
                {
                    hasReachedNeutral = true;
                }
            }
            else
            {
                neutralCountdown = 0f;
            }
            
            if (hasReachedNeutral)
            {
                int newDirection = relativeGyroZ > gyroZThreshold ? 1 : (relativeGyroZ < -gyroZThreshold ? -1 : 0);
                float currentMagnitude = Mathf.Abs(relativeGyroZ);
                
                // 같은 방향이라도 이전보다 30% 이상 강할 때만 방향 전환 허용
                bool allowSameDirection = newDirection == lastMovementDirection &&
                    currentMagnitude > lastGyroZMagnitude * 1.3f;
                
                if (newDirection != 0 && (newDirection != lastMovementDirection || allowSameDirection))
                {
                    Vector3 currentForward = transform.forward;
                    currentForward.y = 0;
                    currentForward.Normalize();
                    Vector3 currentRight = Vector3.Cross(Vector3.up, currentForward).normalized;
                    Vector3 currentLeft = -currentRight;
                    
                    if (newDirection == 1)
                    {
                        straightDirection = currentLeft;
                        Debug.Log($"Direction Change: LEFT, gyroZ = {relativeGyroZ:F3}, magnitude = {currentMagnitude:F3}");
                    }
                    else
                    {
                        straightDirection = currentRight;
                        Debug.Log($"Direction Change: RIGHT, gyroZ = {relativeGyroZ:F3}, magnitude = {currentMagnitude:F3}");
                    }
                    
                    lastMovementDirection = newDirection;
                    lastGyroZMagnitude = currentMagnitude; // 현재 강도 저장
                    targetRotation = Quaternion.LookRotation(straightDirection);
                    directionMaintainTime = 0f;
                    hasReachedNeutral = false;
                }
            }
        }
    }


    // MoveInCircle 함수를 수정하여 속도와 상관없이 원 경로 유지
    private void MoveInCircle()
    {
        // 중립 상태인 경우 타이머 증가 처리 (기존 코드 유지)
        if (useYawControl && Mathf.Abs(currentYaw) <= neutralYawThreshold)
        {
            ProcessNeutralYaw();
        }

        // Roll 기반 속도 계산 (기존 코드 유지)
        float rollBasedSpeed = CalculateSpeedFromRoll();
        float speedRatio = rollBasedSpeed / baseWalkSpeed;
        
        // 각속도 조정 (기존 코드 유지)
        float adjustedRotationSpeed = rotationSpeed * speedRatio;

        // 백워킹 모드에 따라 원회전 방향 결정 (기존 코드 유지)
        if (isBackwalkingMode)
            angle += adjustedRotationSpeed * Time.deltaTime;
        else
            angle -= adjustedRotationSpeed * Time.deltaTime;

        // 각도 정규화 (기존 코드 유지)
        if (angle > 2 * Mathf.PI)
            angle -= 2 * Mathf.PI;
        else if (angle < 0)
            angle += 2 * Mathf.PI;

       // 원 위의 접선 방향 계산 - 외적 순서 변경
        float x = Mathf.Sin(angle) * circleRadius;
        float z = Mathf.Cos(angle) * circleRadius;

        // 여기서 방향과 크기를 모두 유지하는 벡터 계산
        Vector3 circleVector = new Vector3(x, 0, z);

        // 외적 순서 변경 - circleVector와 Vector3.up의 외적으로 변경
        Vector3 tangentDirection = Vector3.Cross(circleVector, Vector3.up).normalized;

        // 이동 방향 설정 (기존 코드 유지)
        straightDirection = isBackwalkingMode ? -tangentDirection : tangentDirection;

        // 캐릭터가 바라보는 방향 설정 (기존 코드 유지)
        Quaternion desiredRotation = Quaternion.LookRotation(tangentDirection);

        // 나머지 코드는 동일하게 유지
        float timeSinceCircleStart = Time.time - circleStateStartTime;
        float rotationFactor = Mathf.Min(1.0f, timeSinceCircleStart * 2);
        float adjustedSmoothness = rotationSmoothness * (0.5f + rotationFactor * 0.5f);

        if (!disableRotationControl)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation,
                                adjustedSmoothness * Time.deltaTime);
            targetRotation = desiredRotation;
        }

        // 방향 전환 체크
        CheckForMovementChange();
    }

    public void SwitchToCircleMovementPublic()
    {
        SwitchToCircleMovement();
    }

    private void CheckForMovementChange()
    {
        float gyroValue = currentGyroZ;
        directionCountdown += Time.deltaTime;
        if (directionCountdown >= requiredDirectionTime)
        {
            directionCountdown = 0f;

            Vector3 currentForward = transform.forward;
            currentForward.y = 0;
            currentForward.Normalize();
            Vector3 currentRight = Vector3.Cross(Vector3.up, currentForward).normalized;
            Vector3 currentLeft = -currentRight;

            // 백워킹 모드에서는 방향 반전
            bool goLeft, goRight;
            if (isBackwalkingMode)
            {
                // 백워킹에서는 반대로 적용
                goLeft = gyroValue < -gyroZThreshold;
                goRight = gyroValue > gyroZThreshold;
            }
            else
            {
                // 일반 모드 - 기존 방식 유지
                goLeft = gyroValue > gyroZThreshold;
                goRight = gyroValue < -gyroZThreshold;
            }
            
            if (goLeft)
            {
                straightDirection = currentLeft;
                Debug.Log($"Direction Change Triggered: LEFT (백워킹: {isBackwalkingMode}), gyroZ = {gyroValue:F3}");
                lastMovementDirection = 1;
                SwitchToStraightMovement();
            }
            else if (goRight)
            {
                straightDirection = currentRight;
                Debug.Log($"Direction Change Triggered: RIGHT (백워킹: {isBackwalkingMode}), gyroZ = {gyroValue:F3}");
                lastMovementDirection = -1;
                SwitchToStraightMovement();
            }
        }
    }

    private void SwitchToStraightMovement()
    {
        currentState = MovementState.Straight;
        directionMaintainTime = 0f;

        // 현재 머리가 바라보는 방향 (현재 회전)을 시작점으로 사용
        Quaternion currentHeadRotation = transform.rotation;

        // 목표 방향 계산 (현재 기준 좌/우 방향)
        Vector3 currentForward = transform.forward;
        currentForward.y = 0;
        currentForward.Normalize();

        // 백워킹 모드는 바라보는 방향과 이동 방향만 분리
        // 회전 방향은 이동 방향과 반대로 설정하지 않음
        targetRotation = Quaternion.LookRotation(isBackwalkingMode ? currentForward : straightDirection);

        isInCooldown = true;
        cooldownTimer = 0f;
    }

    // OnGUI로 현재 Yaw 상태 표시 개선 (디버깅용)
    // void OnGUI()
    // {
    //     if (useYawControl)
    //     {
    //         // 첫 번째 줄 - 기본 상태 정보
    //         GUI.Label(new Rect(10, 100, 350, 20),
    //             $"Yaw: {currentYaw:F1}°, 상태: {currentYawState}, 강도: {currentYawIntensity:P0}");
            
    //         // 두 번째 줄 - 활성화 상태와 방향
    //         string statusText = isYawDirectionActive ? 
    //             $"회전 중: {(currentYawState == YawRotationState.Left ? "왼쪽" : "오른쪽")}" : 
    //             "비활성화";
                
    //         // 방향 강제 유지 상태 표시
    //         if (yawDirectionOverride)
    //             statusText += " (강제 유지 중)";
                
    //         GUI.Label(new Rect(10, 120, 350, 20), statusText);
                
    //         // 세 번째 줄 - 현재 이동 상태
    //         GUI.Label(new Rect(10, 140, 350, 20), 
    //             $"이동 상태: {(currentState == MovementState.Circle ? "원형" : "직선")}");
    //     }
    //     // Roll 정보 표시 추가
    //     if (useRollSpeedControl)
    //     {
    //         GUI.Label(new Rect(10, 160, 350, 20), 
    //             $"Roll: {currentRoll:F1}°, 속도: {CalculateSpeedFromRoll():F2}");
    //     }
    // }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        switch (currentState)
        {
            case MovementState.Circle:
                Gizmos.color = Color.blue;
                break;
            case MovementState.Straight:
                Gizmos.color = Color.red;
                break;
        }
        Gizmos.DrawRay(transform.position, straightDirection * 2);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, new Vector3(currentGyroZ * 2, 0, 0));
    }
}
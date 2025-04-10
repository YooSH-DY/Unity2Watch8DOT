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
    public float Speed = 3.0f;
    [Tooltip("원형 이동 시의 반지름.")]
    public float circleRadius = 15.0f;
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
            if(rb == null)
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
        // GyroZ 데이터 구독 (기존)
        DataTransBehavior.OnNewGyroZ += (value) => {
            gyroQueue.Enqueue(value);
            // Debug.Log($"GyroZ value enqueued: {value}");
        };

        // DOT 센서의 GyroY 데이터 구독 추가
        DataTransBehavior.OnNewGyroY += (value) => {
            UpdateGyroYData(value);
            // Debug.Log($"GyroY value received: {value}");
        };
        // DataTransBehavior.OnNewHandGesture += (fingerCount, palmOrientation) => {
        //     HandleHandGesture(fingerCount, palmOrientation);
        // };
        currentGyroZ = 0f;
        baselineGyroZ = 0f; // 초기 기준은 0으로 고정
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
        if (Time.time - lastDataReceivedTime > dataTimeoutDuration)
        {
            // 일정 시간 데이터가 없으면 gyro 값 리셋
            currentGyroZ = 0f;
            smoothedGyroZ = 0f;
        }
        // 메인 스레드에서 큐에 저장한 자이로 데이터를 처리
        while(gyroQueue.TryDequeue(out float newValue))
        {
            UpdateGyroData(newValue);
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

        // 이동 처리: 직선 이동 시 미리 설정된 straightDirection 사용
    if (isMove)
    {
        float currentSpeedMultiplier = 0.8f; // 기본 속도 계수
        
        // 감속 중일 때는 속도를 점진적으로 감소
        if (isSlowingDown)
        {
            // 기존 코드...
        }
        
        // 백워킹 모드에 따라 이동 방향 결정
        Vector3 moveDirection = straightDirection;
        
        // 계산된 속도로 이동
        transform.position += moveDirection * Speed * currentSpeedMultiplier * Time.deltaTime;
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

    // MoveInCircle 메소드를 완전히 수정합니다
    private void MoveInCircle()
    {
        // 백워킹 모드에 따라 원회전 방향 결정
        if (isBackwalkingMode)
            angle -= rotationSpeed * Time.deltaTime;  // 백워킹 시 반대 방향으로 회전
        else
            angle += rotationSpeed * Time.deltaTime;  // 일반 모드에서는 시계 방향으로 회전
        
        // 각도 정규화
        if (angle > 2 * Mathf.PI)
            angle -= 2 * Mathf.PI;
        else if (angle < 0)
            angle += 2 * Mathf.PI;
        
        // 원 위의 위치 계산 (백워킹이어도 똑같은 궤적)
        float x = Mathf.Sin(angle) * circleRadius;
        float z = Mathf.Cos(angle) * circleRadius;
        
        // 원 회전의 접선 방향 벡터 계산
        Vector3 tangentDirection = new Vector3(x, 0, z).normalized;
        
        // 백워킹 모드일 때는 접선 방향의 반대로 이동 (중요!)
        // 이 방식은 같은 원을 그리면서 반대 방향으로 이동하게 함
        straightDirection = isBackwalkingMode ? -tangentDirection : tangentDirection;
        
        // 캐릭터가 바라보는 방향 설정
        // 항상 원 접선 방향을 바라보도록 함 (백워킹이어도 방향은 같음)
        Quaternion desiredRotation = Quaternion.LookRotation(tangentDirection);
        
        // 나머지 코드는 그대로 유지...
        float timeSinceCircleStart = Time.time - circleStateStartTime;
        float rotationFactor = Mathf.Min(1.0f, timeSinceCircleStart * 2);
        float adjustedSmoothness = rotationSmoothness * (0.5f + rotationFactor * 0.5f);
        
        if (!disableRotationControl)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 
                                adjustedSmoothness * Time.deltaTime);
            targetRotation = desiredRotation;
        }
        
        CheckForMovementChange();
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
            
            if (gyroValue > gyroZThreshold)
            {
                straightDirection = currentLeft;
                Debug.Log($"Direction Change Triggered: LEFT, gyroZ = {gyroValue:F3}, gyroY = {currentGyroY:F3}");
                lastMovementDirection = 1;
                SwitchToStraightMovement();
            }
            else if (gyroValue < -gyroZThreshold)
            {
                straightDirection = currentRight;
                Debug.Log($"Direction Change Triggered: RIGHT, gyroZ = {gyroValue:F3}, gyroY = {currentGyroY:F3}");
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
    private void MoveInStraightLine()
    {
        isMove = true;
        directionMaintainTime += Time.deltaTime;
        
        if (forceCircleAfterTime && directionMaintainTime >= forcedStraightDuration)
        {
            SwitchToCircleMovement();
            return;
        }
        
        // 부드러운 회전을 위해 매 프레임 현재 회전에서 targetRotation으로 천천히 회전
        if (!disableRotationControl)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSmoothness * 20f * Time.deltaTime);
        }
        // 나머지 로직 (예: 중립 상태 및 방향 전환 체크)는 기존 코드 그대로...
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
            
            if (hasReachedNeutral)
            {
                int newDirection = relativeGyroZ > gyroZThreshold ? 1 : (relativeGyroZ < -gyroZThreshold ? -1 : 0);
                float currentMagnitude = Mathf.Abs(relativeGyroZ);
                
                // 같은 방향이라도 이전보다 큰 값이면 허용
                bool allowSameDirection = newDirection == lastMovementDirection && 
                                        currentMagnitude > lastGyroZMagnitude * 1.3f; // 30% 이상 큰 경우만
                
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
    public void SwitchToCircleMovementPublic()
    {
        SwitchToCircleMovement();
    }
    private void SwitchToCircleMovement()
    {
        currentState = MovementState.Circle;
        circleStateStartTime = Time.time;
        
        // 중요: 현재 방향을 기준으로 원회전 각도를 계산
        Vector3 forwardDir = transform.forward;
        
        // 원회전 각도 계산은 백워킹 모드에 관계없이 동일하게 유지
        // 이렇게 하면 백워킹 전환 시에도 원의 위치가 유지됨
        angle = Mathf.Atan2(forwardDir.x, forwardDir.z);
        
        // 원 위의 위치와 접선 방향 계산
        float x = Mathf.Sin(angle) * circleRadius;
        float z = Mathf.Cos(angle) * circleRadius;
        Vector3 tangentDirection = new Vector3(x, 0, z).normalized;
        
        // 백워킹 모드에서는 이동 방향만 반대로
        straightDirection = isBackwalkingMode ? -tangentDirection : tangentDirection;
        
        // 바라보는 방향은 항상 진행 방향의 접선 방향으로
        targetRotation = Quaternion.LookRotation(tangentDirection);
        
        // 상태 변수 초기화
        hasReachedNeutral = false;
        isInCooldown = false;
        
        Debug.Log($"원형 이동으로 전환 - 백워킹 모드: {isBackwalkingMode}, 각도: {angle * Mathf.Rad2Deg}도");
    }
    // OnDrawGizmos() 메서드 위에 추가
    // private void HandleHandGesture(int fingerCount, int palmOrientation)
    // {
    //     // 손가락 개수와 손바닥 방향에 따른 처리
    //     Debug.Log($"손가락 개수: {fingerCount}, 손바닥 방향: {palmOrientation}");
        
    //     // 예시: 손가락 개수에 따라 다른 동작 수행
    //     switch (fingerCount)
    //         case 0: // 주먹
    //             // 멈춤 동작 등 구현
    //             break;
    //         case 1: // 검지만 펴기
    //             // 가리키는 동작 등 구현
    //             break;
    //         case 5: // 손 전체 펴기
    //             // 인사 동작 등 구현
    //             break;
    //         // 나머지 경우에 대한 처리...
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
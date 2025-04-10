using UnityEngine;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class GestureController : MonoBehaviour
{
    public Animator animator;
    public PlayerController playerController;
    
    // 상태 관리 열거형 (단순화)
    private enum JumpState
    {
        None, // 점프 상태 아님
        WaitingForDown, // Base 상태에서 대기 중 (아래로 기울임 대기)
        WaitingForUp, // Ready 상태에서 대기 중 (위로 기울임 대기)
        Jumping, // Jumping 상태 (도약)
        JumpAir, // JumpAir 상태 (체공)
        JumpingDown, // JumpingDown 상태 (착지)
        Completing // 점프 완료 중
    }

    // 핵심 변수
    private JumpState jumpState = JumpState.None;
    private float currentGyroY = 0f;
    private float animationStartTime = 0f;
    
    // 애니메이션 제어 상수
    private const float JUMP_PREP_FRAME = 11.0f;
    private const float JUMP_TOTAL_FRAMES = 55.0f;
    private const float JUMP_PREP_NORMALIZED = JUMP_PREP_FRAME / JUMP_TOTAL_FRAMES;
    private bool isKeyboardJump = false;
    public bool isJumpInProgress = false; // 점프 중임을 나타내는 플래그

    // 임계값 상수 (강화)
    private const float GYRO_DOWN_THRESHOLD = -60f; // 아래로 기울임 감지 임계값
    private const float GYRO_UP_THRESHOLD = 60f; // 위로 기울임 감지 임계값
    private const float AUTO_ADVANCE_TIME = 0.3f;
    private float lowestGyroY = 0f; // 점프 준비 중 저장된 최소 자이로Y값
    private float ct=0f;
    // 애니메이션 잠금 관련 변수
    private bool isAnimationLocked = false;
    private float animationLockEndTime = 0f;
    private string currentAnimationName = "";
    private bool isProcessingDownArrow = false;
    private bool jumpBase=false;
    private bool jumpReady=false;
    private bool jumping=false;
    private bool jumpDown=false;
    // 손 제스처 관련
    private int lastFingers = -1;
    private int lastPalmOrientation = -1;
    private ConcurrentQueue<(int, int)> gestureQueue = new ConcurrentQueue<(int, int)>();
    private float readyAnimStartTime = 0f; // 점프 준비 애니메이션 시작 시간 추적용 변수 추가
    // 손가락 개수를 외부에서 접근할 수 있게 공개 속성 추가
    public int LastFingerCount => lastFingers;
    // 애니메이션 길이 정의
    private readonly Dictionary<string, float> animationDurations = new Dictionary<string, float>()
    {
        { "Jump", 1.5f },
        { "BackWalking", 1.5f },
        { "CatWalk", 1.5f },
        { "Crouch", 1.0f },
        { "Run", 2.0f },
        { "Turn", 2.0f}
    };

    // 롤(Roll) 각도 관련 변수 추가
    private float currentRoll = 0f;
    private float minRoll = 0f;  // 점프 준비 중 저장된 최소 롤 값
    private float maxRoll = 0f;  // 점프 실행 시 저장된 최대 롤 값
    private bool isWaitingForRollUp = false;  // 롤 각도가 올라가길 기다리는 중인지
    
    // 롤 각도 임계값 설정
    private const float ROLL_DOWN_THRESHOLD = -10f;  // 웅크림 시작 임계값
    private const float ROLL_UP_THRESHOLD = 10f;    // 점프 시작 임계값
    
    // 점프 높이 설정 관련 변수
    private const float MIN_JUMP_HEIGHT = 1.0f;  // 최소 점프 높이
    private const float MAX_JUMP_HEIGHT = 6.0f;  // 최대 점프 높이
    private const float MIN_ROLL_SUM = 20f;      // 최소 롤 합계 (절대값)
    private const float MAX_ROLL_SUM = 160f;     // 최대 롤 합계 (절대값)
    // 클래스 멤버 변수 추가
    private bool wasBackwalkingBeforeJump = false;  // 점프 전 백워킹 상태였는지 저장
    private float calculatedJumpHeight = 0f; // 계산된 점프 높이 저장

    void Start()
    {
        // 이벤트 구독
        DataTransBehavior.OnNewHandGesture += EnqueueHandGesture;
        DataTransBehavior.OnNewGyroY += OnGyroYChanged;
         // 새로운 쿼터니언X 구독 추가
        DataTransBehavior.OnNewQuatX += (value) => {
        UpdateQuaternionXData(value);
        //Debug.Log($"쿼터니언X 값 수신: {value}");
    };
        // 롤 데이터 구독 추가
        DataTransBehavior.OnNewRoll += (value) => {
            UpdateRollData(value);
            //Debug.Log($"롤 값 수신: {value}");
        };
        // PlayerController 자동 찾기
        if (playerController == null)
            playerController = FindObjectOfType<PlayerController>();
        
        // 초기 상태 확인
        Debug.Log("GestureController 시작됨: 점프 상태 = " + jumpState);
        if (animator != null)
        {
            animator.applyRootMotion = false; // 루트 모션 비활성화 (직접 제어하기 위함)
            animator.SetFloat("JumpPhase", 0); // 블렌드 트리 파라미터 초기화
        }
    }
        // 쿼터니언X 데이터 처리 메서드 추가
    private float currentQuatX = 0f;
    private float smoothedQuatX = 0f;

    private void UpdateQuaternionXData(float newQuatXValue)
    {
        // 이전 값 저장
        float previousQuatX = currentQuatX;
     
        // 노이즈 제거를 위한 임계값 적용
        float quatXNoiseThreshold = 0.01f; // 작은 변화 무시
        
        // 이전 값과의 차이가 임계값보다 작으면 값 유지
        if (Mathf.Abs(smoothedQuatX - previousQuatX) < quatXNoiseThreshold)
        {
            // 값 유지 (이전 값 사용)
            smoothedQuatX = previousQuatX;
        }
        
        currentQuatX = smoothedQuatX;
        
        // 매우 작은 값은 0으로 처리
        if (Mathf.Abs(currentQuatX) < 0.005f)
        {
            currentQuatX = 0f;
        }
        
        // 이제 currentQuatX 값을 사용하여 원하는 처리 수행
        // 예: 캐릭터 회전, 크기 조절 등에 활용
    }

    // 롤 데이터 처리 메서드 수정
    private void UpdateRollData(float newRollValue)
    {
        // 현재 롤 값 업데이트 (노이즈 필터링)
        if (Mathf.Abs(currentRoll - newRollValue) >= 0.5f)
        {
            currentRoll = newRollValue;
            
            // 디버그: 롤 값 변화 출력
            //Debug.Log($"Roll 값 갱신: 현재={currentRoll:F1}°, 최소={minRoll:F1}°, 최대={maxRoll:F1}°, 상태={jumpState}, 웅크림대기={isWaitingForRollUp}");
            
            // 단계별 점프 상태 처리
            
            // 1. 웅크림 감지 (jumpBase → jumpReady)
            if (jumpState == JumpState.WaitingForDown && !isWaitingForRollUp)
            {
                // UpdateRollData 메서드 내의 웅크림 전환 부분 수정
                if (currentRoll <= ROLL_DOWN_THRESHOLD)
                {
                    // 현재 위치 저장 (웅크림 전환 전)
                    Vector3 originalPosition = transform.position;
                    characterOriginalHeight = animator.bodyPosition.y; // 원래 높이 저장
                    
                    // 이 시점부터 최소값 추적 시작 (초기값을 현재 롤 값으로 설정)
                    minRoll = currentRoll;
                    
                    Debug.Log($"[중요] 웅크림 감지됨! Roll={currentRoll:F1}° (임계값: {ROLL_DOWN_THRESHOLD}°)");
                    
                    // 상태 변수 업데이트
                    isWaitingForRollUp = true;
                    jumpBase = false;
                    jumpReady = true;
                    jumping = false;
                    jumpState = JumpState.WaitingForUp;
                    
                    // 이제 이동을 중지 (중요!) - 웅크림 자세로 들어갈 때만 이동 비활성화
                    if (playerController != null)
                        playerController.isMove = false;
                        
                    // 웅크림 애니메이션 시작
                    StopAllCoroutines();
                    StartCoroutine(ImprovedJump());
                }
            }
            // 웅크림 상태에서 최소 롤 각도 업데이트 (계속해서 더 낮은 값 추적)
            else if (jumpState == JumpState.WaitingForUp && isWaitingForRollUp && currentRoll < 0)
            {
                // 웅크림 단계에서는 최소값만 계속 갱신
                if (currentRoll < minRoll)
                {
                    minRoll = currentRoll;
                    //Debug.Log($"웅크림 중 최소 Roll 값 갱신: {minRoll:F1}°");
                    
                    // 웅크림 깊이에 따른 애니메이션 속도 조절을 실시간으로 업데이트
                    float negativeRoll = Mathf.Abs(minRoll);
                    float clampedRoll = Mathf.Clamp(negativeRoll, 10f, 80f); 
                    float squatIntensity = Mathf.InverseLerp(10f, 80f, clampedRoll);
                    // 가장 깊이 웅크림을 1로, 덜 웅크린 것을 0에 가깝게 표시
                    float normalizedIntensity = squatIntensity;
                    animator.speed = Mathf.Lerp(1.0f, 0.7f, squatIntensity);

                    // 웅크림 강도 로그 출력 (강도를 0~1로 표시)
                    Debug.Log($"[웅크림 상태] Roll: {minRoll:F1}°, 강도: {normalizedIntensity:F2} (1이 최대), 애니메이션 속도: {animator.speed:F2}");
                }
            }
            // 2. 점프 실행 감지 (jumpReady → jumping)
            else if (jumpState == JumpState.WaitingForUp && isWaitingForRollUp)
            {
                // +10도 이상으로 올라가면 최대값 수집 시작 (점프는 아직 실행하지 않음)
                if (currentRoll >= ROLL_UP_THRESHOLD && !isCollectingMaxRoll)
                {
                    // 최대 각도 수집 시작
                    isCollectingMaxRoll = true;
                    maxRollCollectStartTime = Time.time;
                    maxRoll = currentRoll; // 초기 최대값 설정
                    
                    Debug.Log($"[중요] 최대 각도 수집 시작! 현재 Roll={currentRoll:F1}°, 0.5초간 더 큰 값 추적");
                    
                    // 최대값 수집 코루틴 시작
                    StartCoroutine(CollectMaxRollValue());
                }
                // 최대값 수집 중에는 계속해서 더 큰 값 추적
                else if (isCollectingMaxRoll && currentRoll > maxRoll)
                {
                    maxRoll = currentRoll;
                    //Debug.Log($"최대 Roll 값 갱신: {maxRoll:F1}°");
                }
            }
        }
    }

    // 클래스 멤버 변수 추가
    private bool isCollectingMaxRoll = false;
    private float maxRollCollectStartTime = 0f;
    private const float maxRollCollectDuration = 0.2f; // 0.5초 동안 최대값 수집

    // 최대 롤 값 수집 코루틴 추가
    private System.Collections.IEnumerator CollectMaxRollValue()
    {
        // 0.5초 동안 최대 각도 수집
        yield return new WaitForSeconds(maxRollCollectDuration);
        
        // 수집 완료 후 점프 실행
        isCollectingMaxRoll = false;
        
        // 최소값과 최대값 사이의 범위 계산
        float rollRange = Mathf.Abs(minRoll) + Mathf.Abs(maxRoll);
        
        //Debug.Log($"[중요] 최대 각도 수집 완료! 최종 최대 Roll={maxRoll:F1}°, 최소 Roll={minRoll:F1}°, 범위={rollRange:F1}°");
        
        // 상태 변수 업데이트
        isWaitingForRollUp = false;
        jumpBase = false;
        jumpReady = false;
        jumping = true;
        
        // 점프 애니메이션 실행
        StopAllCoroutines();
        StartCoroutine(ImprovedJump());
    }

    void OnDestroy()
    {
        // 이벤트 구독 해제
        DataTransBehavior.OnNewHandGesture -= EnqueueHandGesture;
        DataTransBehavior.OnNewGyroY -= OnGyroYChanged;
    }

    void Update()
    {
        // 키보드 입력 감지 (새로 추가)
        CheckKeyboardInput();
        
        // 손 제스처 처리
        while (gestureQueue.TryDequeue(out var gesture))
        {
            HandleHandGesture(gesture.Item1, gesture.Item2);
        }
        
        // 점프 관련 상태 관리
        ManageJumpState();
        
        // 애니메이션 잠금 상태 업데이트
        UpdateAnimationLock();
    }

    // GyroY 값 변경 이벤트 핸들러
    private void OnGyroYChanged(float newValue)
    {
        // 키보드 점프 중이거나 화살표 키가 눌려있으면 자이로 값 업데이트 무시
        if (isKeyboardJump || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.UpArrow))
            return;
        
        currentGyroY = newValue;
        
        // 중요 상태 변화 로그
        if (jumpState != JumpState.None)
        {
            // 중요 임계값 근처일 때만 로그 출력
            if ((jumpState == JumpState.WaitingForDown && newValue < GYRO_DOWN_THRESHOLD) ||
                (jumpState == JumpState.WaitingForUp && newValue > GYRO_UP_THRESHOLD))
            {
                Debug.Log($"중요 GyroY 값: {currentGyroY:F2}, 점프 상태: {jumpState}");
            }
        }
    }
    public float GetJumpHeight()
    {
        return calculatedJumpHeight;
    }
    // 키보드 입력 확인 메서드 (W키 처리부분 수정)
    private void CheckKeyboardInput()
    {
        // 애니메이션 잠금 또는 점프 진행 중이면 새 입력 무시
        if (isAnimationLocked && jumpState == JumpState.None)
        {
            // 백워킹 중에는 Q키와 W키(점프)만 특별히 허용
            if (!(currentAnimationName == "BackWalking" && 
                (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.W))))
            {
                return;
            }
        }

        // 각 키를 해당하는 손가락 개수에 매핑
        if (Input.GetKeyDown(KeyCode.Q)) // 걷기 (손가락 5개)
        {
            Debug.Log("키보드 입력: Q (걷기)");
            // 점프 후에도 제대로 작동하도록 lastFingers 초기화
            lastFingers = -1;
            
            // 백워킹 상태인 경우 특별히 처리
            if (playerController != null && playerController.isBackwalkingMode)
            {
                // 백워킹 상태에서는 애니메이션 잠금을 강제로 해제
                isAnimationLocked = false;
                currentAnimationName = "";
                StopAllCoroutines();
                
                Debug.Log("백워킹에서 걷기로 전환: 회전 애니메이션 시작");
                StartCoroutine(TurnToWalk());
            }
            else
            {
                // 일반적인 경우 핸들러를 호출
                HandleHandGesture(5, 0);
            }
        }
        else if (Input.GetKeyDown(KeyCode.W)) 
        {
            Debug.Log("키보드 입력: W (점프)");
            
            // 백워킹 상태 저장
            wasBackwalkingBeforeJump = playerController != null && playerController.isBackwalkingMode;
            
            // 실행 중인 모든 코루틴 중지
            StopAllCoroutines();
            lastFingers = -1;
            isKeyboardJump = true;
            currentGyroY = 0f;
            lowestGyroY = 0f; 
            jumpState = JumpState.None;
            jumpBase = true;
            jumpReady = false;
            jumping = false;
            
            // 롤 관련 변수 초기화
            currentRoll = 0f;
            minRoll = 0f;
            maxRoll = 0f;
            isWaitingForRollUp = false;
            
            // 점프 중 플래그 설정
            isJumpInProgress = true;

            // 이동 즉시 비활성화 (수정된 부분)
            if (playerController != null)
                playerController.isMove = false;
            
            // 현재 모드에 맞는 애니메이션으로 시작 (Jump 애니메이션으로 직접 전환)
            if (wasBackwalkingBeforeJump) {
                animator.CrossFade("BackWalking", 0.2f);
            } else {
                animator.Play("Jump", 0, 0);
                animator.SetFloat("JumpPhase", 0); // 즉시 JumpBase 상태로 설정
            }
            
            Debug.Log($"점프 초기화 완료: {(wasBackwalkingBeforeJump ? "백워킹" : "JumpBase")} 자세로 전환(이동 중지)");
            StartCoroutine(ImprovedJump());
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            // 코루틴 실행 전에 변수 초기화
            StopAllCoroutines();
            isKeyboardJump = true;
            currentGyroY = 0f;
            jumpState = JumpState.None;
            // 이동 상태 비활성화 (중요!)
            if (playerController != null)
                playerController.isMove = false;
                
            StartCoroutine(AutomaticJump());
        }
        else if (Input.GetKeyDown(KeyCode.E)) // 뒤로 걷기
        {
            Debug.Log("키보드 입력: E (뒤로걷기)");
            lastFingers = -1;
            isKeyboardJump = true;
            currentGyroY = 0f;
            lowestGyroY = 0f;
            HandleHandGesture(1, 0);
        }
        else if (Input.GetKeyDown(KeyCode.R)) // 캣워크
        {
            Debug.Log("키보드 입력: R (캣워크)");
            lastFingers = -1;
            isKeyboardJump = true;
            currentGyroY = 0f;
            lowestGyroY = 0f;
            HandleHandGesture(3, 0); // 이 내부에서 StartJumpAtFrame0() 호출됨
        }
        else if (Input.GetKeyDown(KeyCode.T)) //스닉무브
        {
            Debug.Log("키보드 입력: T (스닉무브)");
            lastFingers = -1;
            isKeyboardJump = true;
            currentGyroY = 0f;
            lowestGyroY = 0f;
            HandleHandGesture(4, 0); // 이 내부에서 StartJumpAtFrame0() 호출됨
        }
        // 키보드 입력 중임을 나타내는 플래그 추가
        // ↓: 키를 누르는 동안 자이로 값을 계속 누적
        if (Input.GetKey(KeyCode.DownArrow))
        {
            isProcessingDownArrow = true;
            // 키 누르는 동안 자이로 값이 계속 감소
            float decreaseRate = 120f * Time.deltaTime;
            currentGyroY -= decreaseRate;
            // 자이로 최소값 계속 업데이트 (누적되는 가장 낮은 값)
            lowestGyroY = Mathf.Min(lowestGyroY, currentGyroY);
            // 로깅 (30 단위로만 출력하여 로그 스팸 방지)
            if (Mathf.Abs(currentGyroY) % 30 < decreaseRate)
            {
                //Debug.Log($"키보드 입력: ↓ 유지 중, currentGyroY={currentGyroY:F2}, lowestGyroY={lowestGyroY:F2}");
            }
        }

        // ↓: 키를 뗐을 때만 상태 변경
        if (Input.GetKeyUp(KeyCode.DownArrow))
        {
            isProcessingDownArrow = false;
            jumpBase=false;
            jumpReady=true;
            jumping=false;
            // 점프 준비 상태에서만 상태 전환 처리
            if (jumpState == JumpState.WaitingForDown && isKeyboardJump && currentGyroY < GYRO_DOWN_THRESHOLD)
            {
                Debug.Log($"↓ Key Released: 저장된 자이로 값으로 점프 준비 시작, lowestGyroY={lowestGyroY:F2}");
                StopAllCoroutines();
                StartCoroutine(ImprovedJump());  // 전체 점프 과정을 직접 시작
            }
            currentGyroY = 0f;
        }

        // ↑: 키를 누르는 동안 자이로 값을 계속 누적
        if (Input.GetKey(KeyCode.UpArrow))
        {
            // 키를 누르는 동안 자이로 값이 계속 증가
            float increaseRate = 120f * Time.deltaTime;
            currentGyroY += increaseRate;
            // 로깅 (30 단위로만 출력)
            if (Mathf.Abs(currentGyroY) % 30 < increaseRate)
            {
                //Debug.Log($"키보드 입력: ↑ 유지 중, currentGyroY={currentGyroY:F2}");
            }
            // 중요: 키를 누르는 동안은 상태 변화 없음
        }

        // ↑: 키를 뗐을 때만 상태 변경
        if (Input.GetKeyUp(KeyCode.UpArrow))
        {
            //Debug.Log($"화살표 위 키 뗌: 최종 자이로 값={currentGyroY:F2}");
            // 점프 준비 상태에서만 점프 실행
            jumpBase=false;
            jumpReady=false;
            jumping=true;
            if (jumpState == JumpState.WaitingForUp && currentGyroY > GYRO_UP_THRESHOLD)
            {
                Debug.Log($"↑ Key Released: 점프 최종 실행, currentGyroY={currentGyroY:F2}");
                StopAllCoroutines();
                StartCoroutine(ImprovedJump());  // 전체 점프 과정을 직접 시작
                isKeyboardJump = false; // 키보드 점프 플래그 리셋
            }
            // 키를 뗐을 때 currentGyroY 값 초기화
            currentGyroY = 0f;
        }
    }

    // 손 제스처 큐 처리
    private void EnqueueHandGesture(int fingerCount, int palmOrientation)
    {
        gestureQueue.Enqueue((fingerCount, palmOrientation));
    }

    // 새로운 메서드: 자동 점프 (자이로값 영향 없이 블렌드 트리 자동 실행)
    private System.Collections.IEnumerator AutomaticJump()
    {
        // 초기 설정 (기존 코드 유지)
        animator.Play("Jump", 0, 0);
        animator.SetFloat("JumpPhase", 0);
        jumpState = JumpState.WaitingForDown;
        // 이동 중지 추가 - 이 부분이 누락됨
        if (playerController != null)
            playerController.isMove = false;
        //Debug.Log("자동 점프: JumpBase 상태 시작");
        
        yield return new WaitForSeconds(0.1f);
        
        // Ready 상태 (기존 코드 유지)
        animator.Play("Jump", 0, 0);
        animator.SetFloat("JumpPhase", 1);
        jumpState = JumpState.WaitingForUp;
        //Debug.Log("자동 점프: JumpReady 상태 전환");
        
        yield return new WaitForSeconds(0.2f);
        
        // Jumping 상태 (기존 코드 유지)
        animator.Play("Jump", 0, 0);
        animator.SetFloat("JumpPhase", 2);
        jumpState = JumpState.Jumping;
        //Debug.Log("자동 점프: Jumping 상태 전환");
        
        // 도약 모션 시간 (기존 코드 유지)
        yield return new WaitForSeconds(0.2f);
        
        // 물리 점프 강도 낮춤 - 중요!!
        if (playerController != null && playerController.rb != null)
        {
            
            float jumpMultiplier = 1.0f; // 0.8f에서 0.5f로 더 낮춤
            float simulatedGyroY = -60f; // -60f에서 -40f로 더 약하게 설정
            playerController.rb.isKinematic = false;
            playerController.Jump(jumpMultiplier, simulatedGyroY);
            Debug.Log($"물리 점프 적용: 강도 {jumpMultiplier}배, gyroY {simulatedGyroY}");
        }
        
        // 점프 시작 시간 기록
        float jumpStartTime = Time.time;
        
        // JumpAir 상태로 전환
        yield return new WaitForSeconds(0.35f); // 이 수치가 정확하게 자세 잡는 시간
        animator.SetFloat("JumpPhase", 3);
        jumpState = JumpState.JumpAir;
        Debug.Log("자동 점프: JumpAir 상태 전환");
        
        // JumpAir 상태에서 Y축 값을 체크 - 먼저 피크(최고점)을 감지한 후 하강 단계에서만 JumpingDown으로 전환
        bool hasReachedPeak = false;
        bool hasTransitionedToDown = false;
        float checkStartTime = Time.time;
        float previousHeight = transform.position.y;
        float peakHeight = 0f;
        int consecutiveDescentFrames = 0; // 연속적인 하강 프레임 카운트
        
        while (Time.time - checkStartTime < 5.0f && !hasTransitionedToDown) // 최대 5초 동안 체크
        {
            float currentHeight = transform.position.y;
            
            // 최고점 갱신
            if (currentHeight > peakHeight)
                peakHeight = currentHeight;
            
            // 연속적인 하강 감지 (피크 감지용)
            if (currentHeight < previousHeight)
            {
                consecutiveDescentFrames++;
                if (consecutiveDescentFrames >= 1 && !hasReachedPeak)
                {
                    hasReachedPeak = true;
                    Debug.Log($"점프 피크 감지: 최고점 {peakHeight:F2}");
                }
            }
            else
            {
                consecutiveDescentFrames = 0;
            }
            
            // 피크를 지난 후 Y축이 임계값 이하로 내려오면 JumpingDown 상태로 전환
            if (hasReachedPeak && currentHeight <= 2.0f)
            {
                // JumpingDown 상태로 전환
                animator.Play("Jump", 0, 0);
                animator.SetFloat("JumpPhase", 4);
                animator.speed = 1.05f;
                jumpState = JumpState.JumpingDown;
                //Debug.Log($"피크 이후 높이 {currentHeight:F2}에서 JumpingDown 상태로 전환");
                hasTransitionedToDown = true;
                break;
            }
            
            previousHeight = currentHeight;
            yield return null; // 매 프레임 체크
        }
        // 착지 확인 (더 짧은 시간, 더 빠른 체크)
        float landingStartTime = Time.time;
        bool hasLanded = false;
        float initialHeight = transform.position.y;
        Vector3 initialPosition = transform.position;
        
        // 착지 조건이 만족될 때까지 대기
        while (Time.time - landingStartTime < 1.0f && !hasLanded)
        {
            bool isGrounded = playerController != null && playerController.IsGrounded();
            float currentHeight = transform.position.y;
            
            // 더 정밀한 착지 감지 - 매우 낮은 높이 또는 IsGrounded가 true일 때
            if (isGrounded || currentHeight <= 0.05f)
            {
                hasLanded = true;
                //Debug.Log($"착지 감지! 높이: {currentHeight:F2}, IsGrounded: {isGrounded}");
                break;
            }
            
            yield return null; // 매 프레임 체크
        }
        
        // 부드러운 착지를 위한 보간
        if (hasLanded)
        {
            float smoothTime = 0.15f; // 보간 시간 (짧게 설정)
            float elapsedTime = 0f;
            Vector3 currentVelocity = Vector3.zero;
            Vector3 startPos = transform.position;
            Vector3 targetPos = new Vector3(startPos.x, 0f, startPos.z);
            
            // 물리 정지
            if (playerController != null && playerController.rb != null)
            {
                playerController.rb.isKinematic = true;
            }
            
            // 부드럽게 위치 보정 (SmoothDamp 사용)
            while (elapsedTime < smoothTime)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / smoothTime);
                transform.position = Vector3.Lerp(startPos, targetPos, t);
                //Debug.Log($"착지 보간 중: {transform.position.y:F3}, 진행률: {t:P0}");
                yield return null;
            }
            
            // 최종 위치 확정
            transform.position = targetPos;
            Debug.Log($"착지 위치 보정 완료: {transform.position}");
        }
        else
        {
            // 착지 감지 실패 시 강제로 위치 조정
            Debug.LogWarning("착지 감지 실패, 강제 위치 설정");
            Vector3 fixedPosition = transform.position;
            fixedPosition.y = 0f;
            transform.position = fixedPosition;
        }
        
        // 짧은 대기 후 상태 초기화
        yield return new WaitForSeconds(0.4f);
        
        // 모든 상태 변수 초기화
        jumpState = JumpState.None;
        isAnimationLocked = false;
        currentAnimationName = "";
        animationLockEndTime = 0f;
        lastFingers = -1;
        lastPalmOrientation = -1;
        
        // 이동 상태 복원
        if (playerController != null)
            playerController.isMove = true;
        
        // 걷기 애니메이션으로 복귀
        animator.CrossFade("Walk", 0.2f);
        Debug.Log("자동 점프 완료, 걷기 상태로 복귀");
    }

    // ManageJumpState 메서드를 다음과 같이 수정
    private void ManageJumpState()
    {
        if (jumpState == JumpState.None)
            return;
        
        // 키보드 점프 진행 중이거나 ↑/↓ 키가 눌려있으면 자이로 체크 무시
        if (isKeyboardJump || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.UpArrow))
            return;
        
        // ↓ 상태 전환: 기록된 최소 자이로값이 -60 이하일 때만 전환
        if (jumpState == JumpState.WaitingForDown && lowestGyroY <= GYRO_DOWN_THRESHOLD)
        {
            Debug.Log($"【준비 시작】아래로 기울임 감지: lowestGyroY={lowestGyroY:F2}");
            StopAllCoroutines();
            StartCoroutine(ImprovedJump());  // StartPreparingJump() 대신 ImprovedJump 직접 호출
        }
        // ↑ 상태 전환: 현재 자이로값이 60 이상일 때만 전환
        else if (jumpState == JumpState.WaitingForUp && currentGyroY >= GYRO_UP_THRESHOLD)
        {
            Debug.Log($"【점프 시작】위로 기울임 감지: currentGyroY={currentGyroY:F2}");
            StopAllCoroutines();
            StartCoroutine(ImprovedJump());  // CompleteJump() 대신 ImprovedJump 직접 호출
        }
    }
    // 애니메이션 전환 중 위치 유지를 위한 코루틴
    private System.Collections.IEnumerator MaintainPositionDuringAnimation(Vector3 originalPosition)
    {
        // 애니메이션 전환 시간 동안 매 프레임 Y축 위치 고정
        float startTime = Time.time;
        float maintainDuration = 0.4f; // 충분한 시간 동안 위치 유지
        
        while (Time.time - startTime < maintainDuration)
        {
            // 현재 위치의 Y값만 고정
            Vector3 currentPosition = transform.position;
            if (currentPosition.y != originalPosition.y)
            {
                transform.position = new Vector3(currentPosition.x, originalPosition.y, currentPosition.z);
            }
            yield return null; // 매 프레임 체크
        }
        
        Debug.Log("위치 유지 코루틴 완료");
    }
    // 새로운 메서드: 임계값 기반 개선된 점프
    private System.Collections.IEnumerator ImprovedJump()
    {
        // 백워킹 모드 체크
        bool isInBackWalkingMode = playerController != null && playerController.isBackwalkingMode;
        
        // jumpBase 상태 처리 수정 - 즉시 멈추고 JumpBase 상태 시작
        if(jumpBase){
            // 백워킹 모드에 따라 적절한 애니메이션 설정
            if(isInBackWalkingMode){
                animator.CrossFade("BackWalking", 0.2f);
            } else {
                // 직접 Jump 애니메이션의 Base 단계로 설정
                animator.Play("Jump", 0, 0);
                animator.SetFloat("JumpPhase", 0);
            }
            
            jumpState = JumpState.WaitingForDown;
            
            // 이동 비활성화 상태 유지 (캐릭터가 움직이지 않도록)
            if (playerController != null)
                playerController.isMove = false;
                
            Debug.Log($"점프 준비 시작: {(isInBackWalkingMode ? "뒤로걷기" : "점프 기본")} 자세에서 롤 각도 감소 대기 중(이동 중지)");
            yield break; // 여기서 종료, UpdateRollData에서 Roll 감지 시 다시 호출
        }
        
        // jumpReady 상태 처리 부분 수정 (웅크림 자세)
        if(jumpReady){
            // 1. 원래 위치 저장 (중요)
            Vector3 originalPosition = transform.position;
            
            // 2. 애니메이션 변경 전에 이동을 먼저 중지 (중요!)
            if (playerController != null)
                playerController.isMove = false;
            
            Debug.Log("점프 준비 상태로 전환: 이동 비활성화");

            // 3. Jump 애니메이션 1단계 (웅크림)로 설정
            animator.Play("Jump", 0, 0);
            animator.SetFloat("JumpPhase", 1);
            jumpState = JumpState.WaitingForUp;
            readyAnimStartTime = Time.time;
            
            // 4. 웅크림 깊이에 따른 애니메이션 속도 조절
            float negativeRoll = Mathf.Abs(minRoll);
            float clampedRoll = Mathf.Clamp(negativeRoll, 10f, 80f); 
            float squatIntensity = Mathf.InverseLerp(10f, 80f, clampedRoll);
            
            // 5. 애니메이션 속도 조절
            animator.speed = Mathf.Lerp(1.0f, 0.7f, squatIntensity);
            
            Debug.Log($"웅크림 시작: Roll={minRoll:F1}°, 강도={squatIntensity:F2}, 속도={animator.speed:F2}");
            yield break;
        }

        // jumping 블록 안의 점프 높이 계산 부분 수정
        if(jumping){
            // Jump 상태(2)로 전환 - 도약
            animator.Play("Jump", 0, 0);
            animator.SetFloat("JumpPhase", 2);
            jumpState = JumpState.Jumping;
            Debug.Log("개선된 점프: Jumping 상태 전환 (롤 각도 기반)");
            
            // 도약 모션 시간 대기
            yield return new WaitForSeconds(0.2f);
            
            // 롤 각도 값에 따른 점프 높이 계산
            float rollSum = Mathf.Abs(minRoll) + Mathf.Abs(maxRoll);
            
            // 롤 각도 합에 따른 점프 높이 계산 - 구간 내에서 비례 계산 추가
            float jumpHeight;
            
            // 단계별 임계값과 높이 정의 (각 구간의 시작점)
            float[] rollThresholds = { 20f, 50f, 80f, 110f, 140f, 160f };
            float[] jumpHeights = { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f };
            
            // 최소값보다 작으면 최소 높이 사용
            if (rollSum <= rollThresholds[0])
            {
                jumpHeight = jumpHeights[0]; // 1.0m
                Debug.Log($"최소 롤 합계보다 작음: {rollSum:F2}° → 높이: {jumpHeight:F2}m");
            }
            // 최대값보다 크면 최대 높이 사용
            else if (rollSum >= rollThresholds[rollThresholds.Length - 1])
            {
                jumpHeight = jumpHeights[jumpHeights.Length - 1]; // 6.0m
                Debug.Log($"최대 롤 합계보다 큼: {rollSum:F2}° → 높이: {jumpHeight:F2}m");
            }
            // 구간 내에서 비례 계산
            else
            {
                // 현재 각도가 속한 구간 찾기
                int i = 0;
                while (i < rollThresholds.Length - 1 && rollSum > rollThresholds[i+1])
                {
                    i++;
                }
                
                // 구간 내에서 선형 보간으로 비례 계산
                float segmentStart = rollThresholds[i];
                float segmentEnd = rollThresholds[i+1];
                float heightStart = jumpHeights[i];
                float heightEnd = jumpHeights[i+1];
                
                // 현재 구간에서의 비율 계산 (0~1 사이 값)
                float ratio = (rollSum - segmentStart) / (segmentEnd - segmentStart);
                
                // 비율에 따른 높이 보간
                jumpHeight = Mathf.Lerp(heightStart, heightEnd, ratio);
                
                Debug.Log($"롤 합계 {rollSum:F2}°는 {segmentStart:F0}°~{segmentEnd:F0}° 구간의 {ratio:P1} 지점 → 높이: {jumpHeight:F2}m");
            }
            calculatedJumpHeight = jumpHeight;
            // 점프 실행
            if (playerController != null && playerController.rb != null)
            {
                playerController.rb.isKinematic = false;
                // 롤 각도 합에 따른 높이로 점프
                playerController.JumpWithHeight(jumpHeight);
            }
            
            // JumpAir 상태로 전환
            yield return new WaitForSeconds(0.35f);
            animator.SetFloat("JumpPhase", 3);
            jumpState = JumpState.JumpAir;
            Debug.Log("개선된 점프: JumpAir 상태 전환");

            // JumpAir 상태에서 Y축 값을 체크
            bool hasReachedPeak = false;
            bool hasTransitionedToDown = false;
            float checkStartTime = Time.time;
            float previousHeight = transform.position.y;
            float peakHeight = 0f;
            int consecutiveDescentFrames = 0; // 연속적인 하강 프레임 카운트
            
            // 최대 시간 제한 추가
            while (Time.time - checkStartTime < 5.0f && !hasTransitionedToDown)
            {
                float currentHeight = transform.position.y;
                
                // 최고점 갱신
                if (currentHeight > peakHeight)
                    peakHeight = currentHeight;
                
                // 연속적인 하강 감지 (피크 감지용)
                if (currentHeight < previousHeight)
                {
                    consecutiveDescentFrames++;
                    if (consecutiveDescentFrames >= 1 && !hasReachedPeak)
                    {
                        hasReachedPeak = true;
                        Debug.Log($"점프 피크 감지: 최고점 {peakHeight:F2}m");
                    }
                }
                else
                {
                    consecutiveDescentFrames = 0;
                }
                
                // 피크를 지난 후 Y축이 임계값 이하로 내려오면 JumpingDown 상태로 전환
                if (hasReachedPeak && currentHeight <= 2.0f)
                {
                    // JumpingDown 상태로 전환
                    animator.Play("Jump", 0, 0);
                    animator.SetFloat("JumpPhase", 4);
                    animator.speed = 1.05f;
                    jumpState = JumpState.JumpingDown;
                    Debug.Log($"피크 이후 높이 {currentHeight:F2}m에서 JumpingDown 상태로 전환");
                    hasTransitionedToDown = true;
                    break;
                }
                
                previousHeight = currentHeight;
                yield return null; // 매 프레임 체크
            }
            
            // 만약 높이 체크에서 전환되지 않았다면 여기서 강제로 전환
            if (!hasTransitionedToDown)
            {
                animator.Play("Jump", 0, 0);
                animator.SetFloat("JumpPhase", 4);
                jumpState = JumpState.JumpingDown;
                Debug.Log("시간 초과 또는 피크 미감지: 강제로 JumpingDown 상태로 전환");
            }
            
            // 착지 확인 부분
            float landingStartTime = Time.time;
            bool hasLanded = false;
            float initialHeight = transform.position.y;
            
            // 착지 조건이 만족될 때까지 대기 (최대 1초)
            while (Time.time - landingStartTime < 1.0f && !hasLanded)
            {
                bool isGrounded = playerController != null && playerController.IsGrounded();
                float currentHeight = transform.position.y;
                
                // 더 정밀한 착지 감지 - 매우 낮은 높이 또는 IsGrounded가 true일 때
                if (isGrounded || currentHeight <= 0.05f)
                {
                    hasLanded = true;
                    break;
                }
                
                yield return null; // 매 프레임 체크
            }
            
            // 부드러운 착지를 위한 보간
            if (hasLanded)
            {
                float smoothTime = 0.15f;
                float elapsedTime = 0f;
                Vector3 startPos = transform.position;
                Vector3 targetPos = new Vector3(startPos.x, 0f, startPos.z);
                
                // 물리 정지
                if (playerController != null && playerController.rb != null)
                {
                    playerController.rb.isKinematic = true;
                }
                
                // 부드럽게 위치 보정
                while (elapsedTime < smoothTime)
                {
                    elapsedTime += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsedTime / smoothTime);
                    transform.position = Vector3.Lerp(startPos, targetPos, t);
                    yield return null;
                }
                
                // 최종 위치 확정
                transform.position = targetPos;
                Debug.Log($"착지 위치 보정 완료: {transform.position}");
            }
            else
            {
                // 착지 감지 실패 시 강제로 위치 조정
                Debug.LogWarning("착지 감지 실패, 강제 위치 설정");
                Vector3 fixedPosition = transform.position;
                fixedPosition.y = 0f;
                transform.position = fixedPosition;
            }
            
            // 대기 시간
            yield return new WaitForSeconds(0.4f);
            
            // 모든 상태 변수 초기화
            jumpState = JumpState.None;
            isAnimationLocked = false;
            isKeyboardJump = false;
            currentAnimationName = "";
            
            // 롤 관련 변수 초기화
            currentRoll = 0f;
            minRoll = 0f;
            maxRoll = 0f;
            isWaitingForRollUp = false;
            
            // 점프 완료 플래그 설정
            isJumpInProgress = false;
            
            // 이동 상태 복원
            if (playerController != null)
                playerController.isMove = true;
            
            // 점프 전 상태에 따라 적절한 애니메이션으로 복귀
            if (wasBackwalkingBeforeJump) {
                // 백워킹 상태로 복귀
                if (playerController != null) {
                    playerController.isBackwalkingMode = true;
                }
                animator.CrossFade("BackWalking", 0.25f);
                Debug.Log("개선된 점프 완료, 백워킹 상태로 복귀");
                
                // 백워킹 상태 유지 코루틴 재시작
                StartCoroutine(MaintainBackwalkingState());
            } 
            else {
                // 일반 걷기 상태로 복귀
                if (playerController != null) {
                    playerController.isBackwalkingMode = false;
                }
                animator.CrossFade("Walk", 0.2f);
                Debug.Log("개선된 점프 완료, 걷기 상태로 복귀");
            }
        }
    }
        // StartBackWalk 메서드 개선 - 기존 메서드 수정
    private System.Collections.IEnumerator StartBackWalk()
    {
        Debug.Log("백워킹 모드 시작: 뒤로 걸어가기");
        
        if (playerController != null) 
        {
            // 백워킹 모드 설정 - 이동 방향은 반대로 바꾸지만 원 궤적은 유지
            playerController.isBackwalkingMode = true;
            
            // 중요: 여기서 회전 제어를 비활성화하지 않음 - Yaw 제어가 작동하도록 함
            playerController.disableRotationControl = false;
            
            // 현재 방향의 반대로 이동 방향 설정
            Vector3 currentDirection = transform.forward;
            playerController.SetMoveDirection(-currentDirection);
            
            // 원형 이동 상태인 경우 원형 이동 모드로 전환
            // 이때 백워킹 모드가 설정된 상태이므로 같은 원을 반대로 돌게 됨
            if (playerController.currentState == PlayerController.MovementState.Circle)
            {
                // 중요: 여기서 현재 각도를 유지하면서 원형 이동으로 전환
                playerController.SwitchToCircleMovementPublic();
            }
        }
        
        // 애니메이션 부드럽게 전환
        animator.CrossFade("BackWalking", 0.25f);
        LockAnimation("BackWalking");
        
        // 이동 상태 활성화
        if (playerController != null)
            playerController.isMove = true;
        
        // 백워킹 상태 유지 코루틴 시작
        StartCoroutine(MaintainBackwalkingState());
        
        Debug.Log("[백워킹 모드 설정 완료] Yaw 기반 방향 제어 및 Roll 기반 속도 제어 활성화됨");
        
        yield break;
    }

    // MaintainBackwalkingState 메서드 수정
    private System.Collections.IEnumerator MaintainBackwalkingState()
    {
        Debug.Log("백워킹 모드 유지 시작");
        
        // 뒤로 뛰기 상태 관련 변수
        bool isBackRunning = false;
        
        while (playerController != null && playerController.isBackwalkingMode)
        {
            // Roll 값에 따른 애니메이션 결정
            if (currentRoll >= 80f && !isBackRunning)
            {
                // Roll 값이 +90도 이상일 때 백런으로 전환
                animator.SetTrigger("BackRunTrigger");
                // 백런 애니메이션 속도 1.0으로 고정
                animator.speed = 1.0f;
                isBackRunning = true;
                Debug.Log($"BackRun 모드 시작: Roll={currentRoll:F1}°, 애니메이션 속도: 1.0 (고정)");
            }
            else if (currentRoll < 80f && isBackRunning)
            {
                // Roll 값이 +90도 미만일 때 다시 백워킹으로 전환
                animator.CrossFade("BackWalking", 0.35f);
                // 백워킹으로 돌아올 때는 롤 기반 속도 적용 (속도는 UpdateAnimationSpeed()에서 처리됨)
                isBackRunning = false;
                Debug.Log($"BackWalking 모드로 복귀: Roll={currentRoll:F1}°");
            }
            
            // 백런 중에만 애니메이션 속도를 1.0으로 고정
            if (isBackRunning)
            {
                animator.speed = 1.0f;
            }
            // 백워킹 중일 때는 속도를 고정하지 않음 (PlayerController의 UpdateAnimationSpeed()에서 처리)
            
            // 직선 이동 상태일 때 Yaw 중립 상태를 확인 (기존 코드 유지)
            if (playerController.currentState == PlayerController.MovementState.Straight &&
                !playerController.disableAutoReturnToCircle)
            {
                // 원형 이동으로의 자동 전환은 PlayerController에서 ProcessNeutralYaw를 통해 처리됨
            }
            
            yield return null;
        }
        
        Debug.Log("백워킹 모드 종료");
    }
   // UpdateAnimationLock 메서드 수정
    private void UpdateAnimationLock()
    {
        // 백워킹에서 걷기로 전환하려는 경우 특별 처리
        if (isAnimationLocked && currentAnimationName == "BackWalking" && lastFingers == 4)
        {
            // 손가락 5개 제스처가 감지되었을 때 백워킹 잠금 해제
            Debug.Log("백워킹 중 걷기 제스처 감지: 애니메이션 잠금 해제");
            isAnimationLocked = false;
            return;
        }
        
        // 기존 잠금 해제 로직
        if (isAnimationLocked && Time.time >= animationLockEndTime && jumpState == JumpState.None)
        {
            // BackWalking 상태인 경우 계속 유지 (자동으로 잠금 해제하지 않음)
            if (currentAnimationName == "BackWalking")
            {
                // BackWalking 상태 유지: 잠금 시간 갱신
                animationLockEndTime = Time.time + 10.0f; // 충분히 긴 시간으로 설정
                return;
            }
            
            // 다른 애니메이션은 기존처럼 잠금 시간이 끝나면 해제
            isAnimationLocked = false;
            Debug.Log($"애니메이션 '{currentAnimationName}' 완료, 잠금 해제됨");
        }
    }

    // 애니메이션 잠금 설정 (기존 코드 유지)
    private void LockAnimation(string animName)
    {
        if (animationDurations.TryGetValue(animName, out float duration))
        {
            isAnimationLocked = true;
            animationLockEndTime = Time.time + duration;
            currentAnimationName = animName;
            Debug.Log($"애니메이션 '{animName}' 시작, {duration}초 동안 잠금");
        }
    }

    // 손 제스처 처리 (기존 코드 유지)
    private void HandleHandGesture(int fingerCount, int palmOrientation)
    {
        // 점프 진행 중인지 확인 (isJumpInProgress)
        if (isJumpInProgress && fingerCount != 2 && jumpState != JumpState.None)
        {
            Debug.Log($"제스처 무시됨: 점프 진행 중 (fingerCount={fingerCount}, jumpState={jumpState})");
            return;
        }

        // 이전 코드와 동일한 제스처인지 확인
        bool isSameGesture = (fingerCount == lastFingers && palmOrientation == lastPalmOrientation);
        
        // 중요 수정: 점프가 완료된 상태(None)인 경우 같은 제스처(2)도 새로 허용
        bool allowRepeatedGesture = (jumpState == JumpState.None && !isAnimationLocked) || 
                                (fingerCount == 2 && jumpState == JumpState.None);
        
        if (isSameGesture && !allowRepeatedGesture)
        {
            Debug.Log("같은 제스처 무시됨");
            return;
        }
        
        // 백워킹 상태 확인 (기존과 동일)
        bool isInBackWalkingMode = playerController != null && playerController.isBackwalkingMode;
        
        // 중요: 백워킹 상태에서 걷기(4) 제스처는 항상 허용 - isAnimationLocked를 무시
        // 하지만 점프 중에는 무시
        if ((isAnimationLocked || isJumpInProgress) && 
            !(fingerCount == 4 && isInBackWalkingMode && !isJumpInProgress))
        {
            Debug.Log($"제스처 무시됨: 애니메이션 잠금 상태 (isAnimationLocked={isAnimationLocked}) 또는 점프 중 (isJumpInProgress={isJumpInProgress})");
            return;
        }
        
        // 중요 변경: 점프 완료 상태(None)일 때는 점프(2) 제스처를 허용하도록 조건 수정
        if ((jumpState == JumpState.Completing || jumpState == JumpState.Jumping ||
            jumpState == JumpState.JumpAir || jumpState == JumpState.JumpingDown) && 
            !(jumpState == JumpState.None && fingerCount == 2))
        {
            Debug.Log($"제스처 무시됨: 점프 진행 중 (jumpState={jumpState})");
            return;
        }
        
        // 여기서 lastFingers와 lastPalmOrientation를 업데이트 (중요!)
        lastFingers = fingerCount;
        lastPalmOrientation = palmOrientation;
        
        Debug.Log($"제스처 처리: 손가락 {fingerCount}개, 상태 허용됨");
    
    
    switch (fingerCount)
    {
        case 4: // 걷기
            StopAllCoroutines();
            jumpState = JumpState.None;
            
            lastFingers = -1;
            
            // 백워킹 상태인 경우 특별히 처리
            if (playerController != null && playerController.isBackwalkingMode)
            {
                // 백워킹 상태에서는 애니메이션 잠금을 강제로 해제 (중요!)
                isAnimationLocked = false;
                currentAnimationName = "";
                StopAllCoroutines();
                
                Debug.Log("백워킹에서 걷기로 전환: 회전 애니메이션 시작");
                StartCoroutine(TurnToWalk());
            }
            else
            {
                // 일반 걷기 모드로 즉시 전환
                if (playerController != null)
                {
                    playerController.isMove = true;
                    playerController.isBackwalkingMode = false;
                    playerController.disableRotationControl = false;
                }
                animator.SetTrigger("WalkTrigger");
                isAnimationLocked = false; // 걷기는 언제든 중단 가능
            }
            break;
            
        case 2: // 점프 - 백워킹 모드 지원 추가
        StopAllCoroutines();
        
        // 점프 전 백워킹 상태 저장 (점프 후 복귀를 위해)
        wasBackwalkingBeforeJump = isInBackWalkingMode;
        
        // W 키 입력과 동일한 초기화 과정
        isKeyboardJump = true;
        currentGyroY = 0f;
        lowestGyroY = 0f; 
        jumpState = JumpState.None;
        jumpBase = true;
        jumpReady = false;
        jumping = false;
        
        // 롤 관련 변수 초기화
        currentRoll = 0f;
        minRoll = 0f;
        maxRoll = 0f;
        isWaitingForRollUp = false;
        
        // 점프 중 플래그 설정
        isJumpInProgress = true;
        
        // 중요: 이동 상태를 즉시 비활성화 (수정된 부분)
        if (playerController != null)
            playerController.isMove = false; // 즉시 캐릭터 이동 중지
        
        // 백워킹 모드인지에 따라 시작 애니메이션 다르게 설정
        if (isInBackWalkingMode) {
            animator.CrossFade("BackWalking", 0.2f);
            Debug.Log("손가락 2개 제스처: 백워킹에서 점프 준비 시작, 롤 각도 하강 대기 중(이동 중지)");
        } else {
            animator.CrossFade("Jump", 0, 0); // 직접 Jump 애니메이션의 처음으로 시작
            animator.SetFloat("JumpPhase", 0); // JumpBase 상태로 즉시 설정
            Debug.Log("손가락 2개 제스처: 점프 준비(JumpBase) 상태로 즉시 전환(이동 중지)");
        }
        
        StartCoroutine(ImprovedJump());
        break;
                
            case 1: // 뒤로 걷기 - 수정
                StopAllCoroutines();
                jumpState = JumpState.None;
                
                Debug.Log("Turn 애니메이션 후 뒤로 걷기 시작");
                
                // 먼저 Turn 애니메이션을 실행한 후 백워킹 상태로 전환
                StartCoroutine(StartBackWalk());
                break;

            // case 3: // 캣워크
            //     StopAllCoroutines();
            //     jumpState = JumpState.None;
            //     animator.SetTrigger("CatWalkTrigger");
            //     LockAnimation("CatWalk");
            //     break;
            // case 4: // 웅크리기
            //     StopAllCoroutines();
            //     jumpState = JumpState.None;
            //     animator.SetTrigger("CrouchTrigger");
            //     LockAnimation("Crouch");
            //     break;
            // case 0: // 달리기
            //     StopAllCoroutines();
            //     jumpState = JumpState.None;
            //     animator.SetTrigger("RunTrigger");
            //     LockAnimation("Run");
            //     break;
        }
    }
    // 새로운 코루틴 추가: 백워킹에서 걷기로 전환하는 회전 애니메이션
    // 백워킹에서 걷기로 전환하는 회전 애니메이션 함수 수정
    private System.Collections.IEnumerator TurnToWalk()
    {
        Debug.Log("백워킹에서 일반 걷기로 전환");
        
        // 백워킹 관련 코루틴 중지
        StopCoroutine("MaintainBackwalkingState");
        
        // 백워킹 모드 즉시 해제
        if (playerController != null)
        {
            playerController.isBackwalkingMode = false;
            playerController.disableRotationControl = false;
            
            // 현재 방향으로 다시 걷기 시작 (백워킹 때는 반대 방향으로 움직였으므로)
            Vector3 currentDirection = playerController.GetCurrentMoveDirection();
            playerController.SetMoveDirection(-currentDirection); // 다시 원래 방향으로
        }
        
        // 걷기 애니메이션으로 부드럽게 전환
        animator.CrossFade("Walk", 0.25f);
        
        // 잠금 상태 해제
        isAnimationLocked = false;
        currentAnimationName = "";
        
        // 이동 상태 유지
        if (playerController != null)
            playerController.isMove = true;
        
        Debug.Log("일반 걷기 모드로 전환 완료");
        
        yield break;
    }
    
    // OnAnimatorIK 메서드 개선 - GyroY 대신 Roll 값 사용
    void OnAnimatorIK(int layerIndex)
    {
        // IK는 점프 준비 단계에서만 적용
        if (jumpState != JumpState.WaitingForUp){
            return;
        }
        
        // 애니메이션 시작 후 일정 시간(0.1초) 동안은 IK 적용 지연
        float delayTime = 0.1f;
        if (Time.time < readyAnimStartTime + delayTime) {
            // 아직 지연 시간이 지나지 않음 - IK 적용 안 함
            return;
        }
        
        // Roll 값에 따른 웅크림 강도 계산 (GyroY 대신 Roll 사용)
        float negativeRoll = Mathf.Abs(minRoll);
        
        // 웅크림 깊이를 Roll 각도 범위로 조정 (-80도 ~ -10도)
        float clampedRoll = Mathf.Clamp(negativeRoll, 10f, 80f);
        float squatIntensity = Mathf.InverseLerp(10f, 80f, clampedRoll);
        
        // 지연 시간 경과 후 부드러운 적용을 위한 추가 보간
        float timeSinceStart = Time.time - readyAnimStartTime - delayTime;
        float fadeInTime = 0.15f; // 0.15초 동안 부드럽게 IK 효과 적용
        float fadeInMultiplier = Mathf.Clamp01(timeSinceStart / fadeInTime);
        
        // IK 가중치 설정 - 페이드인 효과 적용
        float ikWeight = squatIntensity * 0.7f * fadeInMultiplier;
        
        // 디버그 로그 추가 - 웅크림 정보 출력
        if (Time.frameCount % 30 == 0) // 모든 프레임에 출력하면 너무 많으므로 30프레임마다 출력
        {
            Debug.Log($"[웅크림 IK] Roll: {minRoll:F1}°, 강도: {squatIntensity:P0}, IK 가중치: {ikWeight:F2}");
        }
        
        // 엉덩이 위치 조절 (아래로 내리기)
        if (animator.isHuman)
        {
            // 기본 웅크림 깊이: 캐릭터 키의 약 25%에서 40%로 강화
            float characterHeight = 1.8f; // 일반적인 캐릭터 키 (미터)
            float maxSquatDepth = characterHeight * 0.5f; 
            
            // 타겟 위치 설정
            Vector3 hipPosition = animator.bodyPosition;
            hipPosition.y -= squatIntensity * maxSquatDepth;
            
            // 엉덩이와 다리 관절에 IK 적용
            animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, ikWeight);
            animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, ikWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, ikWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, ikWeight);
            
            // 신체 중심 위치 조절 (허리 굽히기 효과)
            animator.bodyPosition = Vector3.Lerp(animator.bodyPosition, hipPosition, ikWeight * 0.5f);
            
            // 발 위치는 고정 (지면에 붙이기)
            Vector3 leftFootPos = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 rightFootPos = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            
            // 발 위치의 y값만 그대로 유지
            animator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootPos);
            animator.SetIKPosition(AvatarIKGoal.RightFoot, rightFootPos);
        }
    }

// 클래스 멤버 변수 추가
private float characterOriginalHeight = 0f;  // 캐릭터 원래 높이 저장용
    
    // 롤 각도 디버그용 시각화
//     void OnGUI()
//     {
//         if (jumpState == JumpState.WaitingForDown || jumpState == JumpState.WaitingForUp)
//         {
//             GUI.Label(new Rect(10, 10, 300, 20), $"현재 롤 각도: {currentRoll:F1}° | 최소값: {minRoll:F1}°");
            
//             if (isWaitingForRollUp)
//             {
//                 // 웅크림 강도 계산 및 표시
//                 float negativeRoll = Mathf.Abs(minRoll);
//                 float clampedRoll = Mathf.Clamp(negativeRoll, 10f, 80f); 
//                 float squatIntensity = Mathf.InverseLerp(10f, 80f, clampedRoll);
                
//                 GUI.Label(new Rect(10, 30, 300, 20), $"웅크림 감지됨! 위로 올리세요 (강도: {squatIntensity:F2})");
                
//                 // 강도를 시각적으로 보여주는 막대 그래프 추가
//                 GUI.Box(new Rect(10, 50, 200, 20), "");
//                 GUI.Box(new Rect(10, 50, 200 * squatIntensity, 20), "");
//             }
//         }
//     }
 }

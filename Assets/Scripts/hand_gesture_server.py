import cv2
import mediapipe as mp
import numpy as np
from collections import deque
import websocket
import json
import time

# 웹소켓 클라이언트 설정
# HOST = "192.168.45.34"  # 집
HOST = "192.168.0.213"  # 연구실
PORT = 5678  # Unity 웹소켓 서버 포트 (기존 스크립트의 포트와 일치)
ws = None

# 쿨다운 관련 변수 추가 (웹소켓 연결 코드 위에)
JUMP_GESTURE_COUNT = 2  # 점프에 해당하는 손가락 개수
JUMP_COOLDOWN_TIME = 0.8  # 동일 점프 제스처 재전송까지의 대기 시간(초)
last_jump_time = 0  # 마지막 점프 제스처 전송 시간


def connect_websocket():
    global ws
    ws_url = f"ws://{HOST}:{PORT}"
    try:
        ws = websocket.create_connection(ws_url)
        print(f"웹소켓 서버에 연결됨: {ws_url}")
        return True
    except Exception as e:
        print(f"웹소켓 연결 실패: {e}")
        return False


# hand_gesture_server.py - MediaPipe Hands 설정 수정
# MediaPipe 손 인식 초기화 - 민감도 조정
mp_hands = mp.solutions.hands
mp_draw = mp.solutions.drawing_utils
hands = mp_hands.Hands(
    max_num_hands=1,
    min_detection_confidence=0.6,  # 감지 민감도 높임 (0.6 → 0.3)
    min_tracking_confidence=0.6,  # 추적 민감도 높임 (0.6 → 0.3)
    model_complexity=1,  # 모델 복잡도 증가 (더 정확하지만 느림)
)

# 손가락 끝 인덱스
tip_ids = [4, 8, 12, 16, 20]
# 안정화를 위한 최근 감지 결과 저장 (5프레임)
recent_hand_types = deque(maxlen=5)
recent_finger_counts = deque(maxlen=5)

# 웹캠 시작
cap = cv2.VideoCapture(0)

# 사용자에게 손바닥 방향 모드 선택 요청
print("손바닥 방향 모드를 선택하세요:")
print("1: 수직 모드 (손바닥이 옆을 향함)")
print("2: 수평 모드 (손바닥이 바닥 또는 천장을 향함)")

# 유효한 입력이 들어올 때까지 반복
while True:
    try:
        palm_orientation = int(input("모드 선택 (1 또는 2): "))
        if palm_orientation in [1, 2]:
            break
        else:
            print("잘못된 입력입니다. 1 또는 2를 입력하세요.")
    except ValueError:
        print("숫자만 입력하세요.")

# 선택된 모드 출력
orientation_text = "수직" if palm_orientation == 1 else "수평"
print(f"선택된 모드: {orientation_text} 모드 ({palm_orientation})")


def count_fingers(hand_landmarks, handedness):
    fingers = []

    # 손의 종류 확인 (왼손/오른손)
    hand_type = handedness.classification[0].label

    # 엄지 손가락 랜드마크
    thumb_tip = hand_landmarks.landmark[4]
    thumb_ip = hand_landmarks.landmark[3]
    thumb_mcp = hand_landmarks.landmark[2]

    if palm_orientation == 1:  # 수직 모드
        # 기존 수직 모드 코드는 그대로 유지
        if hand_type == "Right":  # 오른손
            # 오른손에서 엄지가 펴지면 엄지 끝이 검지 방향으로 가게 됨
            if thumb_tip.x < thumb_ip.x and thumb_ip.x < thumb_mcp.x:
                fingers.append(1)
            else:
                fingers.append(0)
        else:  # 왼손
            # 왼손에서 엄지가 펴지면 엄지 끝이 검지 반대 방향으로 가게 됨
            if thumb_tip.x > thumb_ip.x and thumb_ip.x > thumb_mcp.x:
                fingers.append(1)
            else:
                fingers.append(0)

        # 나머지 손가락 (수직 모드)
        other_tip_ids = [8, 12, 16, 20]

        for i, tip_id in enumerate(other_tip_ids):
            # PIP 관절(중간 관절)과 비교
            pip_id = tip_id - 2

            # 손가락 끝이 PIP 관절보다 위에 있는지 확인
            is_straight = (
                hand_landmarks.landmark[tip_id].y < hand_landmarks.landmark[pip_id].y
            )

            # 손가락이 펴져 있으면 1, 아니면 0 추가
            if is_straight:
                fingers.append(1)
            else:
                fingers.append(0)

    else:  # 수평 모드 (palm_orientation == 2) - 완전히 새로운 방식
        # 손바닥이 카메라를 향할 때의 손가락 인식

        # 1. 엄지손가락 처리 (수평 모드) - 단순화된 버전
        # 엄지는 다른 손가락들과 달리 옆으로 펴짐

        # 손바닥 중심점 계산 (검지와 새끼 MCP 사이)
        index_mcp = np.array(
            [
                hand_landmarks.landmark[5].x,
                hand_landmarks.landmark[5].y,
                hand_landmarks.landmark[5].z,
            ]
        )

        pinky_mcp = np.array(
            [
                hand_landmarks.landmark[17].x,
                hand_landmarks.landmark[17].y,
                hand_landmarks.landmark[17].z,
            ]
        )

        palm_center = (index_mcp + pinky_mcp) / 2

        # 엄지 관절 좌표
        thumb_cmc = np.array(
            [
                hand_landmarks.landmark[1].x,
                hand_landmarks.landmark[1].y,
                hand_landmarks.landmark[1].z,
            ]
        )

        thumb_mcp_arr = np.array(
            [
                hand_landmarks.landmark[2].x,
                hand_landmarks.landmark[2].y,
                hand_landmarks.landmark[2].z,
            ]
        )

        thumb_ip_arr = np.array(
            [
                hand_landmarks.landmark[3].x,
                hand_landmarks.landmark[3].y,
                hand_landmarks.landmark[3].z,
            ]
        )

        thumb_tip_arr = np.array(
            [
                hand_landmarks.landmark[4].x,
                hand_landmarks.landmark[4].y,
                hand_landmarks.landmark[4].z,
            ]
        )

        # 수평모드에서 엄지는 X축 차이로 판단 (왼손/오른손 구분)
        if hand_type == "Right":  # 오른손
            # 오른손은 엄지를 펴면 tip의 x좌표가 ip보다 작아짐 (왼쪽으로 펴짐)
            is_thumb_extended = thumb_tip_arr[0] < thumb_ip_arr[0] - 0.0015
        else:  # 왼손
            # 왼손은 엄지를 펴면 tip의 x좌표가 ip보다 커짐 (오른쪽으로 펴짐)
            is_thumb_extended = thumb_tip_arr[0] > thumb_ip_arr[0] + 0.02

        fingers.append(1 if is_thumb_extended else 0)

        # 디버깅용 (엄지)
        print(
            f"엄지 - X차이: {abs(thumb_tip_arr[0] - thumb_ip_arr[0]):.4f}, 펴짐: {is_thumb_extended}"
        )

        # 2. 나머지 손가락 처리 (수평 모드) - 개선된 버전
        for i, tip_id in enumerate([8, 12, 16, 20]):
            # 관절 포인트
            pip_id = tip_id - 2  # PIP 관절
            mcp_id = tip_id - 3  # MCP 관절
            dip_id = tip_id - 1  # DIP 관절 (추가)

            # 손가락 관절 좌표 가져오기
            mcp = np.array(
                [
                    hand_landmarks.landmark[mcp_id].x,
                    hand_landmarks.landmark[mcp_id].y,
                    hand_landmarks.landmark[mcp_id].z,
                ]
            )

            pip = np.array(
                [
                    hand_landmarks.landmark[pip_id].x,
                    hand_landmarks.landmark[pip_id].y,
                    hand_landmarks.landmark[pip_id].z,
                ]
            )

            dip = np.array(
                [
                    hand_landmarks.landmark[dip_id].x,
                    hand_landmarks.landmark[dip_id].y,
                    hand_landmarks.landmark[dip_id].z,
                ]
            )

            tip = np.array(
                [
                    hand_landmarks.landmark[tip_id].x,
                    hand_landmarks.landmark[tip_id].y,
                    hand_landmarks.landmark[tip_id].z,
                ]
            )

            # 1. 각도 계산 (세 관절 사이 각도)
            # PIP를 중심으로 한 각도 계산 (MCP-PIP-DIP 각도)
            v1 = mcp - pip
            v2 = dip - pip

            if np.linalg.norm(v1) * np.linalg.norm(v2) > 0:  # 0으로 나누기 방지
                dot_product = np.dot(v1, v2) / (np.linalg.norm(v1) * np.linalg.norm(v2))
                angle1 = np.degrees(np.arccos(np.clip(dot_product, -1.0, 1.0)))
            else:
                angle1 = 0

            # DIP를 중심으로 한 각도 계산 (PIP-DIP-TIP 각도)
            v3 = pip - dip
            v4 = tip - dip

            if np.linalg.norm(v3) * np.linalg.norm(v4) > 0:  # 0으로 나누기 방지
                dot_product = np.dot(v3, v4) / (np.linalg.norm(v3) * np.linalg.norm(v4))
                angle2 = np.degrees(np.arccos(np.clip(dot_product, -1.0, 1.0)))
            else:
                angle2 = 0

            # 2. 손가락이 펴져 있으면 두 각도가 모두 크게 나타남 (160도 이상)
            # 주먹을 쥐면 각도가 작아짐 (90도 이하)
            angle_straight = angle1 > 140 and angle2 > 140

            # 3. 손가락 끝과 중간 관절의 높이(Y) 비교
            # 손바닥이 바닥을 향하면 펴진 손가락은 PIP보다 TIP의 Y값이 더 작음(위에 위치)
            y_diff = pip[1] - tip[1]
            y_straight = y_diff > 0.04  # 임계값 상향 조정

            # 4. 깊이(Z) 비교 - 펴진 손가락은 MCP에서 TIP까지 깊이가 일정하게 유지됨
            z_diff_mcp_tip = abs(mcp[2] - tip[2])
            z_diff_pip_tip = abs(pip[2] - tip[2])
            z_straight = z_diff_mcp_tip < 0.05 and z_diff_pip_tip < 0.03

            # 새끼손가락에 대한 특별 처리 (tip_id == 20)
            if tip_id == 20:  # 새끼손가락일 경우
                # 1. 기존 조건들은 유지
                angle_straight = angle1 > 150 and angle2 > 150
                y_diff = pip[1] - tip[1]
                y_straight = y_diff > 0.05
                z_straight = z_diff_mcp_tip < 0.04 and z_diff_pip_tip < 0.03

                # 2. 추가: 약지(16)와 새끼손가락(20) 관계 확인
                ring_tip = np.array(
                    [
                        hand_landmarks.landmark[16].x,
                        hand_landmarks.landmark[16].y,
                        hand_landmarks.landmark[16].z,
                    ]
                )

                # 약지와 새끼손가락 끝의 Y축 위치 비교
                # 새끼손가락이 접혔다면 약지보다 Y값이 더 크거나(아래에 위치) 비슷해야 함
                y_relative_to_ring = tip[1] >= (ring_tip[1] - 0.01)

                # 약지와 새끼손가락의 X축 거리 확인 (같은 방향으로 펴져 있다면 X축 거리가 일정함)
                x_dist_to_ring = abs(tip[0] - ring_tip[0])
                normal_x_dist = 0.04  # 일반적인 X축 거리
                x_spacing_normal = abs(x_dist_to_ring - normal_x_dist) < 0.02

                # 3. 최종 판정: 모든 기존 조건을 만족하고, 추가 조건도 만족해야 함
                is_finger_extended = (
                    angle_straight and (y_straight or z_straight)
                ) and (x_spacing_normal or y_relative_to_ring)
            else:
                # 다른 손가락들은 기존 로직 유지
                angle_straight = angle1 > 140 and angle2 > 140
                y_diff = pip[1] - tip[1]
                y_straight = y_diff > 0.04
                z_straight = z_diff_mcp_tip < 0.05 and z_diff_pip_tip < 0.03
                is_finger_extended = (angle_straight and y_straight) or (
                    angle_straight and z_straight
                )

            fingers.append(1 if is_finger_extended else 0)

    return sum(fingers), fingers


# 웹소켓 연결 시도
if not connect_websocket():
    print("웹소켓 연결 실패. 5초 후 재시도합니다.")
    time.sleep(5)
    if not connect_websocket():
        print("웹소켓 연결 실패. 프로그램을 종료합니다.")
        if cap.isOpened():
            cap.release()
        cv2.destroyAllWindows()
        exit()

print("손가락 제스처 인식 시작!")
last_sent_finger_count = -1  # 마지막으로 전송된 손가락 개수
jump_gesture_active = False  # 점프 제스처 활성화 상태 추적

while True:
    success, img = cap.read()
    if not success:
        break

    # img = cv2.flip(img, 1)

    img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    results = hands.process(img_rgb)

    # 기본 정보 표시
    cv2.putText(
        img,
        "Press 'Q' to quit",
        (10, img.shape[0] - 10),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.5,
        (255, 255, 255),
        1,
    )

    # 모드 정보 항상 표시
    orientation_text = "수직" if palm_orientation == 1 else "수평"
    cv2.putText(
        img,
        f"모드: {orientation_text}",
        (img.shape[1] - 150, 30),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        (0, 165, 255),
        2,
    )

    finger_count = 0
    finger_states = [0, 0, 0, 0, 0]  # 기본값 설정

    if results.multi_hand_landmarks and results.multi_handedness:
        for idx, hand_landmarks in enumerate(results.multi_hand_landmarks):
            mp_draw.draw_landmarks(img, hand_landmarks, mp_hands.HAND_CONNECTIONS)

            # 손의 종류 정보 가져오기
            handedness = results.multi_handedness[idx]
            hand_type = handedness.classification[0].label
            confidence = handedness.classification[0].score

            # 손 종류 안정화 (최근 5프레임 중 가장 많은 타입으로 결정)
            recent_hand_types.append(hand_type)
            stable_hand_type = max(set(recent_hand_types), key=recent_hand_types.count)

            # 손 타입과 신뢰도 표시
            cv2.putText(
                img,
                f"Hand ({confidence:.2f})",
                (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.7,
                (0, 255, 0),
                2,
            )

            # 수정된 함수 호출 - 고정된 방향으로 손가락 감지
            finger_count, finger_states = count_fingers(hand_landmarks, handedness)

            # 손가락 카운트 안정화
            recent_finger_counts.append(finger_count)
            stable_finger_count = max(
                set(recent_finger_counts), key=recent_finger_counts.count
            )

            # 각 손가락 상태 표시
            finger_names = ["Thumb", "Index", "Middle", "Ring", "Pinky"]
            for i, state in enumerate(finger_states):
                color = (
                    (0, 255, 0) if state == 1 else (0, 0, 255)
                )  # 펴짐: 녹색, 접힘: 빨간색
                cv2.putText(
                    img,
                    f"{finger_names[i]}: {state}",
                    (10, 60 + i * 25),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    color,
                    2,
                )

            # 안정화된 손가락 개수 표시
            cv2.putText(
                img,
                f"Count: {stable_finger_count}",
                (10, 200),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                (255, 255, 0),
                2,
            )

            # 결과 출력 및 Unity로 웹소켓 전송
            try:
                # 동작이 변경되었을 때만 웹소켓으로 전송하는 로직
                if stable_finger_count != last_sent_finger_count:
                    # 점프 제스처(2개 손가락) 상태 관리
                    if stable_finger_count == JUMP_GESTURE_COUNT:
                        # 점프 제스처가 활성화 상태가 아닐 때만 전송
                        if not jump_gesture_active:
                            message = f"HAND:{stable_finger_count},{palm_orientation}"
                            ws.send(message)
                            print(
                                f"손가락 개수 {stable_finger_count}개 점프 제스처 감지 (웹소켓 전송)"
                            )
                            jump_gesture_active = True  # 점프 제스처 활성화 상태로 표시
                            last_sent_finger_count = stable_finger_count
                        else:
                            # 이미 점프 제스처가 활성화된 상태면 전송하지 않음
                            print(
                                f"손가락 개수 {stable_finger_count}개 점프 제스처 유지 (전송 안함)"
                            )
                    else:
                        # 다른 손가락 개수일 경우 항상 전송
                        message = f"HAND:{stable_finger_count},{palm_orientation}"
                        ws.send(message)
                        print(
                            f"손가락 개수 변경: {last_sent_finger_count} → {stable_finger_count}, 방향: {orientation_text} (웹소켓 전송)"
                        )
                        last_sent_finger_count = stable_finger_count

                        # 점프 제스처가 아닌 다른 제스처로 변경되면, 점프 활성화 상태 해제
                        if jump_gesture_active:
                            jump_gesture_active = False
                else:
                    # 같은 값이면 전송하지 않고 로컬 콘솔에만 출력
                    status = (
                        "점프 유지 중"
                        if stable_finger_count == JUMP_GESTURE_COUNT
                        and jump_gesture_active
                        else "유지"
                    )
                    print(
                        f"손가락 개수 유지: {stable_finger_count}, 방향: {orientation_text} ({status}, 전송 안함)"
                    )
            except Exception as e:
                print(f"웹소켓 전송 오류: {e}")
                # 다시 연결 시도
                if connect_websocket():
                    print("웹소켓 재연결 성공")
                    try:
                        # 재연결 성공 시에는 현재 상태 무조건 전송
                        message = f"HAND:{stable_finger_count},{palm_orientation}"
                        ws.send(message)
                        last_sent_finger_count = stable_finger_count
                        # 점프 제스처 상태도 업데이트
                        jump_gesture_active = stable_finger_count == JUMP_GESTURE_COUNT
                    except:
                        print("재연결 후에도 전송 실패")

    cv2.imshow("Hand Tracking", img)
    if cv2.waitKey(1) & 0xFF == ord("q"):
        break

# 정리
if ws:
    ws.close()
cap.release()
cv2.destroyAllWindows()

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;

public class DataTransServer : MonoBehaviour
{
    public int port = 5678;
    private WebSocketServer wss;

    void Start()
    {   
        //string ip = "192.168.45.34"; //집
        string ip = "192.168.0.213"; // 연구실
        wss = new WebSocketServer($"ws://{ip}:{port}");
        wss.AddWebSocketService<WatchDataTransBehavior>("/watch");
        wss.AddWebSocketService<DOTDataTransBehavior>("/dot");
        
        // 기존 경로도 하위 호환성을 위해 유지 (선택사항)
        wss.AddWebSocketService<DataTransBehavior>("/");
        
        wss.Start();
        Debug.Log($"WebSocket 서버가 ws://{ip}:{port}/watch, ws://{ip}:{port}/dot 에서 시작되었습니다.");
    }

    void OnApplicationQuit()
    {
        if (wss != null)
        {
            wss.Stop();
        }
    }
    
    void Update()
    {
        // 모든 처리기의 메인 스레드 액션 실행
        DataTransBehavior.ExecuteMainThreadActions();
        WatchDataTransBehavior.ExecuteMainThreadActions();
        DOTDataTransBehavior.ExecuteMainThreadActions();
    }
    
    private string GetLocalIPAddress()
    {
        string localIP = "127.0.0.1";
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    break;
                }
            }
        }
        catch (Exception e)
        {
            // Debug.LogError("Error getting local IP: " + e.Message);
        }
        return localIP;
    }
}


public class DataTransBehavior : WebSocketBehavior
{
    // 이벤트 정의는 그대로 유지
    public static event Action<float> OnNewGyroZ;
    public static event Action<float> OnNewGyroY;
    public static event Action<float> OnNewQuatX;
    public static event Action<int, int> OnNewHandGesture;
    public static event Action<float> OnNewYaw;
    public static event Action<float> OnNewPitch;
    public static event Action<float> OnNewRoll;   // 롤(Roll) - 필요시

    // 스레드 안전한 큐 추가
    public static ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    protected override void OnOpen()
    {
        Debug.Log("DataTransBehavior 클라이언트 연결됨!");
    }
    protected override void OnMessage(MessageEventArgs e)
    {
        string message = e.Data;
        // --- JSON 형식의 dotSensorData 처리 ---
        if (message.StartsWith("{") && message.Contains("\"type\":\"dotSensorData\""))
        {
            try
            {
                // deviceId 추출
                var idMatch = Regex.Match(message, "\"deviceId\":\"(.*?)\"");
                string deviceId = idMatch.Success ? idMatch.Groups[1].Value : "";

                // mainThreadActions.Enqueue(() => {
                //     Debug.Log($"DOT JSON 전체: {message}");
                // });

                if (deviceId == "DOT1")
                {
                    // DOT1 → Roll만 처리
                    var rollMatch = Regex.Match(message, "\"r\":\"(-?\\d+\\.?\\d*)\"");
                    if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, out float rollValue))
                    {
                        mainThreadActions.Enqueue(() => {
                            OnNewRoll?.Invoke(rollValue);
                            Debug.Log($"DOT1 Roll: {rollValue}°");
                        });
                    }
                }
                else if (deviceId == "DOT2")
                {
                    // DOT2 → Pitch만 처리
                    var pitchMatch = Regex.Match(message, "\"p\":\"(-?\\d+\\.?\\d*)\"");
                    if (pitchMatch.Success && float.TryParse(pitchMatch.Groups[1].Value, out float pitchValue))
                    {
                        mainThreadActions.Enqueue(() => {
                            OnNewPitch?.Invoke(pitchValue);
                            Debug.Log($"DOT2 Pitch: {pitchValue}°");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                mainThreadActions.Enqueue(() =>
                    Debug.LogError($"JSON DOT 파싱 실패: {ex.Message}, 데이터: {message}"));
            }
        }
        else if (message.StartsWith("DOT:"))
        {
            try {
                string data = message.Substring(4).Trim();
                
                //   mainThreadActions.Enqueue(() => {
                //       Debug.Log($"DOT 데이터 전체: {data}");
                //   });  

                // 정규표현식을 사용하여 r: 패턴 찾기
                var rollMatch = Regex.Match(data, @"r:(-?\d+\.?\d*)");
                if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, out float rollValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewRoll?.Invoke(rollValue);
                        //Debug.Log($"DOT: Roll 값(도): {rollValue}°");
                    });
                }
                
                // 필요할 경우 다른 값들도 유사하게 파싱 가능
                // 예: 시간(t) 값 파싱
                var timeMatch = Regex.Match(data, @"t:(\d+\.?\d*)");
                if (timeMatch.Success)
                {
                    // 필요 시 시간 값 처리
                    // float timeValue = float.Parse(timeMatch.Groups[1].Value);
                }
            }
            catch (Exception ex)
            {
                mainThreadActions.Enqueue(() => 
                    Debug.LogError($"DOT 데이터 파싱 실패: {ex.Message}, 데이터: {message}"));
            }
        }
        // DataTransBehavior 클래스의 OnMessage 메소드 내 W: 처리 부분 수정
        else if (message.StartsWith("W:"))
        {
            try
            {
                string data = message.Substring(2).Trim(); // "W:" 제거
                
                // 새로운 형식 파싱
                float yawValue = 0;
                
                // "y:값" 패턴 찾기
                var yawMatch = Regex.Match(data, @"y:(-?\d+\.?\d*)");
                if (yawMatch.Success && float.TryParse(yawMatch.Groups[1].Value, out yawValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewYaw?.Invoke(yawValue);
                        //Debug.Log($"Watch: Yaw 값(도): {yawValue}°");
                    });
                }
                mainThreadActions.Enqueue(() => {
                    Debug.Log($"123WATCH 데이터 전체: {data}");
                });       
                // 필요한 경우 roll과 pitch도 유사하게 파싱
                var rollMatch = Regex.Match(data, @"r:(-?\d+\.?\d*)");
                if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, out float rollValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewRoll?.Invoke(rollValue);
                    });
                }
                
                var pitchMatch = Regex.Match(data, @"p:(-?\d+\.?\d*)");
                if (pitchMatch.Success && float.TryParse(pitchMatch.Groups[1].Value, out float pitchValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewPitch?.Invoke(pitchValue);
                    });
                }
            }
            catch (Exception ex)
            {
                mainThreadActions.Enqueue(() => 
                    Debug.LogError($"WATCH 데이터 파싱 실패: {ex.Message}, 데이터: {message}"));
            }
        }
        else if (message.StartsWith("{") && message.Contains("\"type\":\"watch\""))
        {
            try
            {
                // Watch에서는 Yaw만 사용
                var yawMatch = Regex.Match(message, "\"y\":\"(-?\\d+\\.?\\d*)\"");
                if (yawMatch.Success && float.TryParse(yawMatch.Groups[1].Value, out float yawValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewYaw?.Invoke(yawValue);
                        Debug.Log($"Watch Yaw: {yawValue}°");
                    });
                }
                // mainThreadActions.Enqueue(() => {
                //     Debug.Log($"WATCH JSON 전체: {message}");
                // });
            }
            catch (Exception ex)
            {
                mainThreadActions.Enqueue(() =>
                    Debug.LogError($"WATCH JSON 파싱 실패: {ex.Message}, 데이터: {message}"));
            }
        }
        else if (message.StartsWith("HAND:"))
        {
            try {
                string data = message.Substring(5).Trim();
                string[] parts = data.Split(',');
                if (parts.Length >= 2 && 
                    int.TryParse(parts[0], out int fingerCount) && 
                    int.TryParse(parts[1], out int palmOrientation))
                {
                    OnNewHandGesture?.Invoke(fingerCount, palmOrientation);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"HAND 데이터 파싱 실패: {ex.Message}");
            }
        }
        else
        {
            //  mainThreadActions.Enqueue(() => {
            //     Debug.Log($"[WS Receive /] {message}");
            // });
            // 처리되지 않은 메시지 형식
        }
    }

    public static void ExecuteMainThreadActions()
    {
        // -- 변경 전 --
//    int processCount = 0;
//    while (processCount < 10 && mainThreadActions.TryDequeue(out var action))
//    {
//        try { action?.Invoke(); }
//        catch (Exception ex) { Debug.LogError($"워치 액션 실행 중 오류: {ex.Message}"); }
//        processCount++;
//    }

    // -- 변경 후: 큐에 남은 모든 액션을 처리하도록 제한 해제 --
        while (mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"워치 액션 실행 중 오류: {ex.Message}");
            }
        }
    }
}
// 워치 데이터 전용 처리 클래스
public class WatchDataTransBehavior : WebSocketBehavior
{
    // 요(Yaw) 전용 이벤트
    public static event Action<float> OnNewYaw;
    
    // 스레드 안전한 큐
    public static ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    
    protected override void OnOpen()
    {
        Debug.Log("워치 클라이언트 연결됨!");
    }
    
    protected override void OnMessage(MessageEventArgs e)
    {
        string message = e.Data;
        Debug.Log($"[WATCH JSON] {message}");
        
        // 메시지 디버깅 로그 추가
        // mainThreadActions.Enqueue(() => {
        //     Debug.Log($"워치 메시지 수신: {message}");
        // });
        
        if (message.StartsWith("W:"))
        {
            string data = message.Substring(2); // "W:" 제거
            var matches = Regex.Matches(data, @"t:([\d\.]+),y:([\d\.\-]+)");
            
            if (matches.Count > 0)
            {
                var match = matches[0];
                float yaw = float.Parse(match.Groups[2].Value);
                
                // UI 스레드에서 실행되도록 큐에 추가
                mainThreadActions.Enqueue(() => {
                    OnNewYaw?.Invoke(yaw);
                    Debug.Log($"워치 Yaw: {yaw}°");
                });
            }
        }
        else if (message == "WATCH_SESSION_START" || message == "WATCH_SESSION_END")
        {
            Debug.Log($"워치 세션 이벤트: {message}");
        }
    }
    
    // 메인 스레드 액션 실행 메소드
    public static void ExecuteMainThreadActions()
    {
        int processCount = 0;
        while (processCount < 10 && mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"워치 액션 실행 중 오류: {ex.Message}");
            }
            processCount++;
        }
    }
}

// DOT 데이터 전용 처리 클래스
public class DOTDataTransBehavior : WebSocketBehavior
{
    // 롤(Roll) 전용 이벤트
    public static event Action<float> OnNewRoll;
    
    // 스레드 안전한 큐
    public static ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    
    protected override void OnOpen()
    {
        Debug.Log("DOT 클라이언트 연결됨!");
    }
    
    protected override void OnMessage(MessageEventArgs e)
    {
        string message = e.Data;
        Debug.Log($"[WS Receive DOT] {message}");
        
        // JSON 형식 처리 (새로 추가)
        if (message.StartsWith("{") && message.Contains("\"type\":\"dotSensorData\""))
        {
            try
            {
                mainThreadActions.Enqueue(() => {
                    Debug.Log($"DOT 데이터 전체: {message}");
                });
                
                // Roll 값 추출
                var rollMatch = Regex.Match(message, "\"r\":(-?\\d+\\.?\\d*)");
                if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, out float rollValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewRoll?.Invoke(rollValue);
                        Debug.Log($"DOT Roll: {rollValue}°");
                    });
                }
                
                // Yaw와 Pitch도 추출
                var yawMatch = Regex.Match(message, "\"y\":(-?\\d+\\.?\\d*)");
                if (yawMatch.Success && float.TryParse(yawMatch.Groups[1].Value, out float yawValue))
                {
                    mainThreadActions.Enqueue(() => {
                        Debug.Log($"DOT Yaw: {yawValue}°");
                    });
                }
                
                var pitchMatch = Regex.Match(message, "\"p\":(-?\\d+\\.?\\d*)");
                if (pitchMatch.Success && float.TryParse(pitchMatch.Groups[1].Value, out float pitchValue))
                {
                    mainThreadActions.Enqueue(() => {
                        Debug.Log($"DOT Pitch: {pitchValue}°");
                    });
                }
            }
            catch (Exception ex)
            {
                mainThreadActions.Enqueue(() => 
                    Debug.LogError($"JSON DOT 데이터 파싱 실패: {ex.Message}, 데이터: {message}"));
            }
        }
        // 기존 "DOT:" 처리 코드 유지
        else if (message.StartsWith("DOT:"))
        {
            // 기존 코드
        }
        else if (message == "DOT_SESSION_START" || message == "DOT_SESSION_END")
        {
            Debug.Log($"DOT 세션 이벤트: {message}");
        }
    
    }
    
    // 메인 스레드 액션 실행 메소드
    public static void ExecuteMainThreadActions()
    {
        int processCount = 0;
        while (processCount < 10 && mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"DOT 액션 실행 중 오류: {ex.Message}");
            }
            processCount++;
        }
    }
}
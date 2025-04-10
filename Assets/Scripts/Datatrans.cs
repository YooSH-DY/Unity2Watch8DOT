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
        wss.AddWebSocketService<DataTransBehavior>("/");
        wss.Start();
        Debug.Log($"WebSocket 서버가 ws://{ip}:{port}/ 에서 시작되었습니다.");
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
        // 메인 스레드에서 WebSocket 액션 실행
        DataTransBehavior.ExecuteMainThreadActions();
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
    
    protected override void OnMessage(MessageEventArgs e)
    {
        string message = e.Data;
        if (message.StartsWith("DOT:"))
        {
            try {
                string data = message.Substring(4).Trim();
                
                // mainThreadActions.Enqueue(() => {
                //     Debug.Log($"DOT 데이터 전체: {data}");
                // });  

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
                // mainThreadActions.Enqueue(() => {
                //     Debug.Log($"WAT CH 데이터 전체: {data}");
                // });       
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
            // 처리되지 않은 메시지 형식
        }
    }

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
                Debug.LogError($"액션 실행 중 오류: {ex.Message}");
            }
            processCount++;
        }
    }
}
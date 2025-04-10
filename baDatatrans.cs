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
        string ip = GetLocalIPAddress();
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
        if (message.StartsWith("GYRO:"))
        {
            string valueStr = message.Substring(5).Trim();
            if (float.TryParse(valueStr, out float gyroValue))
            {
                OnNewGyroZ?.Invoke(gyroValue);
            }
        }
        else if (message.StartsWith("DOT:"))
        {
            try {
                string data = message.Substring(4).Trim();
                
                string[] parts = data.Split(',');
                
                // Roll - 인덱스 7 (변경됨)
                if (parts.Length >= 8 && float.TryParse(parts[7].Trim(), out float rollValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewRoll?.Invoke(rollValue);
                        //Debug.Log($"DOT: Roll 값(도): {rollValue}°");
                    });
                }
            }
            catch (Exception ex)
            {
                mainThreadActions.Enqueue(() => 
                    Debug.LogError($"DOT 데이터 파싱 실패: {ex.Message}, 데이터: {message}"));
            }
        }
        else if (message.StartsWith("WATCH:"))
        {
            try
            {
                string data = message.Substring(6).Trim(); // "WATCH:" 제거
                string[] parts = data.Split(',');
                
                // 인덱스 확인 - 마지막 항목(인덱스 9)이 Yaw 값
                if (parts.Length >= 10 && float.TryParse(parts[9].Trim(), out float yawValue))
                {
                    mainThreadActions.Enqueue(() => {
                        OnNewYaw?.Invoke(yawValue);
                        Debug.Log($"Watch: Yaw 값(도): {yawValue}°");
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
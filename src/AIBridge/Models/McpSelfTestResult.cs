using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class McpSelfTestResult
{
    public bool ServerRunning { get; set; }
    public bool InitializePassed { get; set; }
    public bool ToolDiscoveryPassed { get; set; }
    public int ExpectedToolCount { get; set; } = 15;
    public int DiscoveredToolCount { get; set; }
    public List<string> DiscoveredTools { get; set; } = new();
    public bool PingPassed { get; set; }
    public bool AuthenticationPassed { get; set; }
    public bool OverallPassed { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime TestedAt { get; set; } = DateTime.Now;
    public string TargetEndpoint { get; set; } = string.Empty;

    public string SummaryText
    {
        get
        {
            if (OverallPassed)
            {
                return $"✓ Kiểm tra thành công! ({DiscoveredToolCount}/{ExpectedToolCount} công cụ sẵn sàng)";
            }
            return $"✕ Kiểm tra thất bại ({Errors.Count} lỗi)";
        }
    }
}

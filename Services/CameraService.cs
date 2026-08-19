using System.Collections.Generic;
using System.Linq;
using DirectShowLib;
using OpenCvSharp;

namespace CIPeekShield.Services;

public class CameraService : System.IDisposable
{
    private readonly object _lock = new();
    private VideoCapture? _cap;
    public int Index { get; private set; } = -1;
    public string? LastError { get; private set; }

    public bool IsOpen
    {
        get
        {
            lock (_lock) return _cap != null && _cap.IsOpened();
        }
    }

    public bool Open(int index)
    {
        lock (_lock)
        {
            try
            {
                CloseInternal();
                Index = index;
                _cap = new VideoCapture(index, VideoCaptureAPIs.DSHOW);

                _cap.Set(VideoCaptureProperties.FrameWidth, 640);
                _cap.Set(VideoCaptureProperties.FrameHeight, 480);
                if (!_cap.IsOpened())
                {
                    LastError = "无法打开摄像头（索引 " + index + "）";
                    return false;
                }
                LastError = null;
                return true;
            }
            catch (System.Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    public bool ReadFrame(Mat frame)
    {
        lock (_lock)
        {
            if (_cap == null) return false;
            try { return _cap.Read(frame); }
            catch { return false; }
        }
    }

    public void Close()
    {
        lock (_lock) CloseInternal();
    }

    private void CloseInternal()
    {
        if (_cap != null)
        {
            try { _cap.Release(); } catch { }
            try { _cap.Dispose(); } catch { }
            _cap = null;
        }
        Index = -1;
    }

    public void Dispose() => Close();

    public static List<(int index, string name)> Enumerate()
    {
        var list = new List<(int index, string name)>();
        try
        {
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            for (int i = 0; i < devices.Length; i++)
            {
                try
                {
                    using var v = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                    if (!v.IsOpened()) continue;
                    var friendly = devices[i]?.Name;
                    list.Add((i, string.IsNullOrWhiteSpace(friendly) ? $"摄像头 {i}" : friendly!.Trim()));
                }
                catch { }
            }
            if (list.Count > 0) return list;
        }
        catch {  }

        for (int i = 0; i < 8; i++)
        {
            try
            {
                using var v = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (v.IsOpened()) list.Add((i, $"摄像头 {i}"));
            }
            catch { }
        }
        return list;
    }
}

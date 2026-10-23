using System.IO.Ports;

namespace DeviceProtocolAnalyzer.Models;

public sealed record SerialSettings(
    string PortName,
    int BaudRate,
    int DataBits,
    Parity Parity,
    StopBits StopBits,
    bool DtrRtsEnable = false,
    string Transport = "serial");

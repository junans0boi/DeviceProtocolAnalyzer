namespace DeviceProtocolAnalyzer.Models;

public sealed record UsbDeviceOption(ushort VendorId, ushort ProductId, byte Bus, byte Address)
{
    public string Display => $"USB {VendorId:X4}:{ProductId:X4} (버스 {Bus}, 주소 {Address})";
}

public sealed record UsbEndpointOption(int InterfaceNumber, byte InterfaceClass, byte EndpointAddress, string TransferType, int MaxPacketSize)
{
    public bool IsIn => (EndpointAddress & 0x80) != 0;

    public string Display =>
        $"인터페이스 {InterfaceNumber}(클래스 0x{InterfaceClass:X2}) · 0x{EndpointAddress:X2} {(IsIn ? "IN" : "OUT")} · {TransferType} · {MaxPacketSize}B";
}

namespace DeviceProtocolAnalyzer.Models;

public sealed record UsbDeviceOption(ushort VendorId, ushort ProductId, byte Bus, byte Address)
{
    public string Display => $"USB {VendorId:X4}:{ProductId:X4} (버스 {Bus}, 주소 {Address})";
}

public sealed record UsbEndpointOption(int InterfaceNumber, byte EndpointAddress, string TransferType, int MaxPacketSize)
{
    public string Display => $"인터페이스 {InterfaceNumber} · 0x{EndpointAddress:X2} IN · {TransferType} · {MaxPacketSize}B";
}

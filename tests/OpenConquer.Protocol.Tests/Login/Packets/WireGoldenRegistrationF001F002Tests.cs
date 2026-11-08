using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.Protocol.Tests.Login.Packets;

public sealed class WireGoldenRegistrationF001F002Tests
{
    [Fact]
    public void RegistrationExtension_UsesPinnedProjectDefinedFrames()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "WireGolden", "F001-F002-registration.project-defined.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        Assert.Equal("project-defined", root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("runtimeCaptured").GetBoolean());
        Assert.Equal("docs/networking/FLOW-001B-account-registration.md", root.GetProperty("sourceSpecification").GetString());
        Assert.Equal("e13bf5724975ec825e64a3609719afedd9b6af41", root.GetProperty("sourceCommit").GetString());
        Assert.Equal(0x0012_34ABu, root.GetProperty("loginSeed").GetUInt32());

        JsonElement requestCase = root.GetProperty("request");
        byte[] frame = Convert.FromHexString(requestCase.GetProperty("frameHex").GetString()!);
        Assert.Equal(LoginAccountRegistrationRequestPacket.PacketIdentifier, requestCase.GetProperty("packetId").GetUInt16());
        Assert.Equal("client-to-server", requestCase.GetProperty("direction").GetString());
        Assert.Equal(LoginAccountRegistrationRequestPacket.FrameLength, frame.Length);
        Assert.Equal(frame.Length, BinaryPrimitives.ReadUInt16LittleEndian(frame));
        Assert.Equal(LoginAccountRegistrationRequestPacket.PacketIdentifier, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
        Assert.Equal(requestCase.GetProperty("frameSha256").GetString(),
            Convert.ToHexString(SHA256.HashData(frame)).ToLowerInvariant());

        Assert.True(LoginAccountRegistrationRequestPacket.TryDecode(frame.AsSpan(WireFrameHeader.Size),
            root.GetProperty("loginSeed").GetUInt32(), out LoginAccountRegistrationRequest? decoded));
        using LoginAccountRegistrationRequest request = Assert.IsType<LoginAccountRegistrationRequest>(decoded);
        Assert.Equal("testacc", request.AccountName);
        char[] password = new char[request.PasswordLength];
        request.CopyPasswordTo(password);
        Assert.Equal("password1", new string(password));
        password.AsSpan().Clear();

        int count = 0;
        foreach (JsonElement responseCase in root.GetProperty("responses").EnumerateArray())
        {
            byte[] expected = Convert.FromHexString(responseCase.GetProperty("frameHex").GetString()!);
            uint resultCode = responseCase.GetProperty("resultCode").GetUInt32();
            Assert.Equal((uint)count++, resultCode);
            Assert.Equal(LoginAccountRegistrationResponsePacket.PacketIdentifier, responseCase.GetProperty("packetId").GetUInt16());
            Assert.Equal("server-to-client", responseCase.GetProperty("direction").GetString());
            Assert.Equal(WireFrameHeader.Size + LoginAccountRegistrationResponsePacket.PayloadSize, expected.Length);
            Assert.Equal(responseCase.GetProperty("frameSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant());

            LoginAccountRegistrationResponsePacket packet = new((LoginAccountRegistrationResultCode)resultCode);
            byte[] written = new byte[expected.Length];
            Assert.Equal(expected.Length, WireFrameEncoder.WriteFrame(packet, written));
            Assert.Equal(expected, written);
        }

        Assert.Equal(5, count);
    }
}

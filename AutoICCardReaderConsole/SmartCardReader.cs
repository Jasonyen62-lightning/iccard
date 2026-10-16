using System.Text;
using System.Diagnostics;
using PCSC;
using PCSC.Iso7816;

internal static class SmartCardReader
{
    private const int CardNumberOffset = 0;
    private const int CardNumberLength = 12;
    private const int NameOffset = 12;
    private const int NameLength = 20;
    private const int IdNumberOffset = 32;
    private const int IdNumberLength = 10;
    private const int BirthdayOffset = 42;
    private const int BirthdayLength = 7;
    private const int SexOffset = 49;
    private const int SexLength = 1;
    private const int CardDateOffset = 50;
    private const int CardDateLength = 7;
    private const int NhiProfileLength = CardDateOffset + CardDateLength;

    public static string ReadAtr(string readerName, Action<string> reportStage)
    {
        var stageTimer = Stopwatch.StartNew();
        reportStage("Establishing PC/SC context to read card ATR.");
        using var context = ContextFactory.Instance.Establish(SCardScope.System);
        reportStage($"PC/SC context ready ({stageTimer.ElapsedMilliseconds} ms).");

        stageTimer.Restart();
        reportStage("Connecting to card to read ATR.");
        using var reader = context.ConnectReader(
            readerName,
            SCardShareMode.Shared,
            SCardProtocol.Any);
        reportStage($"Connected to card ({stageTimer.ElapsedMilliseconds} ms).");

        stageTimer.Restart();
        reportStage("Reading card ATR.");
        var atr = reader.GetAttrib(SCardAttribute.AtrString);
        reportStage($"Card ATR read ({stageTimer.ElapsedMilliseconds} ms).");

        if (atr.Length == 0)
        {
            throw new InvalidDataException("The card returned an empty ATR.");
        }

        return Convert.ToHexString(atr);
    }

    public static NhiCardProfile ReadNhiProfile(string readerName, Action<string> reportStage)
    {
        var stageTimer = Stopwatch.StartNew();
        reportStage("Establishing PC/SC context for card read.");
        using var context = ContextFactory.Instance.Establish(SCardScope.System);
        reportStage($"PC/SC context ready ({stageTimer.ElapsedMilliseconds} ms).");

        stageTimer.Restart();
        reportStage("Connecting to card through the reader.");
        using var reader = new IsoReader(
            context,
            readerName,
            SCardShareMode.Shared,
            SCardProtocol.Any);
        reportStage($"Connected to card ({stageTimer.ElapsedMilliseconds} ms).");

        var initializeCard = new CommandApdu(IsoCase.Case4Short, reader.ActiveProtocol)
        {
            CLA = 0x00,
            INS = 0xA4,
            P1 = 0x04,
            P2 = 0x00,
            Data = new byte[] { 0xD1, 0x58, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11 }
        };
        stageTimer.Restart();
        reportStage("Sending card initialization APDU.");
        var initializeResponse = reader.Transmit(initializeCard);
        reportStage($"Card initialization APDU returned ({stageTimer.ElapsedMilliseconds} ms).");
        stageTimer.Restart();
        EnsureSuccess(initializeResponse, "The card is not a supported NHI card.");
        reportStage($"Initialization response validated ({stageTimer.ElapsedMilliseconds} ms).");

        var readProfile = new CommandApdu(IsoCase.Case4Short, reader.ActiveProtocol)
        {
            CLA = 0x00,
            INS = 0xCA,
            P1 = 0x11,
            P2 = 0x00,
            Data = new byte[] { 0x00, 0x00 }
        };
        stageTimer.Restart();
        reportStage("Sending NHI profile APDU.");
        var profileResponse = reader.Transmit(readProfile);
        reportStage($"NHI profile APDU returned ({stageTimer.ElapsedMilliseconds} ms).");
        stageTimer.Restart();
        EnsureSuccess(profileResponse, "The NHI card profile could not be read.");

        if (!profileResponse.HasData)
        {
            throw new InvalidDataException("The NHI card returned no profile data.");
        }

        var data = profileResponse.GetData();
        if (data.Length < NhiProfileLength)
        {
            throw new InvalidDataException("The NHI card profile data is incomplete.");
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var cardNumber = DecodeField(data, CardNumberOffset, CardNumberLength, Encoding.UTF8);
        var name = DecodeField(data, NameOffset, NameLength, Encoding.GetEncoding("big5"));
        var idNumber = DecodeField(data, IdNumberOffset, IdNumberLength, Encoding.UTF8);
        var birthday = DecodeField(data, BirthdayOffset, BirthdayLength, Encoding.UTF8);
        var rawSex = DecodeField(data, SexOffset, SexLength, Encoding.UTF8);
        var sex = rawSex switch
        {
            "M" => "男",
            "F" => "女",
            _ => rawSex
        };
        var cardDate = DecodeField(data, CardDateOffset, CardDateLength, Encoding.UTF8);

        if (idNumber.Length != IdNumberLength)
        {
            throw new InvalidDataException("The NHI card ID number has an unexpected length.");
        }

        reportStage($"NHI profile fields decoded and validated ({stageTimer.ElapsedMilliseconds} ms).");
        return new NhiCardProfile(
            cardNumber,
            name,
            idNumber,
            birthday,
            sex,
            cardDate,
            Convert.ToHexString(data));
    }

    private static string DecodeField(byte[] data, int offset, int length, Encoding encoding) =>
        encoding.GetString(data, offset, length).Trim('\0', ' ', '\u3000');

    private static void EnsureSuccess(Response response, string message)
    {
        if (response.SW1 != 0x90 || response.SW2 != 0x00)
        {
            throw new InvalidOperationException(
                $"{message} (SW1={response.SW1:X2}, SW2={response.SW2:X2})");
        }
    }

    internal sealed record NhiCardProfile(
        string CardNumber,
        string Name,
        string IdNumber,
        string Birthday,
        string Sex,
        string CardDate,
        string RawDataHex);
}

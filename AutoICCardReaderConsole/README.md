# AutoICCardReaderConsole

A .NET 10 console application that monitors a PC/SC smart-card reader. By default, it reads a card's ATR and prints it as hexadecimal to standard output. Pass `--apdu` to use the Taiwan NHI-specific APDU commands. NHI profile fields are displayed as `[已遮蔽]`; raw response bytes are never printed. Timestamped diagnostics and elapsed timings are written to standard error. Card-read diagnostics also show elapsed time since the insertion event. Press `Ctrl+C` to exit.

```sh
# Generic smart-card mode: read the ATR
./AutoICCardReaderConsole

# Taiwan NHI card mode: send NHI-specific APDUs and print de-identified fields
./AutoICCardReaderConsole --apdu
```

## Publish

Run the matching PowerShell script from this directory:

```powershell
.\Publish-Windows-x64.ps1
.\Publish-Linux-x64.ps1
.\Publish-Linux-arm64.ps1
```

Each script creates a self-contained, single-file Release under `release\<runtime-id>\`. The .NET runtime is bundled; Linux still needs PC/SC Lite and a compatible reader driver. For Debian/Ubuntu systems, install them with:

```sh
sudo apt update
sudo apt install -y pcscd libccid
```

Run the Linux executable with:

```sh
chmod +x ./AutoICCardReaderConsole
./AutoICCardReaderConsole [--apdu]
```

The default ATR mode prints the card ATR. The `--apdu` mode reads the NHI profile but masks every personal-data field before printing, and does not output the raw profile bytes.

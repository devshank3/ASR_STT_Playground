# WhisperNetPlay

Windows-only sample of live English speech-to-text using [Whisper.net](https://github.com/sandrohanea/whisper.net), NAudio, and Avalonia.

This is a POC, not a product. CUDA 12 and CUDA 13 runtimes are restored by default; inference falls back to CPU if no GPU backend loads.

## Requirements

- Windows 10 or later
- .NET 10 SDK
- A microphone
- Optional: NVIDIA CUDA Toolkit (for GPU inference)

## Run

```powershell
dotnet run --project src/WhisperNetPlay/WhisperNetPlay.App
```

On first launch the app downloads `ggml-large-v3-turbo` (~1.5 GB) into `%LOCALAPPDATA%\WhisperNetPlay\models`. To point at a model you already have, set:

```powershell
$env:WHISPERNETPLAY_MODEL = "C:\path\to\ggml-large-v3-turbo.bin"
```

## Layout

```
src/WhisperNetPlay/
  WhisperNetPlay.App/       UI (Avalonia, MVVM)
  WhisperNetPlay.Speech/    microphone, model download, Whisper
```

Crash log: `%LOCALAPPDATA%\WhisperNetPlay\crash.log`  
Native Whisper log: `%LOCALAPPDATA%\WhisperNetPlay\whisper-native.log`

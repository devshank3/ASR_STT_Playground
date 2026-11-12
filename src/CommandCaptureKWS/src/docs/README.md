# Command Capture

A desktop app that shows a moving preview and saves a still when you press **Capture image** or say a phrase from the command list.

## Projects

| Project | Responsibility |
| --- | --- |
| `CommandCapture.App` | Avalonia window, view model, and the services that own the catalog file, the preview bitmap, and still capture |
| `CommandCapture.Camera` | Preview frames and PNG stills |
| `CommandCapture.Audio` | Microphone samples at 16 kHz and the level meter |
| `CommandCapture.Voice` | `commands.json`, the keyword spotter, and dispatch of a heard phrase to a handler |

`tests/CommandCapture.Voice.Tests` covers the catalog, the dispatcher, the keyword spotter, and preview stills.

The window does not contain business logic. `MainWindow.axaml` binds to `MainViewModel`. The view model keeps screen state and calls:

- `CatalogStore` for reading, saving, and watching `commands.json`
- `FramePreview` for copying each frame into the image on screen
- `StillCaptureHandler` for saving a still when the voice pipeline reports `capture_still`
- `VoicePipeline` for listening

## Run

From the repository root:

```powershell
dotnet run --project src/CommandCapture.App
```

Closing the window stops the preview and returns the terminal to the prompt.

Stills are written to `Pictures\CommandCapture`, which is the folder shown in the window. Windows may place `Pictures` under OneDrive.

## Commands file

The app reads and writes only the `commands.json` beside the executable (`src/CommandCapture.App/bin/Debug/net10.0/commands.json` while debugging).

`src/CommandCapture.App/commands.json` is the seed. The project copies it to the output folder with `PreserveNewest`, so a build replaces the output file only when the seed is newer. Phrases added in the app stay in the output file until that happens.

The file lists `directives`. Each directive is one command, such as `capture_still`, plus the phrases that trigger it. A recognized phrase becomes a `VoiceCommand`, and `IVoiceDirectiveHandler` runs that command. **Add phrase** appends one phrase and, if Listen is already on, rebuilds the keyword spotter without closing the microphone. Saving the output file in an editor reloads it the same way.

## What this build does

The preview draws a checkerboard and a new random number on every frame. A saved PNG contains that number. **Listen** and **Capture image** both save the current frame. The Heard list shows the latest match, miss, cooldown, or busy drop.

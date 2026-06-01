Shipped alert sounds live in this folder.

How sound resolution works for alert rules:
1. Absolute path (if configured).
2. %LocalAppData%\HISA\AlertSounds\<fileName> (user override).
3. Assets\Sounds\<fileName> (shipped default in app build output).

Default rule sound file name: default-alert.wav

If no playable file is found, HISA falls back to a short beep.

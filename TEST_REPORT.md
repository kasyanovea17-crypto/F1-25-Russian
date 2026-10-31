# Additional launcher checks

UI_TEST_PASS checks=187 real_game_writes=false
SPEECH_UI_PASS checks=48 host_calls=0 game_writes=false registry_writes=false
MAX_VOICE_UI_PASS checks=36 host_calls=0 game_writes=false registry_writes=false

- Baseline reproduces startup-language mismatch, keyboard targets behind dialogs and hidden path error.
- Corrected state audit:80 checks,0 findings.
- Single-EXE launch-directory hint tested separately from compatibility, and end-to-end in the wrapper.
- Engine, text, fonts, compatibility profiles and voice hosts unchanged.
- No game launch, real game/registry writes, or screenshot-capture workaround.

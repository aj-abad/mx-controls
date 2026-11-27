; The agent keeps running after the window closes, so stop it before its executable is replaced or removed.
; `--stop` asks it to exit cleanly, handing the buttons back to the mouse; taskkill covers an agent that doesn't answer.
!macro STOP_EZPZ_AGENT
  IfFileExists "$INSTDIR\ezpz-agent.exe" 0 +2
    nsExec::Exec '"$INSTDIR\ezpz-agent.exe" --stop'
  nsExec::Exec 'taskkill /F /IM ezpz-agent.exe'
!macroend

!macro NSIS_HOOK_PREINSTALL
  !insertmacro STOP_EZPZ_AGENT
!macroend

; The agent registers its sign-in task itself on first run; uninstalling removes it, and the Run key older builds used.
!macro NSIS_HOOK_PREUNINSTALL
  !insertmacro STOP_EZPZ_AGENT
  nsExec::Exec 'schtasks /Delete /TN "ezPz Agent" /F'
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "ezPz Agent"
  nsExec::Exec 'schtasks /Delete /TN "MX Studio Agent" /F'
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MX Studio Agent"
  nsExec::Exec 'schtasks /Delete /TN "MX Controls Agent" /F'
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MX Controls Agent"
!macroend

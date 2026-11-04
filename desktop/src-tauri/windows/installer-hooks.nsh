; The agent keeps running after the window closes, so stop it before its executable is replaced or removed.
; `--stop` asks it to exit cleanly, handing the buttons back to the mouse; taskkill covers an agent that doesn't answer.
!macro STOP_MX_AGENT
  IfFileExists "$INSTDIR\mx-agent.exe" 0 +2
    nsExec::Exec '"$INSTDIR\mx-agent.exe" --stop'
  nsExec::Exec 'taskkill /F /IM mx-agent.exe'
!macroend

!macro NSIS_HOOK_PREINSTALL
  !insertmacro STOP_MX_AGENT
!macroend

; The agent registers its sign-in task itself on first run; uninstalling removes it, and the Run key older builds used.
!macro NSIS_HOOK_PREUNINSTALL
  !insertmacro STOP_MX_AGENT
  nsExec::Exec 'schtasks /Delete /TN "MX Studio Agent" /F'
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MX Studio Agent"
  nsExec::Exec 'schtasks /Delete /TN "MX Controls Agent" /F'
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MX Controls Agent"
!macroend

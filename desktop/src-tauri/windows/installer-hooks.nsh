; The agent keeps running after the window closes, so stop it before its executable is replaced or removed.
; It diverts mapped buttons again when the app starts it after an update.
!macro NSIS_HOOK_PREINSTALL
  nsExec::Exec 'taskkill /F /IM mx-agent.exe'
!macroend

!macro NSIS_HOOK_PREUNINSTALL
  nsExec::Exec 'taskkill /F /IM mx-agent.exe'
!macroend

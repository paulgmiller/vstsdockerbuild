@echo off
REM Here just for back compat. prefer you use powershell and ideally we want to kill the ensure-* scripts
REM this is gross but not sure how to export env vars out of a powershell script.
powershell.exe -noprofile -command "& { .\ensure-cloudbuild.ps1; cmd.exe }" -ExecutionPolicy RemoteSigned